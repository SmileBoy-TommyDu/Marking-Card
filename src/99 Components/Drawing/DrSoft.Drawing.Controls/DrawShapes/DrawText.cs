using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using Riok.Mapperly.Abstractions;
using SkiaSharp;
using System.Diagnostics;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public class DrawText : DrawObject, IHatchable, ITextShapeData
    {
        // ── ITextShapeData：代理到 TextModel.FontSettings ────────────────────────────
        string ITextShapeData.Text => TextModel?.Text ?? string.Empty;
        string ITextShapeData.FontFamily => TextModel?.FontSettings?.FontFamily ?? string.Empty;
        float ITextShapeData.FontSize => TextModel?.FontSettings?.FontSize ?? 0f;
        bool ITextShapeData.IsBold => TextModel?.FontSettings?.IsBold ?? false;
        bool ITextShapeData.IsItalic => TextModel?.FontSettings?.IsItalic ?? false;
        float ITextShapeData.LineHeight => TextModel?.FontSettings?.LineHeight ?? 1.2f;
        float ITextShapeData.CharacterSpacing => TextModel?.FontSettings?.CharacterSpacing ?? 0f;
        // CenterX/CenterY/ChildShapes 由基类处理
        public TextModel TextModel { get; set; }

        public SKPath TextPath { get; private set; }
        public List<SKPoint[]> Contours { get; set; }

        // Underline segments in font coordinates (before GetTextPath center/flip transform)
        private List<(SKPoint Start, SKPoint End)> _underlineSegmentsFont = new();

        // Underline segments in path coordinates (after GetTextPath transform)
        public IReadOnlyList<(SKPoint Start, SKPoint End)> UnderlineSegments { get; private set; }
            = Array.Empty<(SKPoint, SKPoint)>();

        public SKPoint CurrentCenterPoint { get; set; }

        public override List<Point2D> OutlinePoints
        {
            get
            {
                if (TextPath == null || TextPath.IsEmpty)
                {
                    Contours = new List<SKPoint[]>();
                    return new List<Point2D>();
                }

                using var currentPath = GetTextPath();
                currentPath.Transform(GetTransformMatrix());

                var contours = FlattenPath(currentPath);

                var flat = new List<SKPoint>();
                foreach (var contour in contours)
                {
                    if (contour == null || contour.Length == 0)
                    {
                        continue;
                    }

                    flat.AddRange(contour);
                    //图形闭合增加点位
                    flat.Add(contour[0]);
                    flat.Add(new SKPoint(float.NaN, float.NaN));
                }

                Contours = contours;
                return TransferToPoint2D(flat);
            }
            set => throw new NotImplementedException();
        }

        public DrawText()
        {
            UId = UniqueIdGenerator.NextId();
            Type = ShapeType.Text;
        }

        public DrawText(string text, SKPoint point, TextModel textModel) : this(text, point, textModel.FontSettings) { }


        public DrawText(string text, SKPoint point, FontSettings fontSettings) : this()
        {
            TextModel = new TextModel();
            TextModel.FontSettings = DrawTextMapper.Clone(fontSettings);
            TextModel.Text = text;

            if (string.IsNullOrEmpty(TextModel.Text))
            {
                TextModel.Text = "请输入内容";
            }

            CurrentCenterPoint = point;
            UpdateSetProperty(new List<SKPoint> { CurrentCenterPoint });
            BakeFontFlipIntoMatrix();
        }

        private List<Point2D> TransferToPoint2D(List<SKPoint> points)
        {
            var list = new List<Point2D>(points.Count);

            foreach (var pt in points)
            {
                list.Add(new Point2D(pt.X, pt.Y));
            }
            return list;
        }

        /// <summary>
        /// 按新的 TextModel 重建局部文字路径。
        /// 缩放/旋转/倾斜全部保留在 Matrix 中（几何唯一真源），这里只更新局部几何：
        /// 字号、字体、内容等变化只改变局部路径，Matrix 不动，已有变换自然保留。
        /// preserveVisualPosition=true 时通过统一的 Translate 接口把视觉中心平移回原位。
        /// </summary>
        public void UpdateTextPath(
            TextModel textModel,
            bool preserveVisualPosition = false,
            bool publishTransformChange = false)
        {
            var oldCenter = GetOBB().Center;
            TextModel = textModel;

            UpdateSetProperty(new List<SKPoint> { CurrentCenterPoint });

            // 局部路径重建后 SharpCenter/Width/Height 需要按矩阵重新同步；
            // 统一走 Translate 提交（幅度可为 0），不再自己维护缓存状态。
            var newCenter = GetOBB().Center;
            float dx = preserveVisualPosition ? oldCenter.X - newCenter.X : 0f;
            float dy = preserveVisualPosition ? oldCenter.Y - newCenter.Y : 0f;
            Translate(dx, dy, true);
            SetRotationCenter(SharpCenter);

            InvalidateTextGeometryBounds();
            if (publishTransformChange)
            {
                DocumentContext.Instance.PublishTransformChange();
            }
        }

        private void InvalidateTextGeometryBounds()
        {
            _bboxDirty = true;
            NotifyBoundingBoxInvalidated();
        }

        /// <summary>
        /// 把字体坐标系（Y 向下）到画布坐标系（Y 向上）的翻转烘焙进 Matrix。
        /// 通过统一 Scale 接口以局部原点的世界像为锚做局部系 (1,-1) 缩放：
        /// 提交后 _matrix = M×Flip，世界几何逐点不变；commit:false + CommitTransform
        /// 不走 ApplyDeltaToProperties，ScaleY 等分解属性不被污染。
        /// 只能在矩阵尚未包含该翻转的确定性入口调用（构造、DRF 加载）。
        /// </summary>
        internal void BakeFontFlipIntoMatrix()
        {
            var anchorWorld = GetTransformMatrix().MapPoint(SKPoint.Empty);
            Scale(1f, -1f, anchorWorld, GetWorldRotationRad(), commit: false);
            CommitTransform();
            // CommitTransform 会用 delta 重映射 RotationCenter（世界翻转会使其偏移），
            // 世界几何未变，旋转中心应保持在同步后的 SharpCenter 上。
            SetRotationCenter(SharpCenter);
        }

        /// <summary>
        /// Resolve a typeface that can render the given text, with CJK font fallback.
        /// DXF files often specify SHX font names ("hztxt", "Standard", "txt.shx")
        /// which are not valid TTF family names. SkiaSharp does not do CJK font
        /// linking automatically, so we must find a suitable fallback.
        /// </summary>
        private static SKTypeface ResolveTypeface(string fontFamily, SKFontStyle fontStyle, string text)
        {
            // 1. Try the specified font family first
            if (!string.IsNullOrEmpty(fontFamily))
            {
                var typeface = SKTypeface.FromFamilyName(fontFamily, fontStyle);
                if (typeface != null && HasGlyphsForText(typeface, text))
                    return typeface;
            }

            return SKTypeface.FromFamilyName(null, fontStyle);
        }

        /// <summary>
        /// Check if a typeface has glyphs for the majority of characters in the text.
        /// </summary>
        private static bool HasGlyphsForText(SKTypeface typeface, string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            using var font = new SKFont(typeface, 12f);
            var glyphs = font.GetGlyphs(text);
            int missing = 0;
            int checked_ = 0;
            for (int i = 0; i < glyphs.Length; i++)
            {
                if (text[i] <= 0x1F || char.IsWhiteSpace(text[i])) continue;
                checked_++;
                if (glyphs[i] == 0) missing++;  // glyph id 0 = .notdef (missing)
                if (missing > checked_ / 3) return false;
            }
            return checked_ == 0 || missing <= checked_ / 3;
        }

        private SKPath GetTextPath()
        {
            var fontStyle = new SKFontStyle(
                TextModel.FontSettings.IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                TextModel.FontSettings.IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright
            );

            var typeface = ResolveTypeface(TextModel.FontSettings.FontFamily, fontStyle, TextModel.Text);
            if (typeface == null) return null;

            using var font = new SKFont(typeface, TextModel.FontSettings.FontSize);
            var path = BuildTextLayoutPath(font);
            if (path == null || path.IsEmpty) return path;

            var bounds = path.Bounds;

            // HorizontalAlign only affects line offsets inside a multi-line layout.
            // The whole text path keeps a stable left-origin so single-line text does not move.
            float transX = -bounds.Left;
            float transY = TextModel?.FontSettings?.VerticalAlign switch
            {
                0 => 0f,                               // Baseline: 第一行基线在原点
                1 => -bounds.Bottom,                   // Bottom: 文本底部在原点
                3 => -bounds.Top,                      // Top: 文本顶部在原点
                _ => -bounds.MidY,                     // Middle (默认): 文本中心在原点
            };

            // 局部路径保持字体坐标系（Y 向下），这里只做纯布局平移归一化；
            // 字体系到画布系的 Y 翻转属于对象级变换，由 BakeFontFlipIntoMatrix
            // 经统一 Scale 接口烘焙进 Matrix，不再自建变换矩阵。

            // 下划线几何已在 Build*TextPath 内直接写入 path（与字形同源、同一次构建），
            // 这里只把线段坐标同步换算到归一化后的路径坐标系，供外部读取。
            var segments = _underlineSegmentsFont.ToArray(); // 快照，线程安全的只读副本
            if (segments.Length > 0)
            {
                var transformed = new (SKPoint, SKPoint)[segments.Length];
                for (int i = 0; i < segments.Length; i++)
                {
                    var (s, e) = segments[i];
                    transformed[i] = (
                        new SKPoint(s.X + transX, s.Y + transY),
                        new SKPoint(e.X + transX, e.Y + transY));
                }
                UnderlineSegments = transformed;
            }
            else
            {
                UnderlineSegments = Array.Empty<(SKPoint, SKPoint)>();
            }

            path.Offset(transX, transY);

            return path;
        }

        private SKPath BuildTextLayoutPath(SKFont font)
        {
            if (string.IsNullOrEmpty(TextModel?.Text))
            {
                return new SKPath();
            }

            return TextModel.FontSettings.IsVerticalLayout
                ? BuildVerticalTextPath(font)
                : BuildHorizontalTextPath(font);
        }

        private SKPath BuildHorizontalTextPath(SKFont font)
        {
            var lines = SplitLines(TextModel.Text);
            if (lines.Length == 0)
            {
                return new SKPath();
            }

            float characterSpacing = TextModel.FontSettings.CharacterSpacing;
            var lineWidths = new float[lines.Length];
            float maxLineWidth = 0f;
            for (int i = 0; i < lines.Length; i++)
            {
                lineWidths[i] = MeasureLineWidth(font, lines[i], characterSpacing);
                if (lineWidths[i] > maxLineWidth)
                {
                    maxLineWidth = lineWidths[i];
                }
            }

            float lineHeightMultiplier = TextModel.FontSettings.LineHeight > 0f
                ? TextModel.FontSettings.LineHeight
                : 1.2f;
            float lineStep = TextModel.FontSettings.FontSize * lineHeightMultiplier;

            var combinedPath = new SKPath();
            for (int i = 0; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i]))
                {
                    continue;
                }

                using var linePath = BuildLinePath(font, lines[i], characterSpacing);
                if (linePath == null || linePath.IsEmpty)
                {
                    continue;
                }

                float xOffset = GetLineXOffset(maxLineWidth, lineWidths[i]);
                float yOffset = i * lineStep;
                using var translatedLinePath = new SKPath(linePath);
                translatedLinePath.Transform(SKMatrix.CreateTranslation(xOffset, yOffset));
                combinedPath.AddPath(translatedLinePath);
            }

            // 下划线段与字形同一次构建：直接写入 path，使 GetTextPath 的归一化边界、
            // AABB/OBB（GetPath().TightBounds）天然包含下划线；字段仅整体换引用，
            // 避免并发调用时 Clear/Add 竞态导致快照为空、包围盒丢失下划线。
            // 注意：Skia 的 TightBounds 在路径含其他轮廓时会丢弃零面积的开放线段，
            // 因此下划线必须写成极窄闭合矩形（而非 Move+Line）才能被紧边界统计。
            float underlineOffsetFont = TextModel.FontSettings.FontSize * 0.1f;
            float underlineHalfThickness = TextModel.FontSettings.FontSize * 0.001f;
            var underlineSegments = new List<(SKPoint Start, SKPoint End)>();
            if (TextModel.FontSettings.IsUnderline)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrEmpty(lines[i])) continue;
                    float xOffset = GetLineXOffset(maxLineWidth, lineWidths[i]);
                    float underlineY = i * lineStep + underlineOffsetFont;
                    var start = new SKPoint(xOffset, underlineY);
                    var end = new SKPoint(xOffset + lineWidths[i], underlineY);
                    underlineSegments.Add((start, end));
                    combinedPath.AddRect(new SKRect(
                        start.X, underlineY - underlineHalfThickness,
                        end.X, underlineY + underlineHalfThickness));
                }
            }
            _underlineSegmentsFont = underlineSegments;

            return combinedPath;
        }

        private SKPath BuildVerticalTextPath(SKFont font)
        {
            var columns = SplitLines(TextModel.Text);
            if (columns.Length == 0) return new SKPath();

            float charSpacing = TextModel.FontSettings.CharacterSpacing;
            float columnSpacingMultiplier = TextModel.FontSettings.LineHeight > 0f
                ? TextModel.FontSettings.LineHeight
                : 1.2f;

            // 固定每个字符的垂直占位高度（使用字体大小）
            float fixedCharHeight = TextModel.FontSettings.FontSize;
            // 或者使用 font.Metrics 获取统一高度：
            // var metrics = font.GetMetrics();
            // float fixedCharHeight = metrics.Descent - metrics.Ascent;

            var columnLayouts = new List<ColumnLayout>(columns.Length);
            float maxColumnHeight = 0f;

            foreach (var columnText in columns)
            {
                var glyphLayouts = new List<GlyphLayout>();
                float columnWidth = MathF.Max(TextModel.FontSettings.FontSize, 1f);
                // 列高 = 字符数 * (固定高度 + 间距) - 间距
                float columnHeight = columnText.Length * (fixedCharHeight + charSpacing) - charSpacing;
                if (columnText.Length == 0) columnHeight = 0;

                for (int i = 0; i < columnText.Length; i++)
                {
                    string ch = columnText[i].ToString();
                    float advance = font.MeasureText(ch);
                    float glyphWidth = MeasureGlyphWidth(font, ch);
                    // 不再需要 MeasureGlyphHeight，但我们可以保留用于居中计算
                    float glyphHeight = MeasureGlyphHeight(font, ch);

                    glyphLayouts.Add(new GlyphLayout
                    {
                        Text = ch,
                        Advance = advance,
                        Width = glyphWidth,
                        Height = glyphHeight // 仍保留实际高度，用于居中偏移
                    });

                    columnWidth = MathF.Max(columnWidth, glyphWidth);
                    // 不再累加高度
                }

                columnLayouts.Add(new ColumnLayout
                {
                    Glyphs = glyphLayouts,
                    Width = columnWidth,
                    Height = MathF.Max(columnHeight, TextModel.FontSettings.FontSize) // 至少一个字符高
                });
                maxColumnHeight = MathF.Max(maxColumnHeight, columnHeight);
            }

            var path = new SKPath();
            float currentX = 0f;
            var underlineSegments = new List<(SKPoint Start, SKPoint End)>();

            for (int colIndex = 0; colIndex < columnLayouts.Count; colIndex++)
            {
                var column = columnLayouts[colIndex];
                // 列垂直起始偏移（居中）
                float columnStartY = GetLineXOffset(maxColumnHeight, column.Height);
                float currentY = columnStartY;

                bool hasVisualBounds = false;
                float minX = float.MaxValue, maxX = float.MinValue;
                float minY = float.MaxValue, maxY = float.MinValue;

                foreach (var glyph in column.Glyphs)
                {
                    using var glyphPath = font.GetTextPath(glyph.Text);
                    if (glyphPath != null && !glyphPath.IsEmpty)
                    {
                        // 水平居中
                        float glyphX = currentX + GetLineXOffset(column.Width, glyph.Width);

                        // 垂直居中：计算字符路径边界，然后平移到当前固定方块的中心
                        var bounds = glyphPath.Bounds;
                        float glyphCenterY = (bounds.Top + bounds.Bottom) / 2;
                        float blockCenterY = currentY + fixedCharHeight / 2;
                        float translateY = blockCenterY - glyphCenterY;

                        using var transformed = new SKPath(glyphPath);
                        transformed.Transform(SKMatrix.CreateTranslation(glyphX, translateY));
                        path.AddPath(transformed);

                        // 更新下划线边界（使用实际边界）
                        var glyphBounds = transformed.Bounds;
                        minX = MathF.Min(minX, glyphBounds.Left);
                        maxX = MathF.Max(maxX, glyphBounds.Right);
                        minY = MathF.Min(minY, glyphBounds.Top);
                        maxY = MathF.Max(maxY, glyphBounds.Bottom);
                        hasVisualBounds = true;
                    }

                    // 步进固定高度
                    currentY += fixedCharHeight + charSpacing;
                }

                if (TextModel.FontSettings.IsUnderline && hasVisualBounds)
                {
                    float underlineOffsetFont = TextModel.FontSettings.FontSize * 0.1f;
                    float underlineHalfThickness = TextModel.FontSettings.FontSize * 0.001f;
                    float underlineX = maxX + underlineOffsetFont;
                    var start = new SKPoint(underlineX, minY);
                    var end = new SKPoint(underlineX, maxY);
                    underlineSegments.Add((start, end));
                    // 写成极窄闭合矩形直接入 path：开放 Move+Line 线段会被
                    // TightBounds 丢弃，导致旋转后 AABB/OBB 不含下划线（见水平版说明）
                    path.AddRect(new SKRect(
                        underlineX - underlineHalfThickness, minY,
                        underlineX + underlineHalfThickness, maxY));
                }

                if (colIndex < columnLayouts.Count - 1)
                    currentX += column.Width * columnSpacingMultiplier;
            }

            _underlineSegmentsFont = underlineSegments;
            return path;
        }

        private float MeasureGlyphWidth(SKFont font, string text)
        {
            using var path = font.GetTextPath(text);
            if (path != null && !path.IsEmpty)
            {
                return MathF.Max(path.Bounds.Width, 0f);
            }

            return MathF.Max(font.MeasureText(text), TextModel.FontSettings.FontSize * 0.5f);
        }

        private float MeasureGlyphHeight(SKFont font, string text)
        {
            using var path = font.GetTextPath(text);
            if (path != null && !path.IsEmpty)
            {
                return MathF.Max(path.Bounds.Height, 0f);
            }

            return MathF.Max(TextModel.FontSettings.FontSize, 1f);
        }

        private sealed class ColumnLayout
        {
            public List<GlyphLayout> Glyphs { get; init; } = new();
            public float Width { get; init; }
            public float Height { get; init; }
        }

        private sealed class GlyphLayout
        {
            public string Text { get; init; }
            public float Advance { get; init; }
            public float Width { get; init; }
            public float Height { get; init; }
        }

        private float MeasureLineWidth(SKFont font, string line, float characterSpacing)
        {
            if (string.IsNullOrEmpty(line))
            {
                return 0f;
            }

            using var linePath = BuildLinePath(font, line, characterSpacing);
            if (linePath != null && !linePath.IsEmpty)
            {
                var lineBounds = linePath.Bounds;
                var lineWidth = MathF.Max(lineBounds.Width, 0f);
                return lineWidth;
            }

            float width = 0f;
            for (int i = 0; i < line.Length; i++)
            {
                width += font.MeasureText(line[i].ToString());
                if (i < line.Length - 1)
                {
                    width += characterSpacing;
                }
            }

            return width;
        }

        private SKPath BuildLinePath(SKFont font, string line, float characterSpacing)
        {
            var linePath = new SKPath();
            float currentX = 0f;

            for (int i = 0; i < line.Length; i++)
            {
                string character = line[i].ToString();
                using var glyphPath = font.GetTextPath(character);
                if (glyphPath != null && !glyphPath.IsEmpty)
                {
                    using var translatedGlyphPath = new SKPath(glyphPath);
                    translatedGlyphPath.Transform(SKMatrix.CreateTranslation(currentX, 0f));
                    linePath.AddPath(translatedGlyphPath);
                }

                currentX += font.MeasureText(character);
                if (i < line.Length - 1)
                {
                    currentX += characterSpacing;
                }
            }

            if (linePath.IsEmpty)
            {
                return linePath;
            }

            var lineBounds = linePath.Bounds;
            var hasHorizontalOffset = Math.Abs(lineBounds.Left) > 0.001f;
            if (hasHorizontalOffset)
            {
                linePath.Transform(SKMatrix.CreateTranslation(-lineBounds.Left, 0f));
            }

            return linePath;
        }

        private string[] SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }

        private float GetLineXOffset(float maxLineWidth, float lineWidth)
        {
            var align = TextModel?.FontSettings?.HorizontalAlign ?? SKTextAlign.Left;

            return align switch
            {
                SKTextAlign.Center => (maxLineWidth - lineWidth) / 2f,
                SKTextAlign.Right => maxLineWidth - lineWidth,
                _ => 0f
            };
        }

        public override SKPath GetPath()
        {
            return GetTextPath();
        }

        //public override SKRect GetLocalBounds()
        //{
        //    // 下划线已在 Build*TextPath 内写入路径，Bounds 天然包含，无需再单独合并
        //    return GetTextPath()?.Bounds ?? base.GetLocalBounds();
        //}

        #region 输入文字轮廓路径并展平为线段
        private List<SKPoint[]> FlattenPath(SKPath path)
        {
            var contours = new List<SKPoint[]>();


            // 使用 using 语句确保迭代器被正确释放
            using (var iter = path.CreateRawIterator())
            {
                // 用于存储每个动词的点数据，最多支持4个点（如贝塞尔曲线）
                var points = new SKPoint[4];
                SKPathVerb verb;
                var currentContour = new List<SKPoint>();
                // 循环遍历路径中的所有绘图指令
                while ((verb = iter.Next(points)) != SKPathVerb.Done)
                {
                    switch (verb)
                    {
                        case SKPathVerb.Move:
                            // 移动画笔，不绘制线条
                            if (currentContour.Count > 0)
                            {
                                contours.Add(currentContour.ToArray());
                                currentContour.Clear();
                            }
                            currentContour.Add(points[0]);
                            break;
                        case SKPathVerb.Line:
                            // 绘制直线
                            currentContour.Add(points[1]);
                            break;
                        case SKPathVerb.Quad:
                        case SKPathVerb.Conic:
                        case SKPathVerb.Cubic:
                            // 曲线近似为线段
                            var flattened = FlattenCurve(points, verb, (float)GlobalVariableManagement.Resolution);
                            currentContour.AddRange(flattened.Skip(1));
                            break;
                        case SKPathVerb.Close:
                            // 闭合路径
                            if (currentContour.Count > 0)
                            {
                                contours.Add(currentContour.ToArray());
                                currentContour.Clear();
                            }
                            break;
                    }
                }
                if (currentContour.Count > 0)
                    contours.Add(currentContour.ToArray());
            }
            return contours;
        }

        /// <summary>
        /// 使用 SKPathMeasure 按弧长采样展平 SKPath 的每一条轮廓（对齐 DrawBezier.OutlinePoints 的展平方式）。
        /// 每个采样点都严格落在原始曲线上，不依赖弦偏近似，从而使扫描线与轮廓边的交点
        /// 与真实字符曲线边界几乎重合。
        /// </summary>
        /// <param name="path">待展平的 TextPath（可含多个开/闭合轮廓）</param>
        /// <param name="stepMm">弧长采样步长，默认 0.01mm</param>
        private List<SKPoint[]> FlattenPathByArcLength(SKPath path)
        {
            //统一曲线采样步长为全局采样精度
            float stepMm = (float)GlobalVariableManagement.Resolution;
            var contours = new List<SKPoint[]>();
            if (path == null || path.IsEmpty || stepMm <= 0f) return contours;

            using var measure = new SKPathMeasure(path);
            do
            {
                float length = measure.Length;
                if (length <= stepMm) continue;

                int approxCount = Math.Max(8, (int)(length / stepMm) + 2);
                var pts = new List<SKPoint>(approxCount);

                // 沿弧长等间距采样
                for (float d = 0f; d < length; d += stepMm)
                {
                    if (measure.GetPosition(d, out var pt))
                        pts.Add(pt);
                }

                // 如果最后一个采样点与轮廓末端距离 > 微小阈值，补上末端点（保证闭合轮廓的几何完整）
                if (measure.GetPosition(length, out var endPt))
                {
                    if (pts.Count == 0)
                    {
                        pts.Add(endPt);
                    }
                    else
                    {
                        var last = pts[pts.Count - 1];
                        float dx = last.X - endPt.X;
                        float dy = last.Y - endPt.Y;
                        if (dx * dx + dy * dy > 1e-10f)
                            pts.Add(endPt);
                    }
                }

                if (pts.Count >= 3)
                    contours.Add(pts.ToArray());
            }
            while (measure.NextContour());

            return contours;
        }

        // 曲线展平为线段
        private IEnumerable<SKPoint> FlattenCurve(SKPoint[] points, SKPathVerb verb, float tolerance)
        {
            // 这里只处理二次和三次贝塞尔，实际可根据需要扩展
            if (verb == SKPathVerb.Quad)
            {
                return FlattenQuad(points[0], points[1], points[2], tolerance);
            }
            else if (verb == SKPathVerb.Cubic)
            {
                return FlattenCubic(points[0], points[1], points[2], points[3], tolerance);
            }
            else if (verb == SKPathVerb.Conic)
            {
                // 简化处理为二次贝塞尔
                return FlattenQuad(points[0], points[1], points[2], tolerance);
            }
            return Array.Empty<SKPoint>();
        }

        private IEnumerable<SKPoint> FlattenQuad(SKPoint p0, SKPoint p1, SKPoint p2, float tolerance)
        {
            // 递归细分二次贝塞尔
            var list = new List<SKPoint> { p0 };
            FlattenQuadRecursive(p0, p1, p2, tolerance, list);
            list.Add(p2);
            return list;
        }

        private void FlattenQuadRecursive(SKPoint p0, SKPoint p1, SKPoint p2, float tolerance, List<SKPoint> list)
        {
            var mid = MidPoint(p0, p2);
            var ctrl = MidPoint(p0, p1);
            var ctrl2 = MidPoint(p1, p2);
            var midCtrl = MidPoint(ctrl, ctrl2);

            if (Distance(mid, midCtrl) < tolerance)
                return;

            FlattenQuadRecursive(p0, ctrl, midCtrl, tolerance, list);
            list.Add(midCtrl);
            FlattenQuadRecursive(midCtrl, ctrl2, p2, tolerance, list);
        }

        private IEnumerable<SKPoint> FlattenCubic(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float tolerance)
        {
            // 递归细分三次贝塞尔
            var list = new List<SKPoint> { p0 };
            FlattenCubicRecursive(p0, p1, p2, p3, tolerance, list);
            list.Add(p3);
            return list;
        }

        private void FlattenCubicRecursive(SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float tolerance, List<SKPoint> list)
        {
            var mid = MidPoint(p0, p3);
            var ctrl1 = MidPoint(p0, p1);
            var ctrl2 = MidPoint(p1, p2);
            var ctrl3 = MidPoint(p2, p3);
            var mid1 = MidPoint(ctrl1, ctrl2);
            var mid2 = MidPoint(ctrl2, ctrl3);
            var midCtrl = MidPoint(mid1, mid2);

            if (Distance(mid, midCtrl) < tolerance)
                return;

            FlattenCubicRecursive(p0, ctrl1, mid1, midCtrl, tolerance, list);
            list.Add(midCtrl);
            FlattenCubicRecursive(midCtrl, mid2, ctrl3, p3, tolerance, list);
        }

        private SKPoint MidPoint(SKPoint a, SKPoint b)
        {
            return new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        }

        private float Distance(SKPoint a, SKPoint b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
        #endregion

        #region 填充
        public List<Point2D> GenerateHatchLines(List<SKPoint[]> contours, float hatchDistance, float hatchAngleDeg)
        {
            var allPoints = new List<Point2D>();
            var bounds = GetBoundingBox(contours);
            float angleRad = hatchAngleDeg * (float)Math.PI / 180;
            float cos = (float)Math.Cos(angleRad);
            float sin = (float)Math.Sin(angleRad);

            // 计算扫描线方向上的投影范围
            float minProj = DotProduct(new SKPoint(bounds.Left, bounds.Top), cos, sin);
            float maxProj = DotProduct(new SKPoint(bounds.Right, bounds.Bottom), cos, sin);
            if (minProj > maxProj) (minProj, maxProj) = (maxProj, minProj);

            for (float proj = minProj; proj <= maxProj; proj += hatchDistance)
            {
                var intersections = new List<SKPoint>();
                foreach (var contour in contours)
                {
                    var pts = FindIntersectionPoints(contour, proj, cos, sin);
                    intersections.AddRange(pts);
                }

                if (intersections.Count >= 2)
                {
                    // 沿扫描线方向排序
                    intersections.Sort((a, b) => DotProduct(a, cos, sin).CompareTo(DotProduct(b, cos, sin)));
                    for (int i = 0; i < intersections.Count - 1; i += 2)
                    {
                        SKPoint p1 = intersections[i];
                        SKPoint p2 = intersections[i + 1];
                        // 生成从 p1 到 p2 的激光点（此处简化：直接添加两个端点，实际应插值）
                        allPoints.Add(new Point2D(p1.X, p1.Y));
                        allPoints.Add(new Point2D(p2.X, p2.Y));
                    }
                }
            }
            return allPoints;
        }

        public static SKRect GetBoundingBox(List<SKPoint[]> contours)
        {
            if (contours == null || contours.Count == 0)
                return SKRect.Empty;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var contour in contours)
            {
                foreach (var point in contour)
                {
                    if (point.X < minX) minX = point.X;
                    if (point.Y < minY) minY = point.Y;
                    if (point.X > maxX) maxX = point.X;
                    if (point.Y > maxY) maxY = point.Y;
                }
            }
            return SKRect.Create(minX, minY, maxX - minX, maxY - minY);
        }

        public static float DotProduct(SKPoint p, float cos, float sin)
        {
            // 将点 (p.X, p.Y) 投影到方向向量 (cos, sin) 上
            return p.X * cos + p.Y * sin;
        }

        public static void FindIntersections(SKPoint[] contour, float proj, float cos, float sin, List<float> intersections)
        {
            for (int i = 0; i < contour.Length; i++)
            {
                SKPoint p1 = contour[i];
                SKPoint p2 = contour[(i + 1) % contour.Length]; // 闭合轮廓

                // 计算两个端点在扫描线方向上的投影值
                float t1 = DotProduct(p1, cos, sin);
                float t2 = DotProduct(p2, cos, sin);

                // 检查线段是否跨越扫描线（包括端点在扫描线上的情况）
                if ((t1 - proj) * (t2 - proj) < 0 || Math.Abs(t1 - proj) < 1e-6 || Math.Abs(t2 - proj) < 1e-6)
                {
                    // 计算交点参数 t（0~1 之间）
                    float t = (proj - t1) / (t2 - t1);
                    if (t >= 0 && t <= 1)
                    {
                        // 计算交点的实际坐标（沿扫描线方向的位置）
                        float intersectCoord = p1.X + t * (p2.X - p1.X);  // 或使用 (p1.Y + t*(p2.Y-p1.Y))
                        intersections.Add(intersectCoord);
                    }
                }
            }
        }

        /// <summary>
        /// 计算线段 (p1, p2) 与扫描线的交点，其中扫描线由参数 t 定义（p = p1 + t*(p2-p1)）
        /// </summary>
        /// <param name="p1">线段起点</param>
        /// <param name="p2">线段终点</param>
        /// <param name="t">交点参数，范围 [0,1]</param>
        /// <returns>交点坐标</returns>
        public static SKPoint ComputeIntersectionPoint(SKPoint p1, SKPoint p2, float t)
        {
            return new SKPoint(
                p1.X + t * (p2.X - p1.X),
                p1.Y + t * (p2.Y - p1.Y)
            );
        }

        public static List<SKPoint> FindIntersectionPoints(SKPoint[] contour, float proj, float cos, float sin, float eps = 1e-6f)
        {
            var intersections = new List<SKPoint>();
            int n = contour.Length;

            for (int i = 0; i < n; i++)
            {
                SKPoint p1 = contour[i];
                SKPoint p2 = contour[(i + 1) % n]; // 闭合轮廓

                float t1 = p1.X * cos + p1.Y * sin;
                float t2 = p2.X * cos + p2.Y * sin;

                // 检查是否跨越扫描线（允许端点容差）
                bool cross = (t1 - proj) * (t2 - proj) < 0;
                bool onStart = Math.Abs(t1 - proj) < eps;

                if (cross || onStart)
                {
                    // 避免除零（线段与扫描线平行时 t2 - t1 ≈ 0）
                    if (Math.Abs(t2 - t1) < eps)
                        continue;

                    float t = (proj - t1) / (t2 - t1);
                    // 钳位 t 到 [0,1] 范围（由于浮点误差）
                    t = Math.Clamp(t, 0f, 1f);

                    SKPoint intersect = new SKPoint(
                        p1.X + t * (p2.X - p1.X),
                        p1.Y + t * (p2.Y - p1.Y)
                    );
                    intersections.Add(intersect);
                }
            }

            return intersections;
        }

        #endregion


        public override IShape Clone()
        {
            var clone = new DrawText(TextModel.Text, CurrentCenterPoint, TextModel.FontSettings)
            {
                HatchParamInfo = HatchParamInfo,
                TextModel = new TextModel
                {
                    Text = TextModel.Text,
                    FontSettings = DrawTextMapper.Clone(TextModel.FontSettings)
                },
            };

            // 关键：变换矩阵（含平移/旋转/缩放/倾斜）才是几何唯一真源。
            // 仅复制 Rotation/ScaleX/SkewX 等属性不会重建 Matrix（它们只是解码值），
            // 会导致克隆体丢失旋转/缩放/倾斜——文本被倾斜并缩放后再导出（走克隆），
            // 导出的 OutlinePoints 就会缺失这些变换，重新导入的多段线随之错位。
            return FinalizeClone(clone);
        }

        public override bool HitTest(SKPoint point, float tolerance = 6)
        {
            return base.HitTest(point, tolerance);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            return base.IntersectsWith(rect);
        }

        public override void UpdateSetProperty(List<SKPoint> points)
        {
            if (string.IsNullOrEmpty(TextModel.Text)) return;

            Points = points;
            Type = ShapeType.Text;
            TextPath = GetTextPath();
            if (TextPath == null || TextPath.IsEmpty) return;


            var anchorPoint = points.FirstOrDefault();
            CurrentCenterPoint = anchorPoint;

            SetRotationCenter(SharpCenter);

            InvalidateTextGeometryBounds();
        }

        internal override List<IShape> CreateCurveChildren()
        {
            return [this];
        }

        #region 填充
        // 填充
        public HatchParamDto HatchParamInfo { get; set; }
        public List<DrawObject> ExpandHatchObject(List<(SKPoint Start, SKPoint End)> hatchLineObjects)
        {
            if (HatchParamInfo == null) throw new ArgumentNullException("填充参数为null！");
            List<DrawObject> result = new List<DrawObject>();
            switch (HatchParamInfo.FillTypeIndex)
            {
                case 0:
                    throw new Exception("实线无需解析！");
                case 1:
                    result.AddRange(HatchRenderHelper.ExpandToDashGeometry(DashRenderType.Dash, hatchLineObjects,
                        HatchRenderHelper.GetDashParameters(HatchParamInfo.FillTypeIndex), SKColor.Parse(HatchParamInfo.FillColor), Name));
                    break;

                case 2:
                    result.AddRange(HatchRenderHelper.ExpandToDashGeometry(DashRenderType.Dot, hatchLineObjects,
                   HatchRenderHelper.GetDashParameters(HatchParamInfo.FillTypeIndex), SKColor.Parse(HatchParamInfo.FillColor), Name));
                    break;
            }

            return result;
        }

        /// <summary>
        /// 文本直线填充：对 TextPath 展平后的多轮廓（含字符内部空洞）做扫描线填充。
        /// 支持参数：Margin/LineSpacing/StartAngle/IncrementalAngle/Count/Extension/FillTypeIndex/ReverseFillLine。
        /// 返回的线段已位于局部坐标系（TextPath 已在 GetTextPath 中做中心对齐变换）。
        /// </summary>
        public HatchPatternObjects CreateHatchPattern()
        {
            if (HatchParamInfo == null) return new HatchPatternObjects();

            // 与渲染端保持一致：TextRenderer.Render 走的是 text.GetPath() → GetTextPath()，
            // 每帧按当前 Width/Height 重新生成路径；而此前填充一直复用 UpdateSetProperty 里
            // 缓存的 TextPath 字段，导致交互变换（拖拽缩放/剪切/平移）时轮廓已更新、填充仍
            // 基于旧尺寸的缓存路径，两者边框不重合。这里刷新缓存，确保填充基于最新几何。
            TextPath = GetTextPath();
            if (TextPath == null || TextPath.IsEmpty) return new HatchPatternObjects();

            // 1. 获取基础数据（Extension / ReverseFillLine 已在 GenerateTextScanlineFill 内部处理）
            var fillLines = GetFillLines(HatchParamInfo);

            var drawObjects = FillLineStyleEmitter.Convert3(fillLines, HatchParamInfo, Name);
            return new HatchPatternObjects
            {
                HatchObjects = drawObjects,
                HatchLineObjects = fillLines,
            };
        }
        /// <summary>
        /// 获取填充线段。返回的线段在**本地坐标系**
        /// 中心为原点，x∈[-W/2, W/2]，y∈[-H/2, H/2]（Y 轴向上）。
        /// 根据 FillTypeIndex 分发到不同的填充算法。
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo == null || TextPath == null || TextPath.IsEmpty)
                return result;

            return hatchInfo.FillTypeIndex switch
            {
                0 => GenerateTextScanlineFill2(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                1 => GenerateTextScanlineFill2(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                _ => new List<(SKPoint, SKPoint)>(),      // 其他
            };
        }
        /// <summary>
        /// 多轮廓扫描线填充：将所有轮廓按 -angle 旋转使填充方向水平，对所有边联合应用
        /// odd-even 规则求全域交点，再跟进边-胶囊禁区对填充段中减去边距 margin 的区间，
        /// 最后应用 Extension / ReverseFillLine / FillTypeIndex(S双向) 并旋转回原系。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GenerateTextScanlineFill(
             HatchParamDto info)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (info == null || TextPath == null || TextPath.IsEmpty) return result;

            // 将 TextPath 展平为多轮廓（容许有字符内部空洞）
            // 使用 SKPathMeasure 按弧长采样（与 DrawBezier 一致，精度 0.01mm），
            // 这样多边形顶点严格落在字符曲线上，扫描线与边的交点
            // 与真实曲线边界几乎完全重合，避免线端与字体轮廓偏离。
            List<SKPoint[]> contours = FlattenPathByArcLength(TextPath);
            if (contours == null || contours.Count == 0) return result;

            if (info.LineSpacing <= 0) return result;

            double angleDeg = info.StartAngle;
            float margin = (float)info.Margin;
            float spacing = (float)info.LineSpacing;
            float extension = (float)info.Extension;
            bool reverseAll = info.ReverseFillLine;
            // FillTypeIndex：0 = S型单向（所有扫描线同向），1 = S型双向（相邻行交替反向）。
            bool bidirectional = info.FillTypeIndex == 1;
            bool relativeToAngle = info.RelativeToAngle;
            // 旋转所有轮廓让填充方向水平
            //double rad = -angleDeg * Math.PI / 180.0;
            //double rad = -(relativeToAngle ? angleDeg : angleDeg + Rotation) * Math.PI / 180.0;
            double rad = -(relativeToAngle ? angleDeg : angleDeg - Rotation) * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            var rotated = new List<SKPoint[]>(contours.Count);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in contours)
            {
                if (c == null || c.Length < 3) continue;
                var rc = new SKPoint[c.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    float x = (float)(c[i].X * cos - c[i].Y * sin);
                    float y = (float)(c[i].X * sin + c[i].Y * cos);
                    rc[i] = new SKPoint(x, y);
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                rotated.Add(rc);
            }
            if (rotated.Count == 0 || minY >= maxY) return result;

            // AverageDistribute ：将 LineSpacing 作为目标值，重算间距使扫描线在 [minY, maxY]
            // 区间均等分布；将 span 平均分成 nGaps 份，生成 nGaps-1 条填充线，
            // 使“边界→首线 / 线间 / 尾线→边界”的间距全部相等 = span / nGaps
            float startOffset = spacing / 2f;
            float yLimit = maxY;
            if (info.AverageDistribute)
            {
                float span = maxY - minY;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startOffset = spacing;
                yLimit = maxY - spacing * 0.5f;
            }

            double cosBack = Math.Cos(-rad), sinBack = Math.Sin(-rad);
            var xs = new List<float>(32);
            var forbidden = new List<(float Start, float End)>(64);
            var segs = new List<(float A, float B)>(16);

            int lineIndex = 0;
            for (float y = minY + startOffset; y < yLimit; y += spacing, lineIndex++)
            {
                // 1) 收集所有轮廓边与扫描线的 x 交点（odd-even 跨轮廓合并，自然处理字符孔洞）
                xs.Clear();
                foreach (var rc in rotated)
                {
                    int n = rc.Length;
                    for (int i = 0; i < n; i++)
                    {
                        var p1 = rc[i];
                        var p2 = rc[(i + 1) % n];
                        if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                        {
                            float t = (y - p1.Y) / (p2.Y - p1.Y);
                            xs.Add(p1.X + t * (p2.X - p1.X));
                        }
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                // 2) 求扫描线与每条边 margin-胶囊 的 x 区间并集（禁区）
                forbidden.Clear();
                if (margin > 0)
                {
                    foreach (var rc in rotated)
                    {
                        int n = rc.Length;
                        for (int i = 0; i < n; i++)
                        {
                            var p1 = rc[i];
                            var p2 = rc[(i + 1) % n];
                            if (TrySegmentCapsuleXRange(p1.X, p1.Y, p2.X, p2.Y, y, margin, out float fMin, out float fMax))
                                forbidden.Add((fMin, fMax));
                        }
                    }
                    if (forbidden.Count > 1)
                    {
                        forbidden.Sort((a, b) => a.Start.CompareTo(b.Start));
                        int w = 0;
                        for (int r = 1; r < forbidden.Count; r++)
                        {
                            if (forbidden[r].Start <= forbidden[w].End)
                            {
                                if (forbidden[r].End > forbidden[w].End)
                                    forbidden[w] = (forbidden[w].Start, forbidden[r].End);
                            }
                            else
                            {
                                w++;
                                forbidden[w] = forbidden[r];
                            }
                        }
                        forbidden.RemoveRange(w + 1, forbidden.Count - w - 1);
                    }
                }

                // 3) 从实心填充段中减去禁区，得到本行有效子段
                segs.Clear();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float a = xs[i], b = xs[i + 1];
                    if (b <= a) continue;
                    float cur = a;
                    for (int k = 0; k < forbidden.Count; k++)
                    {
                        var (fs, fe) = forbidden[k];
                        if (fe <= cur) continue;
                        if (fs >= b) break;
                        if (fs > cur) segs.Add((cur, fs));
                        if (fe > cur) cur = fe;
                        if (cur >= b) break;
                    }
                    if (cur < b) segs.Add((cur, b));
                }
                if (segs.Count == 0) continue;

                // 4) Extension 延伸：向两端沿扫描线方向延长 (负值则可收缩)
                if (extension != 0f)
                {
                    for (int si = 0; si < segs.Count; si++)
                    {
                        var (a, b) = segs[si];
                        a -= extension;
                        b += extension;
                        if (b > a) segs[si] = (a, b);
                    }
                }

                // 5) 确定本行方向：S型双向时奇数行翻转，叠加全局 ReverseFillLine
                bool reverseLine = reverseAll;
                if (bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                // 6) 旋转回原坐标系并输出
                foreach (var (a, b) in segs)
                {
                    float sx = reverseLine ? b : a;
                    float ex = reverseLine ? a : b;
                    float bsx = (float)(sx * cosBack - y * sinBack);
                    float bsy = (float)(sx * sinBack + y * cosBack);
                    float bex = (float)(ex * cosBack - y * sinBack);
                    float bey = (float)(ex * sinBack + y * cosBack);
                    result.Add((new SKPoint(bsx, bsy), new SKPoint(bex, bey)));
                }
            }

            return result;
        }

        /// <summary>
        /// 生成文本扫描线填充（返回世界坐标系）
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GenerateTextScanlineFill2(HatchParamDto info)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (info == null || TextPath == null || TextPath.IsEmpty) return result;

            // 1. 展平文本路径（局部坐标系）
            List<SKPoint[]> contours = FlattenPathByArcLength(TextPath);
            if (contours == null || contours.Count == 0) return result;

            // 2. ✅ 获取变换矩阵，将轮廓转换到世界坐标系
            var matrix = GetTransformMatrix();
            var worldContours = new List<SKPoint[]>(contours.Count);
            foreach (var c in contours)
            {
                var transformed = new SKPoint[c.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    transformed[i] = matrix.MapPoint(c[i]);
                }
                worldContours.Add(transformed);
            }

            if (info.LineSpacing <= 0) return result;

            // 3. 在世界坐标系中计算边界
            float spacing = (float)info.LineSpacing;
            float margin = (float)info.Margin;
            float extension = (float)info.Extension;
            bool reverseAll = info.ReverseFillLine;
            // FillTypeIndex：0 = S型单向（所有扫描线同向），1 = S型双向（相邻行交替反向）。
            bool bidirectional = info.FillTypeIndex == 1;
            bool relativeToAngle = info.RelativeToAngle;

            // 4. 填充角度（世界坐标系）
            // 注意：Rotation 已经在矩阵中体现了，这里只需要使用 StartAngle
            double rad = -(relativeToAngle ? info.StartAngle + Rotation : info.StartAngle) * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            // 5. 旋转所有轮廓使填充方向水平
            var rotatedContours = new List<SKPoint[]>(worldContours.Count);
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in worldContours)
            {
                var rc = new SKPoint[c.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    rc[i] = new SKPoint(
                        (float)(c[i].X * cos - c[i].Y * sin),
                        (float)(c[i].X * sin + c[i].Y * cos)
                    );

                    float y = rc[i].Y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
                rotatedContours.Add(rc);
            }

            if (minY >= maxY) return result;

            // AverageDistribute
            float startOffset = spacing / 2f;
            float yLimit = maxY;
            if (info.AverageDistribute)
            {
                float span = maxY - minY;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startOffset = spacing;
                yLimit = maxY - spacing * 0.5f;
            }

            // 7. 旋转回去的矩阵
            double cosBack = Math.Cos(-rad), sinBack = Math.Sin(-rad);
            var xs = new List<float>(32);
            var forbidden = new List<(float Start, float End)>(64);
            var segs = new List<(float A, float B)>(16);

            int lineIndex = 0;
            for (float y = minY + startOffset; y < yLimit; y += spacing, lineIndex++)
            {
                // 1) 收集所有轮廓边与扫描线的交点
                xs.Clear();
                foreach (var rc in rotatedContours)
                {
                    int n = rc.Length;
                    for (int i = 0; i < n; i++)
                    {
                        var p1 = rc[i];
                        var p2 = rc[(i + 1) % n];
                        if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                        {
                            float t = (y - p1.Y) / (p2.Y - p1.Y);
                            xs.Add(p1.X + t * (p2.X - p1.X));
                        }
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                // 2) 计算禁区（margin 胶囊）
                forbidden.Clear();
                if (margin > 0)
                {
                    foreach (var rc in rotatedContours)
                    {
                        int n = rc.Length;
                        for (int i = 0; i < n; i++)
                        {
                            var p1 = rc[i];
                            var p2 = rc[(i + 1) % n];
                            if (TrySegmentCapsuleXRange(p1.X, p1.Y, p2.X, p2.Y, y, margin, out float fMin, out float fMax))
                                forbidden.Add((fMin, fMax));
                        }
                    }
                    if (forbidden.Count > 1)
                    {
                        forbidden.Sort((a, b) => a.Start.CompareTo(b.Start));
                        int w = 0;
                        for (int r = 1; r < forbidden.Count; r++)
                        {
                            if (forbidden[r].Start <= forbidden[w].End)
                            {
                                if (forbidden[r].End > forbidden[w].End)
                                    forbidden[w] = (forbidden[w].Start, forbidden[r].End);
                            }
                            else
                            {
                                w++;
                                forbidden[w] = forbidden[r];
                            }
                        }
                        forbidden.RemoveRange(w + 1, forbidden.Count - w - 1);
                    }
                }

                // 3) 减去禁区，得到有效段
                segs.Clear();
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float a = xs[i], b = xs[i + 1];
                    if (b <= a) continue;
                    float cur = a;
                    for (int k = 0; k < forbidden.Count; k++)
                    {
                        var (fs, fe) = forbidden[k];
                        if (fe <= cur) continue;
                        if (fs >= b) break;
                        if (fs > cur) segs.Add((cur, fs));
                        if (fe > cur) cur = fe;
                        if (cur >= b) break;
                    }
                    if (cur < b) segs.Add((cur, b));
                }
                if (segs.Count == 0) continue;

                // 4) Extension 延伸
                if (extension != 0f)
                {
                    for (int si = 0; si < segs.Count; si++)
                    {
                        var (a, b) = segs[si];
                        a -= extension;
                        b += extension;
                        if (b > a) segs[si] = (a, b);
                    }
                }

                // 5) 方向
                bool reverseLine = reverseAll;
                if (bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                // 6) 旋转回原坐标系（世界坐标系）并输出
                foreach (var (a, b) in segs)
                {
                    float sx = reverseLine ? b : a;
                    float ex = reverseLine ? a : b;
                    float bsx = (float)(sx * cosBack - y * sinBack);
                    float bsy = (float)(sx * sinBack + y * cosBack);
                    float bex = (float)(ex * cosBack - y * sinBack);
                    float bey = (float)(ex * sinBack + y * cosBack);
                    result.Add((new SKPoint(bsx, bsy), new SKPoint(bex, bey)));
                }
            }

            return result;
        }

        /// <summary>
        /// 水平扫描线 y = y 与线段 P1P2 的 margin-胶囊（线段与半径 margin 的圆盘的 Minkowski 和）交集 x 区间。
        /// 胶囊为凸集，与直线交集为单个区间。返回 true 则输出 [xMin, xMax]。
        /// </summary>
        private static bool TrySegmentCapsuleXRange(float p1x, float p1y, float p2x, float p2y,
                                                    float y, float margin,
                                                    out float xMin, out float xMax)
        {
            xMin = float.MaxValue;
            xMax = float.MinValue;
            bool any = false;

            float dx = p2x - p1x;
            float dy = p2y - p1y;
            float L2 = dx * dx + dy * dy;

            if (L2 < 1e-12f)
            {
                float ddy0 = y - p1y;
                if (Math.Abs(ddy0) > margin) return false;
                float dd0 = (float)Math.Sqrt(margin * margin - ddy0 * ddy0);
                xMin = p1x - dd0;
                xMax = p1x + dd0;
                return true;
            }

            float L = (float)Math.Sqrt(L2);

            // 端点 P1 处的半圆帽
            {
                float ddy = y - p1y;
                if (Math.Abs(ddy) <= margin)
                {
                    float dd = (float)Math.Sqrt(margin * margin - ddy * ddy);
                    if (p1x - dd < xMin) xMin = p1x - dd;
                    if (p1x + dd > xMax) xMax = p1x + dd;
                    any = true;
                }
            }
            // 端点 P2 处的半圆帽
            {
                float ddy = y - p2y;
                if (Math.Abs(ddy) <= margin)
                {
                    float dd = (float)Math.Sqrt(margin * margin - ddy * ddy);
                    if (p2x - dd < xMin) xMin = p2x - dd;
                    if (p2x + dd > xMax) xMax = p2x + dd;
                    any = true;
                }
            }
            // 线段中间的垂直条带：单位法线 (nx, ny) = (-dy/L, dx/L)，|(x-p1x)*nx + (y-p1y)*ny| ≤ margin，
            // 且垂足参数 t = ((x-p1x)*dx + (y-p1y)*dy)/L² ∈ [0,1]
            {
                float nx = -dy / L;
                float B = (y - p1y) * (dx / L);

                float stripMin, stripMax;
                bool stripActive = true;
                if (Math.Abs(nx) < 1e-9f)
                {
                    if (Math.Abs(y - p1y) > margin) stripActive = false;
                    stripMin = float.MinValue;
                    stripMax = float.MaxValue;
                }
                else
                {
                    float s1 = (-margin - B) / nx;
                    float s2 = (margin - B) / nx;
                    stripMin = Math.Min(s1, s2);
                    stripMax = Math.Max(s1, s2);
                }

                if (stripActive)
                {
                    float tMin, tMax;
                    if (Math.Abs(dx) < 1e-9f)
                    {
                        float yMn = Math.Min(p1y, p2y);
                        float yMx = Math.Max(p1y, p2y);
                        if (y < yMn || y > yMx) stripActive = false;
                        tMin = float.MinValue;
                        tMax = float.MaxValue;
                    }
                    else
                    {
                        float yDyTerm = (y - p1y) * dy;
                        float t1 = -yDyTerm / dx;
                        float t2 = (L2 - yDyTerm) / dx;
                        tMin = Math.Min(t1, t2);
                        tMax = Math.Max(t1, t2);
                    }

                    if (stripActive)
                    {
                        float fMinRel = Math.Max(stripMin, tMin);
                        float fMaxRel = Math.Min(stripMax, tMax);
                        if (fMinRel <= fMaxRel)
                        {
                            float absMin = fMinRel + p1x;
                            float absMax = fMaxRel + p1x;
                            if (absMin < xMin) xMin = absMin;
                            if (absMax > xMax) xMax = absMax;
                            any = true;
                        }
                    }
                }
            }

            return any;
        }
        /// <summary>
        /// 对单条填充线应用 Extension：正值从两端向外延长，负值向内收缩。
        /// 若收缩后长度≤ 0 则返回 false，表示该填充线不存在。
        /// </summary>
        private static bool TryApplyLineExtension(SKPoint s, SKPoint e, float extension,
            out SKPoint newStart, out SKPoint newEnd)
        {
            if (extension == 0f) { newStart = s; newEnd = e; return true; }
            float dx = e.X - s.X, dy = e.Y - s.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-6f) { newStart = s; newEnd = e; return extension > 0f; }
            if (len + 2f * extension <= 1e-6f) { newStart = default; newEnd = default; return false; }
            float ux = dx / len, uy = dy / len;
            newStart = new SKPoint(s.X - ux * extension, s.Y - uy * extension);
            newEnd = new SKPoint(e.X + ux * extension, e.Y + uy * extension);
            return true;
        }
        #endregion

    }
}
