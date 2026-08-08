// ============================================================
// SceneStore.cs
// DXF 几何数据容器：紧凑平坦数组 + 图层索引
//
// 性能关键设计：
//   · 所有图元存为 struct 数组（缓存友好，无多态开销）
//   · Build() 后冻结，渲染线程只读无锁
//   · 按图层预分组索引（渲染时按层批量绘制，最少 SKPaint 切换）
//   · float 精度（DXF 用 double，渲染不需要亚像素精度）
// ============================================================

using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.DXFHelper.Parser;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DrSoft.Drawing.Controls.DXFHelper
{
    // ── 图元类型 ────────────────────────────────────────────────────
    public enum GeoKind : byte { Point,Line, Arc, Circle , LWPOLYLINE }

    // ── 统一图元结构体（28 字节，4 字节对齐）───────────────────────
    // Line:   X1,Y1 → X2,Y2
    // Arc:    X1,Y1=圆心  X2=半径  A1=起始角度(度)  A2=终止角度(度)
    // Circle: X1,Y1=圆心  X2=半径
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct GeoItem
    {
        public GeoKind Kind;
        public int LayerIdx;
        public float X1, Y1, X2, Y2;
        public float A1, A2;           // Arc 专用
        public bool Closed;
        public double Width;            // constant width (code 43)
        public List<LwVertex> Verts;
    }

    // ── 图层状态 ─────────────────────────────────────────────────────
    public sealed class LayerInfo
    {
        public int Idx;
        public string Name;
        public SKColor Color;
        public bool Visible = true;
        public LayerInfo(int id,string n, SKColor c) { Idx = id; Name = n; Color = c; }
    }

    // ── 场景容器 ─────────────────────────────────────────────────────
    public sealed class SceneStore
    {
        // ── 构建期（仅在导入线程访问）──────────────────────────────
        //private readonly List<GeoItem> _items = new(128_000);
        private readonly List<DrawObject> _itemsDto = new(128_000);
        private readonly List<LayerInfo> _layers = new(64);
        private readonly Dictionary<string, int> _layerMap =
            new(64, StringComparer.OrdinalIgnoreCase);

        private float _minX = float.MaxValue, _minY = float.MaxValue;
        private float _maxX = float.MinValue, _maxY = float.MinValue;

        // ── 冻结后只读（渲染线程访问）──────────────────────────────
        public GeoItem[] Items { get; private set; } = Array.Empty<GeoItem>();

        public List<DrawObject>? ItemsDto { get; private set; } = new();
        public LayerInfo[] Layers { get; private set; } = Array.Empty<LayerInfo>();
        public int[][] LayerIndex { get; private set; } = Array.Empty<int[]>();
        public SKRect Bounds { get; private set; }
        public bool IsBuilt { get; private set; }
        public int TotalCount => Items.Length;
        public int DtoCount => _itemsDto.Count;

        public void EnsureDtoCapacity(int capacity)
        {
            if (capacity > _itemsDto.Capacity)
            {
                _itemsDto.EnsureCapacity(capacity);
            }
        }

        // ── 图层颜色调色板 ──────────────────────────────────────────
        private static readonly SKColor[] Palette =
        {
            new(0x00, 0x00, 0x00),   // 黑
            new(0xF8, 0x51, 0x49),   // 红
            new(0x3F, 0xB9, 0x50),   // 绿
            new(0xBC, 0x8C, 0xFF),   // 紫
            new(0xFF, 0xD3, 0x3D),   // 黄
            new(0x58, 0xA6, 0xFF),   // 蓝
            new(0xF7, 0x8A, 0x00),   // 橙
            new(0x39, 0xD3, 0xD5),   // 青
            new(0xFF, 0x8C, 0xC0),   // 粉
            new(0x96, 0xCB, 0xFF),   // 浅蓝
            new(0x7D, 0xE8, 0x7D),   // 浅绿
        };

        // ── 构建期只读访问（供 DrfSerializer 覆盖颜色/可见性）────────
        // 仅在 Build() 调用前使用
        public List<LayerInfo> LayersForInit => _layers;

        // ── 添加图层（若不存在则创建）──────────────────────────────
        public int GetOrAddLayer(string name)
        {
            if (_layerMap.TryGetValue(name, out int idx)) return idx;
            idx = _layers.Count;
            _layers.Add(new LayerInfo(idx,name, Palette[idx % Palette.Length]));
            _layerMap[name] = idx;
            return idx;
        }
        // ── 添加图元（直接创建 DrawObject，零拷贝，无需二次映射）────
        public void AddLine(float x1, float y1, float x2, float y2, int layerIdx, bool showJumpLine)
        {
            int n = _itemsDto.Count;
            _itemsDto.Add(new DrawPolyLines(new List<SKPoint>(2) { new(x1, y1), new(x2, y2) },true)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}"
            });
            Expand(x1, y1); Expand(x2, y2);
        }

        public void AddArcThreePoints((float X, float Y) p1, (float X, float Y) p2, (float X, float Y) p3, int layerIdx, bool showJumpLine)
        {
            // 三点圆弧 → 计算圆心和半径（用于 SharpCenter 与边界扩展）
            double ax = p1.X, ay = p1.Y, bx = p2.X, by = p2.Y, cx = p3.X, cy = p3.Y;
            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            float centerX = 0, centerY = 0;
            if (Math.Abs(d) > 1e-10)
            {
                double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
                centerX = (float)((a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d);
                centerY = (float)((a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d);
            }

            int n = _itemsDto.Count;
            var dest = new DrawArc(
                new Point2D((float)ax, (float)ay),
                new Point2D((float)bx, (float)by),
                new Point2D((float)cx, (float)cy))
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}"
            };
            _itemsDto.Add(dest);

            double minX = Math.Min(Math.Min(p1.X, p2.X), p3.X);
            double minY = Math.Min(Math.Min(p1.Y, p2.Y), p3.Y);
            double maxX = Math.Max(Math.Max(p1.X, p2.X), p3.X);
            double maxY = Math.Max(Math.Max(p1.Y, p2.Y), p3.Y);
            Expand((float)minX, (float)minY);
            Expand((float)maxX, (float)maxY);
        }

        /// <summary>
        /// 添加多段线（LWPOLYLINE），支持 Bulge 弧线段细分
        /// Bulge ≈ 0 → 直线段；Bulge ≠ 0 → 将弧线细分为多个直线点近似
        /// </summary>
        public void AddPolyLinePoints(bool closed, double width, List<LwVertex> verts, int layerIdx, bool showJumpLine)
        {
            int n = verts.Count;
            if (n < 2) return;
            int segCount = closed ? n : n - 1;

            var skPoints = new List<SKPoint>(n * 2);

            for (int i = 0; i < segCount; i++)
            {
                var p1 = verts[i];
                var p2 = verts[(i + 1) % n];

                // 每段只添加起点（首次）和弧线中间点；末点由下一段的起点或 IsClosed 补齐
                if (i == 0)
                    skPoints.Add(new SKPoint((float)p1.X, (float)p1.Y));

                if (Math.Abs(p1.Bulge) < 1e-12)
                {
                    // 直线段：只添加终点
                    skPoints.Add(new SKPoint((float)p2.X, (float)p2.Y));
                }
                else
                {
                    // 弧线段：计算弧参数并细分为直线点
                    double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
                    double chord2 = dx * dx + dy * dy;
                    if (chord2 < 1e-24)
                    {
                        skPoints.Add(new SKPoint((float)p2.X, (float)p2.Y));
                        continue;
                    }
                    double chord = Math.Sqrt(chord2);
                    double ab = Math.Abs(p1.Bulge), b2 = p1.Bulge * p1.Bulge;
                    double r = chord * (1 + b2) / (4 * ab);
                    double dtc = chord * (1 - b2) / (4 * ab);
                    double px = -dy / chord, py = dx / chord;
                    double sg = p1.Bulge > 0 ? 1.0 : -1.0;
                    double cx = (p1.X + p2.X) * 0.5 + sg * dtc * px;
                    double cy = (p1.Y + p2.Y) * 0.5 + sg * dtc * py;
                    double sa = Math.Atan2(p1.Y - cy, p1.X - cx);
                    double ea = Math.Atan2(p2.Y - cy, p2.X - cx);

                    // 计算扫掠角
                    double sweep = ea - sa;
                    if (p1.Bulge > 0) { if (sweep <= 0) sweep += 2 * Math.PI; }
                    else { if (sweep >= 0) sweep -= 2 * Math.PI; }

                    // 细分弧线为直线点（跳过起点，包含终点）
                    const int arcSteps = 8;
                    for (int j = 1; j <= arcSteps; j++)
                    {
                        double t = j / (double)arcSteps;
                        double angle = sa + sweep * t;
                        skPoints.Add(new SKPoint((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle))));
                    }
                }
            }

            // 闭合多段线：最后一个点与第一个点重合，移除它让 IsClosed 处理闭合
            if (closed && skPoints.Count > 1)
                skPoints.RemoveAt(skPoints.Count - 1);

            foreach (var pt in skPoints) Expand(pt.X, pt.Y);

            int idx = _itemsDto.Count;
            _itemsDto.Add(new DrawPolyLines(skPoints, isDxf: true)
            {
                LayerId = layerIdx,
                IsVisible = true,
                IsClosed = closed,
                Name = closed ? $"{idx}" : $"{idx}",
               // Name = closed ? $"Polygon{idx}" : $"PolyLine{idx}",
            });
        }

        public void AddArc(double cx, double cy, double r,
                           double startDeg, double endDeg, int layerIdx, bool showJumpLine,
                           double? exactStartX = null, double? exactStartY = null,
                           double? exactEndX = null, double? exactEndY = null)
        {
            Expand(cx - r, cy - r); Expand(cx + r, cy + r);
            int n = _itemsDto.Count;

            // DXF arcs are always CCW from StartAngle to EndAngle.
            // When endDeg < startDeg, the sweep crosses 0 degrees, so add 360 degrees;
            // When endDeg == startDeg, it's a full circle (sweep = 360 degrees).
            // Use double precision throughout to avoid endpoint mismatch with connected lines.
            double startRad = startDeg * (Math.PI / 180.0);
            double sweepDeg = endDeg - startDeg;
            if (sweepDeg <= 0) sweepDeg += 360.0;
            double sweepRad = sweepDeg * (Math.PI / 180.0);

            // 如果提供了来自 LWPOLYLINE 的精确端点，直接使用，避免从中心/半径/角度
            // 重新计算带来的浮点误差，确保与相邻线段严格对齐。
            Point2D start, end;
            if (exactStartX.HasValue && exactStartY.HasValue &&
                exactEndX.HasValue && exactEndY.HasValue)
            {
                start = new Point2D((float)exactStartX.Value, (float)exactStartY.Value);
                end   = new Point2D((float)exactEndX.Value, (float)exactEndY.Value);
            }
            else
            {
                start = new Point2D((float)(cx + r * Math.Cos(startRad)), (float)(cy + r * Math.Sin(startRad)));
                end   = new Point2D((float)(cx + r * Math.Cos(startRad + sweepRad)), (float)(cy + r * Math.Sin(startRad + sweepRad)));
            }
            var mid = new Point2D((float)(cx + r * Math.Cos(startRad + sweepRad / 2)), (float)(cy + r * Math.Sin(startRad + sweepRad / 2)));

            _itemsDto.Add(new DrawArc(start, mid, end,true)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}",
            });
        }

        public void AddCircle(float cx, float cy, float r, int layerIdx, bool showJumpLine)
        {
            Expand(cx - r, cy - r); Expand(cx + r, cy + r);
            int n = _itemsDto.Count;
            _itemsDto.Add(new DrawCircle(new List<SKPoint>(2) { new(cx, cy), new(cx + r, cy) },true)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}"
            });
        }

        /// <summary>
        /// 添加椭圆（或旋转椭圆），使用 DXF 主轴向量方式导入。
        /// 主轴端点 = 圆心 + 主轴方向向量（长度 = radiusX，方向 = rotationDeg）。
        /// </summary>
        /// <param name="cx">中心 X</param>
        /// <param name="cy">中心 Y</param>
        /// <param name="radiusX">长半径（实际长度）</param>
        /// <param name="radiusY">短半径（实际长度）</param>
        /// <param name="rotationDeg">旋转角度（DXF 约定：逆时针为正，度数）</param>
        /// <param name="layerIdx">图层索引</param>
        /// <param name="showJumpLine">是否显示跳转路径</param>
        public void AddEllipse(float cx, float cy, float radiusX, float radiusY, float rotationDeg, int layerIdx, bool showJumpLine)
        {
            int n = _itemsDto.Count;
            // 计算主轴端点（世界坐标）：圆心 + 主轴方向向量
            float rotRad = rotationDeg * MathF.PI / 180f;
            float majorEndX = cx + radiusX * MathF.Cos(rotRad);
            float majorEndY = cy + radiusX * MathF.Sin(rotRad);
            float ratio = radiusX > 0 ? radiusY / radiusX : 1f;

            _itemsDto.Add(new DrawCircle(new List<SKPoint>(2) { new(cx, cy), new(majorEndX, majorEndY) }, isDxf: true, dxfRatio: ratio)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}",
            });
            float maxRadius = Math.Max(radiusX, radiusY);
            Expand(cx - maxRadius, cy - maxRadius);
            Expand(cx + maxRadius, cy + maxRadius);
        }

        public void AddPoint(float x, float y, int layerIdx, bool showJumpLine)
        {
            Expand(x, y);
            int n = _itemsDto.Count;
            _itemsDto.Add(new DrawDot(new SKPoint(x, y),true)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}"
            });
        }

        public void AddBezier(List<(double X, double Y)> points, int layerIdx, bool showJumpLine)
        {
            var skPoints = new List<SKPoint>(points.Count);
            foreach (var p in points)
            {
                float px = (float)p.X, py = (float)p.Y;
                skPoints.Add(new SKPoint(px, py));
                Expand(px, py);
            }
            int n = _itemsDto.Count;
            _itemsDto.Add(new DrawBezier(skPoints)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}",
            });
        }

        public void AddArbitraryCurve(List<(double X, double Y)> points, bool closed, int layerIdx, bool showJumpLine)
        {
            var skPoints = new List<SKPoint>(points.Count);
            foreach (var p in points)
            {
                float px = (float)p.X, py = (float)p.Y;
                skPoints.Add(new SKPoint(px, py));
                Expand(px, py);
            }
            int n = _itemsDto.Count;
            _itemsDto.Add(new DrawArbitraryCurve(skPoints)
            {
                LayerId = layerIdx,
                IsVisible = true,
                IsClosed = closed,
                Name = $"{n}",
            });
        }

    

        public void AddRectangle(List<(double X, double Y)> points , List<double>? FRs, List<double>? CRs, int layerIdx, bool showJumpLine, double rotationDeg = 0)
        {
            int n = _itemsDto.Count;
            List<SKPoint> skPoints = new List<SKPoint>(points.Count);
            foreach (var p in points)
            {
                skPoints.Add(new SKPoint((float)p.X, (float)p.Y));
            }
            foreach (var pt in skPoints) Expand(pt.X, pt.Y);

            // 圆角参数：FRs 可能为 null（仅倒角矩形），单独判断避免空引用
            List<float>? CornerPara = (FRs != null && FRs.Count >= 4)
                ? new List<float>() { (float)FRs[0], (float)FRs[1], (float)FRs[2], (float)FRs[3] }
                : null;

            _itemsDto.Add(new DrawRectangle(skPoints, CornerPara, true)
            {
                LayerId = layerIdx,
                IsVisible = true,
                Name = $"{n}"
            });
        }



   
        private void Expand(float x, float y)
        {
            if (x < _minX) _minX = x; if (x > _maxX) _maxX = x;
            if (y < _minY) _minY = y; if (y > _maxY) _maxY = y;
        }

        private void Expand(double x, double y) => Expand((float)x, (float)y);

        // ── Build：冻结场景，构建图层索引 ───────────────────────────
        public void Build()
        {
            //Items = _items.ToArray();
            Layers = _layers.ToArray();
            // 直接移交引用，避免 O(N) 拷贝（大数据量时显著）
            ItemsDto = _itemsDto;

            if (Items.Length > 0)
            {
                float w = Math.Max(_maxX - _minX, 1e-6f);
                float h = Math.Max(_maxY - _minY, 1e-6f);
                Bounds = new SKRect(_minX, _minY, _minX + w, _minY + h);
            }

            // 按图层分组索引（渲染时按图层批量提交，最小化 Paint 切换）
            int lc = _layers.Count;
            var cnt = new int[lc];
            for (int i = 0; i < Items.Length; i++) cnt[Items[i].LayerIdx]++;
            var idx = new int[lc][];
            for (int li = 0; li < lc; li++) idx[li] = new int[cnt[li]];
            var pos = new int[lc];
            for (int i = 0; i < Items.Length; i++)
            {
                int li = Items[i].LayerIdx;
                idx[li][pos[li]++] = i;
            }
            LayerIndex = idx;
            IsBuilt = true;
        }
        public void AddText(string text, float x, float y, float height, float rotationDeg, string fontName, int layerIdx, bool isVisibleProcessPath, int hAlign = 0, int vAlign = 0, double obliquingDeg = 0, bool isUnderline = false)
        {
            int n = _itemsDto.Count;

            // 将 DXF 水平对齐 (72) 映射到 SKTextAlign
            var skHAlign = hAlign switch
            {
                1 => SKTextAlign.Center,   // Center
                2 => SKTextAlign.Right,    // Right
                3 => SKTextAlign.Center,   // Aligned → 近似 Center
                4 => SKTextAlign.Center,   // Middle → Center
                5 => SKTextAlign.Center,   // Fit → 近似 Center
                _ => SKTextAlign.Left,     // Left / default
            };

            _itemsDto.Add(new DrawText(
                                text ?? string.Empty, 
                                new SKPoint(x, y),  
                                new TextModel
                                {
                                    Text = text ?? string.Empty,
                                    FontSettings = new FontSettings
                                    {
                                        FontFamily = fontName ?? string.Empty,
                                        FontSize = height,
                                        IsVerticalLayout = false,
                                        HorizontalAlign = skHAlign,
                                        VerticalAlign = vAlign,
                                        // DXF obliquing > 0 表示倾斜（类似斜体），映射到 IsItalic
                                        IsItalic = Math.Abs(obliquingDeg) > 0.01,
                                        // DXF 下划线（从 MTEXT {\L...} 或 STYLE 表检测）
                                        IsUnderline = isUnderline,
                                    }
                                })
                          {
                              LayerId = layerIdx,
                              IsVisible = true,
                              Name = $"{n}",
                              Rotation = -rotationDeg,
                          });
            Expand(x, y);
        }
    }
}
