using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Shapes;

namespace DrSoft.Drawing.Controls.Tools
{
    public class ToolText : ToolBase
    {
        public override ToolType ToolType => ToolType.Text;

        public override string Name => "输入文字";

        private readonly DocumentContext _context = DocumentContext.Instance;
        private SKPoint _anchorPoint;
        private string _textData = string.Empty;

        public override bool NeedRedrawOnMove => false;

        public override bool NeedRedrawOnDown => false;

        public override bool NeedRedrawOnUp => false;

        public bool ShouldShowCaretIndicator
        {
            get
            {
                var isActiveTextTool = _context.ActiveTool == this;
                var hasVisibleText = !string.IsNullOrWhiteSpace(_textData);
                var hasTrailingEmptyLine = HasTrailingEmptyLine(_textData);
                var shouldShow = _context.IsDrawing &&
                                 isActiveTextTool &&
                                 (!hasVisibleText || hasTrailingEmptyLine);
                return shouldShow;
            }
        }

        public SKPoint CaretAnchorPoint => _anchorPoint;

        public float CaretHeight
        {
            get
            {
                var fontSettings = _context.CurrentTextFontSettings;
                var fontSize = fontSettings?.FontSize ?? 10f;
                var caretHeight = MathF.Max(fontSize * 0.6f, 2f);
                return caretHeight;
            }
        }

        public bool TryGetCaretSegment(out SKPoint startPoint, out SKPoint endPoint)
        {
            startPoint = default;
            endPoint = default;

            var shouldShowCaret = ShouldShowCaretIndicator;
            if (!shouldShowCaret)
            {
                return false;
            }

            var hasTrailingEmptyLine = HasTrailingEmptyLine(_textData);
            var hasPreviewShape = _context.CurrentShape is DrawText;
            var canUsePreviewShape = hasTrailingEmptyLine && hasPreviewShape;
            if (!canUsePreviewShape)
            {
                return TryGetInitialCaretSegment(out startPoint, out endPoint);
            }

            var previewText = (DrawText)_context.CurrentShape;
            var result = TryGetTrailingEmptyLineCaretSegment(previewText, out startPoint, out endPoint);
            return result;
        }

        public override bool OnMouseDown(SKPoint point)
        {
            var result = BeginInlineEdit(point);
            return result;
        }

        public override void OnMouseMove(SKPoint point)
        {
        }

        public override bool OnMouseRightDown()
        {
            var hasText = !string.IsNullOrWhiteSpace(_textData);
            if (hasText)
            {
                var commitResult = CommitInlineEdit();
                return commitResult;
            }

            CancelInlineEdit();
            return true;
        }

        public bool BeginInlineEdit(SKPoint point)
        {
            if (_context.ActiveCanvas == null)
            {
                _context.ReportStatus("错误：没有激活的画布");
                return false;
            }

            _anchorPoint = point;
            _textData = string.Empty;
            _context.CurrentShape = null;
            // 每次新建文字都使用默认样式，避免沿用上一次文字的字体等格式
            _context.CurrentTextFontSettings = new FontSettings();
            _context.IsDrawing = true;
            _context.ReportStatus("输入文字，Ctrl+Enter 完成，Esc 取消");

            return true;
        }

        public bool UpdateInlineTextPreview(string textData)
        {
            if (!_context.IsDrawing)
            {
                return false;
            }

            var normalizedText = textData ?? string.Empty;
            _textData = normalizedText;

            var hasText = !string.IsNullOrWhiteSpace(normalizedText);
            if (!hasText)
            {
                _context.CurrentShape = null;
                return true;
            }

            var fontSettings = CreateInlineFontSettings(_context.CurrentTextFontSettings);
            var anchorPoints = new List<SKPoint> { _anchorPoint };

            if (_context.CurrentShape is DrawText previewText)
            {
                previewText.TextModel.Text = normalizedText;
                previewText.TextModel.FontSettings = fontSettings;
                previewText.UpdateSetProperty(anchorPoints);
                return true;
            }
            else
            {
                var drawText = new DrawText(normalizedText, _anchorPoint, fontSettings);
                _context.CurrentShape = drawText;

                drawText.Translate(_anchorPoint.X, _anchorPoint.Y, true);
                return true;
            }
        }

        public bool CommitInlineEdit()
        {
            if (!_context.IsDrawing)
            {
                return false;
            }

            var hasText = !string.IsNullOrWhiteSpace(_textData);
            var hasPreviewShape = _context.CurrentShape is DrawText;
            var canCommit = hasText && hasPreviewShape;
            if (!canCommit)
            {
                CancelInlineEdit();
                return false;
            }

            var previewText = (DrawText)_context.CurrentShape;
            // 输入预览允许使用临时顶对齐语义，但最终落图必须回到正式文字语义。
            var committedText = CreateCommittedTextFromPreview(previewText);
            _context.CurrentShape = committedText;

            FinishDrawing();
            _textData = string.Empty;
            _context.ReportStatus("文字输入完成");
            return true;
        }

        public void CancelInlineEdit()
        {
            _textData = string.Empty;
            OnCancel();
            _context.ReportStatus("已取消文字输入");
        }

        private static FontSettings CreateInlineFontSettings(FontSettings source)
        {
            var fontSettings = CloneFontSettings(source);
            fontSettings.VerticalAlign = 3;
            fontSettings.HorizontalAlign = SKTextAlign.Left;
            return fontSettings;
        }

        private DrawText CreateCommittedTextFromPreview(DrawText previewText)
        {
            var committedFontSettings = CloneFontSettings(_context.CurrentTextFontSettings);
            var committedText = new DrawText(_textData, _anchorPoint, committedFontSettings);

            var previewBounds = previewText.GetAABB();
            var committedBounds = committedText.GetAABB();

            var deltaX = previewBounds.MidX - committedBounds.MidX;
            var deltaY = previewBounds.MidY - committedBounds.MidY;

            committedText.Translate(deltaX, deltaY, true);
            return committedText;
        }

        private static FontSettings CloneFontSettings(FontSettings source)
        {
            if (source == null)
            {
                return new FontSettings();
            }

            var fontSettings = new FontSettings
            {
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                IsBold = source.IsBold,
                IsItalic = source.IsItalic,
                IsUnderline = source.IsUnderline,
                IsVerticalLayout = source.IsVerticalLayout,
                HorizontalAlign = source.HorizontalAlign,
                VerticalAlign = source.VerticalAlign,
                LineHeight = source.LineHeight,
                CharacterSpacing = source.CharacterSpacing,
                TextColor = source.TextColor
            };

            return fontSettings;
        }

        private bool TryGetInitialCaretSegment(out SKPoint startPoint, out SKPoint endPoint)
        {
            var hasInlineSegment = TryGetInlineInitialCaretSegment(out startPoint, out endPoint);
            if (hasInlineSegment)
            {
                return true;
            }

            var halfCaretHeight = CaretHeight * 0.5f;
            startPoint = new SKPoint(_anchorPoint.X, _anchorPoint.Y - halfCaretHeight);
            endPoint = new SKPoint(_anchorPoint.X, _anchorPoint.Y + halfCaretHeight);
            return true;
        }

        private bool TryGetInlineInitialCaretSegment(out SKPoint startPoint, out SKPoint endPoint)
        {
            startPoint = default;
            endPoint = default;

            var fontSettings = CreateInlineFontSettings(_context.CurrentTextFontSettings);
            var probeText = "Ag";
            var probeShape = new DrawText(probeText, _anchorPoint, fontSettings);
            probeShape.Translate(_anchorPoint.X, _anchorPoint.Y, true);
            var probeBounds = probeShape.GetAABB();
            var hasProbeBounds = !probeBounds.IsEmpty;
            if (!hasProbeBounds)
            {
                return false;
            }

            var availableHeight = Math.Abs(probeBounds.Top - probeBounds.Bottom);
            var caretHeight = MathF.Min(CaretHeight, MathF.Max(availableHeight, 2f));
            var halfCaretHeight = caretHeight * 0.5f;
            var caretCenterY = (probeBounds.Top + probeBounds.Bottom) * 0.5f;

            startPoint = new SKPoint(_anchorPoint.X, caretCenterY + halfCaretHeight);
            endPoint = new SKPoint(_anchorPoint.X, caretCenterY - halfCaretHeight);
            return true;
        }

        private bool TryGetTrailingEmptyLineCaretSegment(DrawText previewText, out SKPoint startPoint, out SKPoint endPoint)
        {
            startPoint = default;
            endPoint = default;

            var textModel = previewText.TextModel;
            var fontSettings = textModel?.FontSettings;
            if (textModel == null || fontSettings == null)
            {
                return false;
            }

            var fontStyle = new SKFontStyle(
                fontSettings.IsBold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                SKFontStyleWidth.Normal,
                fontSettings.IsItalic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);

            using var typeface = SKTypeface.FromFamilyName(fontSettings.FontFamily, fontStyle);
            using var font = new SKFont(typeface, fontSettings.FontSize);
            var fontMetrics = font.Metrics;

            var normalizedText = NormalizeLineBreaks(textModel.Text);
            var lines = SplitLines(normalizedText);
            var isVerticalLayout = fontSettings.IsVerticalLayout;

            if (isVerticalLayout)
            {
                var verticalResult = TryGetVerticalTrailingCaretSegment(
                    previewText,
                    font,
                    fontMetrics,
                    lines,
                    fontSettings,
                    out startPoint,
                    out endPoint);
                return verticalResult;
            }

            var horizontalResult = TryGetHorizontalTrailingCaretSegment(
                previewText,
                font,
                fontMetrics,
                lines,
                fontSettings,
                out startPoint,
                out endPoint);
            return horizontalResult;
        }

        private bool TryGetHorizontalTrailingCaretSegment(
            DrawText previewText,
            SKFont font,
            SKFontMetrics fontMetrics,
            string[] lines,
            FontSettings fontSettings,
            out SKPoint startPoint,
            out SKPoint endPoint)
        {
            startPoint = default;
            endPoint = default;

            if (lines.Length == 0)
            {
                return false;
            }

            float characterSpacing = fontSettings.CharacterSpacing;
            var lineWidths = new float[lines.Length];
            float maxLineWidth = 0f;

            for (int i = 0; i < lines.Length; i++)
            {
                var lineWidth = MeasureLineWidth(font, lines[i], characterSpacing);
                lineWidths[i] = lineWidth;
                if (lineWidth > maxLineWidth)
                {
                    maxLineWidth = lineWidth;
                }
            }

            var lineHeightMultiplier = ResolveLineHeightMultiplier(fontSettings);
            var lineStep = fontSettings.FontSize * lineHeightMultiplier;

            using var combinedPath = BuildHorizontalCombinedPath(
                font,
                lines,
                lineWidths,
                maxLineWidth,
                lineStep,
                characterSpacing,
                fontSettings.HorizontalAlign);
            if (combinedPath == null || combinedPath.IsEmpty)
            {
                return TryGetInitialCaretSegment(out startPoint, out endPoint);
            }

            var lastLineIndex = lines.Length - 1;
            var lastLineWidth = lineWidths[lastLineIndex];
            var caretX = GetLineXOffset(fontSettings.HorizontalAlign, maxLineWidth, lastLineWidth) + lastLineWidth;
            var caretBaselineY = lastLineIndex * lineStep;
            ResolveCaretVerticalRange(fontMetrics, caretBaselineY, out var caretTopY, out var caretBottomY);

            var localStart = MapToLocalTextPoint(
                previewText,
                combinedPath.Bounds,
                fontSettings.VerticalAlign,
                caretX,
                caretTopY);

            var localEnd = MapToLocalTextPoint(
                previewText,
                combinedPath.Bounds,
                fontSettings.VerticalAlign,
                caretX,
                caretBottomY);

            startPoint = localStart;
            endPoint = localEnd;
            return true;
        }

        private bool TryGetVerticalTrailingCaretSegment(
            DrawText previewText,
            SKFont font,
            SKFontMetrics fontMetrics,
            string[] columns,
            FontSettings fontSettings,
            out SKPoint startPoint,
            out SKPoint endPoint)
        {
            startPoint = default;
            endPoint = default;

            if (columns.Length == 0)
            {
                return false;
            }

            float characterSpacing = fontSettings.CharacterSpacing;
            float columnSpacingMultiplier = ResolveLineHeightMultiplier(fontSettings);
            var columnWidths = new float[columns.Length];
            float currentX = 0f;

            using var combinedPath = new SKPath();
            for (int i = 0; i < columns.Length; i++)
            {
                var columnText = columns[i];
                var columnWidth = MeasureVerticalColumnWidth(font, columnText, fontSettings.FontSize);
                columnWidths[i] = columnWidth;

                if (!string.IsNullOrEmpty(columnText))
                {
                    using var columnPath = BuildVerticalColumnPath(font, columnText, columnWidth, characterSpacing);
                    if (columnPath != null && !columnPath.IsEmpty)
                    {
                        using var translatedPath = new SKPath(columnPath);
                        translatedPath.Transform(SKMatrix.CreateTranslation(currentX, 0f));
                        combinedPath.AddPath(translatedPath);
                    }
                }

                if (i < columns.Length - 1)
                {
                    currentX += columnWidth * columnSpacingMultiplier;
                }
            }

            if (combinedPath.IsEmpty)
            {
                return TryGetInitialCaretSegment(out startPoint, out endPoint);
            }

            var lastColumnIndex = columns.Length - 1;
            float caretX = 0f;
            for (int i = 0; i < lastColumnIndex; i++)
            {
                caretX += columnWidths[i] * columnSpacingMultiplier;
            }

            var caretColumnWidth = columnWidths[lastColumnIndex];
            var localCenterX = caretX + caretColumnWidth * 0.5f;
            ResolveCaretVerticalRange(fontMetrics, 0f, out var localTopY, out var localBottomY);

            var localStart = MapToLocalTextPoint(
                previewText,
                combinedPath.Bounds,
                fontSettings.VerticalAlign,
                localCenterX,
                localTopY);

            var localEnd = MapToLocalTextPoint(
                previewText,
                combinedPath.Bounds,
                fontSettings.VerticalAlign,
                localCenterX,
                localBottomY);

            startPoint = localStart;
            endPoint = localEnd;
            return true;
        }

        private static float ResolveLineHeightMultiplier(FontSettings fontSettings)
        {
            var configuredLineHeight = fontSettings.LineHeight;
            var lineHeightMultiplier = configuredLineHeight > 0f
                ? configuredLineHeight
                : 1.2f;
            return lineHeightMultiplier;
        }

        private static SKPath BuildHorizontalCombinedPath(
            SKFont font,
            string[] lines,
            float[] lineWidths,
            float maxLineWidth,
            float lineStep,
            float characterSpacing,
            SKTextAlign align)
        {
            var combinedPath = new SKPath();

            for (int i = 0; i < lines.Length; i++)
            {
                var lineText = lines[i];
                if (string.IsNullOrEmpty(lineText))
                {
                    continue;
                }

                using var linePath = BuildLinePath(font, lineText, characterSpacing);
                if (linePath == null || linePath.IsEmpty)
                {
                    continue;
                }

                var xOffset = GetLineXOffset(align, maxLineWidth, lineWidths[i]);
                var yOffset = i * lineStep;
                using var translatedPath = new SKPath(linePath);
                translatedPath.Transform(SKMatrix.CreateTranslation(xOffset, yOffset));
                combinedPath.AddPath(translatedPath);
            }

            return combinedPath;
        }

        private static SKPath BuildVerticalColumnPath(SKFont font, string columnText, float columnWidth, float characterSpacing)
        {
            var columnPath = new SKPath();
            var currentY = 0f;

            for (int i = 0; i < columnText.Length; i++)
            {
                var characterText = columnText[i].ToString();
                using var glyphPath = font.GetTextPath(characterText);
                if (glyphPath != null && !glyphPath.IsEmpty)
                {
                    var glyphWidth = MathF.Max(glyphPath.Bounds.Width, 0f);
                    var glyphX = GetLineXOffset(SKTextAlign.Center, columnWidth, glyphWidth);
                    using var translatedGlyphPath = new SKPath(glyphPath);
                    translatedGlyphPath.Transform(SKMatrix.CreateTranslation(glyphX, currentY));
                    columnPath.AddPath(translatedGlyphPath);
                }

                var glyphHeight = MeasureGlyphHeight(font, characterText);
                currentY += glyphHeight + characterSpacing;
            }

            return columnPath;
        }

        private static SKPoint MapToLocalTextPoint(
            DrawText previewText,
            SKRect bounds,
            int verticalAlign,
            float x,
            float y)
        {
            var transX = -bounds.Left;
            float transY;
            if (verticalAlign == 0)
            {
                transY = 0f;
            }
            else if (verticalAlign == 1)
            {
                transY = -bounds.Bottom;
            }
            else if (verticalAlign == 3)
            {
                transY = -bounds.Top;
            }
            else
            {
                transY = -bounds.MidY;
            }

            // 局部路径为字体坐标系（Y 向下），这里只做与 GetTextPath 一致的布局平移归一化；
            // 字体系到画布系的 Y 翻转已烘焙在文本的 Matrix 中，由 GetTransformMatrix 统一完成。
            var localPoint = new SKPoint(x + transX, y + transY);
            var worldPoint = previewText.GetTransformMatrix().MapPoint(localPoint);
            return worldPoint;
        }

        private static float MeasureLineWidth(SKFont font, string line, float characterSpacing)
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
                var characterText = line[i].ToString();
                width += font.MeasureText(characterText);
                if (i < line.Length - 1)
                {
                    width += characterSpacing;
                }
            }

            return width;
        }

        private static float MeasureVerticalColumnWidth(SKFont font, string columnText, float fallbackWidth)
        {
            float width = MathF.Max(fallbackWidth, 1f);

            foreach (var character in columnText)
            {
                var characterText = character.ToString();
                var glyphWidth = MeasureGlyphWidth(font, characterText);
                if (glyphWidth > width)
                {
                    width = glyphWidth;
                }
            }

            return width;
        }

        private static float MeasureGlyphWidth(SKFont font, string text)
        {
            using var path = font.GetTextPath(text);
            if (path != null && !path.IsEmpty)
            {
                return MathF.Max(path.Bounds.Width, 0f);
            }

            return MathF.Max(font.MeasureText(text), 0f);
        }

        private static float MeasureGlyphHeight(SKFont font, string text)
        {
            using var path = font.GetTextPath(text);
            if (path != null && !path.IsEmpty)
            {
                return MathF.Max(path.Bounds.Height, 0f);
            }

            return MathF.Max(font.Size, 1f);
        }

        private static SKPath BuildLinePath(SKFont font, string line, float characterSpacing)
        {
            var linePath = new SKPath();
            float currentX = 0f;

            for (int i = 0; i < line.Length; i++)
            {
                var characterText = line[i].ToString();
                using var glyphPath = font.GetTextPath(characterText);
                if (glyphPath != null && !glyphPath.IsEmpty)
                {
                    using var translatedGlyphPath = new SKPath(glyphPath);
                    translatedGlyphPath.Transform(SKMatrix.CreateTranslation(currentX, 0f));
                    linePath.AddPath(translatedGlyphPath);
                }

                currentX += font.MeasureText(characterText);
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

        private static float GetLineXOffset(SKTextAlign align, float maxLineWidth, float lineWidth)
        {
            if (align == SKTextAlign.Center)
            {
                return (maxLineWidth - lineWidth) / 2f;
            }

            if (align == SKTextAlign.Right)
            {
                return maxLineWidth - lineWidth;
            }

            return 0f;
        }

        private void ResolveCaretVerticalRange(
            SKFontMetrics fontMetrics,
            float baselineY,
            out float topY,
            out float bottomY)
        {
            var fontTopY = baselineY + fontMetrics.Ascent;
            var fontBottomY = baselineY + fontMetrics.Descent;
            var fontHeight = fontBottomY - fontTopY;
            var caretHeight = MathF.Min(CaretHeight, MathF.Max(fontHeight, 2f));
            var halfCaretHeight = caretHeight * 0.5f;
            var fontMidY = (fontTopY + fontBottomY) * 0.5f;

            topY = fontMidY - halfCaretHeight;
            bottomY = fontMidY + halfCaretHeight;
        }

        private static bool HasTrailingEmptyLine(string text)
        {
            var normalizedText = NormalizeLineBreaks(text);
            var hasTrailingEmptyLine = normalizedText.EndsWith('\n');
            return hasTrailingEmptyLine;
        }

        private static string NormalizeLineBreaks(string text)
        {
            var normalizedText = (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            return normalizedText;
        }

        private static string[] SplitLines(string text)
        {
            var normalizedText = NormalizeLineBreaks(text);
            var lines = normalizedText.Split('\n');
            return lines;
        }

        public override bool OnMouseUp(SKPoint point)
        {
            return true;
        }
    }
}
