using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.DXFHelper.Parser;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Shapes;
using static DrSoft.Drawing.Controls.DrawShapes.DrawArc;

namespace DrSoft.Drawing.Controls.DXFHelper
{
    public static class DxfExporter
    {
        private const string UserLayer = "USER_DRAW";

        // ── 主入口：导出选中内容 ──────────────────────────────────────
        public static void Export(string path, CanvasSnapshotDto? store)
        {
            // 统一转成 DrawingLayerDto 列表（导出逻辑依赖 DTO 属性）
            List<DrawingLayer> layerDtos;
            if (store != null && store.Layers.Count > 0 && store.Layers[0] is DrawingLayer)
            {
                layerDtos = store.Layers.Cast<DrawingLayer>().ToList();
            }
            else if (store != null)
            {
                layerDtos = new List<DrawingLayer>();
                foreach (var layerData in store.Layers)
                {
                    if (layerData is DrawingLayer drawingLayer)
                        layerDtos.Add(drawingLayer);
                    //layerDtos.Add(CanvasSnapshotMapper.MapLayerToDto(drawingLayer));
                }
            }
            else
            {
                layerDtos = new List<DrawingLayer>();
            }

            using var sw = new StreamWriter(path, append: false, encoding: new UTF8Encoding(false));
            var w = new DxfWriter(sw);

            // HEADER
            w.Section("HEADER");
            w.Group(9, "$ACADVER");
            w.Group(1, "AC1015"); // AutoCAD 2013+ — UTF-8 编码
            w.Group(9, "$DWGCODEPAGE");
            w.Group(3, "ANSI_65001"); // 声明 UTF-8 编码
            w.EndSection();

            // TABLES（图层表 + 样式表）
            w.Section("TABLES");
            w.Group(0, "TABLE");
            w.Group(2, "VPORT");
            w.Group(5, "8");
            w.Group(70, "0");
            w.Group(0, "VPORT");
            w.Group(5, "30");
            w.Group(100, "AcDbSymbolTableRecord");
            w.Group(100, "AcDbViewportTableRecord");
            w.Group(2, "*Active");
            w.Group(70, "0");
            w.Group(0, "ENDTAB");

            // 收集需要的图层
            var layerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (store != null)
            {
                foreach (var layer in store.Layers)
                {
                    layerNames.Add(layer.Name);
                }
            }
            layerNames.Add(UserLayer);

            // LAYER 表
            w.Group(0, "TABLE");
            w.Group(2, "LAYER");
            w.Group(5, "2");
            w.Group(70, layerNames.Count.ToString());

            foreach (var name in layerNames)
            {
                w.Group(0, "LAYER");
                w.Group(5, layerNames.ToList().IndexOf(name).ToString());
                w.Group(100, "AcDbSymbolTableRecord");
                w.Group(100, "AcDbLayerTableRecord");
                w.Group(2, name);
                w.Group(70, "0");
                w.Group(62, "7");
                w.Group(6, "CONTINUOUS");
            }

            w.Group(0, "ENDTAB");

            // STYLE 表 — 定义 Standard 样式，确保 MTEXT 正确渲染
            w.Group(0, "TABLE");
            w.Group(2, "STYLE");
            w.Group(5, "3");
            w.Group(70, "1");
            w.Group(0, "STYLE");
            w.Group(5, "11");
            w.Group(100, "AcDbSymbolTableRecord");
            w.Group(100, "AcDbTextStyleTableRecord");
            w.Group(2, "Standard");
            w.Group(70, "0");
            w.Group(40, "0");
            w.Group(41, "1");
            w.Group(50, "0");
            w.Group(71, "0");
            w.Group(42, "1");
            w.Group(3, "Arial"); // 默认字体（不影响中文字体名在组码7中的传递）
            w.Group(4, "");
            w.Group(0, "ENDTAB");

            w.EndSection();

            // ENTITIES
            w.Section("ENTITIES");

            // CLASSES 段（AC1024+ 要求 CLASSES 在 ENTITIES 之前）
            // 留空即可，部分 CAD 需要此段存在

            // 添加所有图层和实体

            foreach (var layerDto in layerDtos)
            {
                List<IShape> AllShape = [];
                if (layerDto.Shapes == null || layerDto.Shapes.Count() == 0)
                    continue;

                // 创建 DXF 图层
                if (!layerDto.IsVisible) continue;

                // 添加实体
                foreach (var s in layerDto.Shapes)
                {
                    if (!s.IsVisible)
                        continue;
                    AllShape.Add(s);
                }

                foreach (var s in AllShape)
                {
                    DxfShape(s, layerDto.Name, w);
                    switch (s)
                    {
                        case DrawingHatch uh:
                            {
                                if (uh.Children == null || uh.Children.Count == 0) return;
                                foreach (var child in uh.Children)
                                {
                                    DxfShape(child, layerDto.Name, w);
                                }
                            }
                            break;
                        case DrawCombination uc:
                            {
                                if (uc.Children == null || uc.Children.Count == 0) return;
                                foreach (var child in uc.Children)
                                {
                                    DxfShape(child, layerDto.Name, w);
                                }
                            }
                            break;
                        case DrawingGroup ug:
                            {
                                if (ug.Children == null || ug.Children.Count == 0) return;
                                foreach (var child in ug.Children)
                                {
                                    DxfShape(child, layerDto.Name, w);
                                }
                            }
                            break;
                        default:
                            break;

                    }
                }
            }
            w.EndSection();
            w.Group(0, "EOF");
        }

        private static void DxfShape(IShape s, string layerName, DxfWriter w)
        {
            switch (s)
            {
                case DrawDot ul: w.Point(ul, layerName); break;
                case DrawPolyLines upl: w.PolyLine(upl, layerName); break;
                case DrawPolygon upl: w.Polygon(upl, layerName); break;
                case DrawRectangle ur:
                    if (ur.IsCornerRadiusRectangle())
                    {
                        w.RoundRectangle(ur, layerName);
                    }
                    else
                    {
                        w.Rectangle(ur, layerName);
                    }

                    break;
                case DrawCircle uc:
                    if (uc.IsEllipse)
                    {
                        //w.EllipseToLwPolyline(uc, layerName);
                        w.Ellipse(uc, layerName);
                    }
                    else w.Circle(uc, layerName);

                    break;
                case DrawText ut: w.TextLwpolyline(ut, layerName); break;
                case DrawArc ua: w.Arc(ua, layerName); break;
                case DrawBezier ub: w.Lwpolyline(ub, layerName); break;
                case DrawArbitraryCurve ua: w.Lwpolyline(ua, layerName); break;
                default:
                    break;

            }

        }
        // ── DXF 写入辅助 ─────────────────────────────────────────────
        private sealed class DxfWriter
        {
            private readonly StreamWriter _w;
            public DxfWriter(StreamWriter w) { _w = w; }

            public void Group(int code, string value)
            {
                _w.WriteLine(code.ToString().PadLeft(3));
                _w.WriteLine(value);
            }

            public void Section(string name)
            {
                Group(0, "SECTION");
                Group(2, name);
            }

            public void EndSection() => Group(0, "ENDSEC");

            // 线段（原逻辑基本正确，保留）
            public void Point(DrawDot dto, string layer)
            {
                Group(0, "POINT");
                Group(5, dto.UId.ToString());
                Group(8, layer);
                Coord(10, dto.SharpCenter.X);
                Coord(20, dto.SharpCenter.Y);
            }


            // 修复点：多段线 (Polyline) - 必须使用 LWPOLYLINE 并定义顶点
            public void PolyLine(DrawPolyLines dto, string layer)
            {
                var points = dto.OutlinePoints;
                if (points.Count < 2) return;

                Group(0, "LWPOLYLINE"); // 修正拼写和类型
                Group(5, dto.UId.ToString());
                Group(8, layer);
                Group(90, points.Count.ToString()); // 顶点数量
                Group(70, dto.IsClosed ? "1" : "0"); // 闭合状态 (0=不闭合, 1=闭合)，根据实际需求调整

                // 写入每个顶点坐标
                foreach (var pt in points)
                {
                    Coord(10, pt.X);
                    Coord(20, pt.Y);
                }
            }

            public void Polygon(DrawPolygon dto, string layer)
            {
                var points = dto.OutlinePoints;
                if (points.Count < 2) return;

                Group(0, "LWPOLYLINE"); // 修正拼写和类型
                Group(5, dto.UId.ToString());
                Group(8, layer);
                Group(90, points.Count.ToString()); // 顶点数量
                Group(70, "1"); // 闭合状态 (0=不闭合, 1=闭合)，根据实际需求调整

                // 写入每个顶点坐标
                foreach (var pt in points)
                {
                    Coord(10, pt.X);
                    Coord(20, pt.Y);
                }
            }

            public void Rectangle(DrawRectangle dto, string layer)
            {
                Group(0, "LWPOLYLINE");
                Group(8, layer);
                Group(90, dto.OutlinePoints.Count.ToString());
                Group(70, "1"); // 1 表示闭合 (Closed)

                // 按顺序写入计算出的 n 个顶点的坐标
                foreach (var pt in dto.OutlinePoints)
                {
                    Coord(10, pt.X); // X 坐标组码 10
                    Coord(20, pt.Y); // Y 坐标组码 20
                }
            }

            /// <summary>
            /// 绘制圆角矩形
            /// 逻辑：在局部坐标系按 clamp 后的半径计算各角圆弧的起点/弧中点/终点，
            /// 统一经变换矩阵映射到世界坐标后再反算 bulge。
            /// 旧实现用世界角点直接退/进局部半径 r，缩放后世界边长已变而 r 未折算，
            /// 且 bulge 固定 tan(22.5°) 无法处理镜像，导致往返后圆角失真。
            /// 末尾附加 DR_RECT XDATA（4 尖角世界点 + 4 圆角 + 4 倒角），供本软件导入时还原 DrawRectangle。
            /// </summary>
            public void RoundRectangle(DrawRectangle dto, string layer)
            {
                var localBounds = dto.GetLocalBounds();
                float left = localBounds.Left, right = localBounds.Right;
                float top = localBounds.Top, bottom = localBounds.Bottom;
                float maxRadius = Math.Min((right - left) / 2f, (top - bottom) / 2f);

                // 各角局部半径（clamp 到半宽/半高），顺序与尖角点一致：TL, TR, BR, BL
                var radii = new float[]
                {
                    Math.Clamp(dto.CornerRadiusTopLeft, 0f, maxRadius),
                    Math.Clamp(dto.CornerRadiusTopRight, 0f, maxRadius),
                    Math.Clamp(dto.CornerRadiusBottomRight, 0f, maxRadius),
                    Math.Clamp(dto.CornerRadiusBottomLeft, 0f, maxRadius),
                };

                // 局部系尖角点（Y 向上），沿轮廓 TL→TR→BR→BL 顺时针
                var corners = new SKPoint[]
                {
                    new SKPoint(left, top),
                    new SKPoint(right, top),
                    new SKPoint(right, bottom),
                    new SKPoint(left, bottom),
                };
                // 各条边的方向：edge[i] = corners[i] → corners[i+1]
                var edgeDirs = new SKPoint[]
                {
                    new SKPoint(1f, 0f),
                    new SKPoint(0f, -1f),
                    new SKPoint(-1f, 0f),
                    new SKPoint(0f, 1f),
                };

                var matrix = dto.GetTransformMatrix();
                var vertices = new List<SKPoint>(8);
                var bulges = new List<double>(8);
                const float radiusEps = 1e-6f;
                const float invSqrt2 = 0.70710678f;

                for (int i = 0; i < 4; i++)
                {
                    var c = corners[i];
                    float r = radii[i];
                    if (r <= radiusEps)
                    {
                        // 无圆角：只输出尖角点
                        vertices.Add(matrix.MapPoint(c));
                        bulges.Add(0.0);
                        continue;
                    }

                    var u = edgeDirs[(i + 3) % 4]; // 入边方向（指向尖角点）
                    var v = edgeDirs[i];           // 出边方向（离开尖角点）
                    var start = new SKPoint(c.X - u.X * r, c.Y - u.Y * r);
                    var end = new SKPoint(c.X + v.X * r, c.Y + v.Y * r);
                    // 圆心 = 尖角点向内偏移 (v - u) * r；弧中点在圆心指向尖角点方向距离 r 处
                    var arcCenter = new SKPoint(c.X + (v.X - u.X) * r, c.Y + (v.Y - u.Y) * r);
                    var mid = new SKPoint(
                        arcCenter.X + (c.X - arcCenter.X) * invSqrt2,
                        arcCenter.Y + (c.Y - arcCenter.Y) * invSqrt2);

                    var worldStart = matrix.MapPoint(start);
                    var worldMid = matrix.MapPoint(mid);
                    var worldEnd = matrix.MapPoint(end);
                    vertices.Add(worldStart);
                    bulges.Add(BulgeFromThreePoints(worldStart, worldMid, worldEnd)); // 圆弧段
                    vertices.Add(worldEnd);
                    bulges.Add(0.0); // 直线段（连接到下一个角）
                }

                Group(0, "LWPOLYLINE");
                Group(8, layer);
                Group(90, vertices.Count.ToString());
                Group(70, "1"); // 1 表示闭合 (Closed)

                for (int i = 0; i < vertices.Count; i++)
                {
                    Coord(10, vertices[i].X); // X 坐标组码 10
                    Coord(20, vertices[i].Y); // Y 坐标组码 20
                    Coord(42, bulges[i]);     // 凸度组码 42
                }

                // 附加 XDATA：4 尖角世界点(8) + 4 圆角半径(世界折算) + 4 倒角(预留写 0)，共 16 个值。
                // 导入侧 TryGetRectangleFromLwp 按 DR_RECT 标记识别并还原 DrawRectangle。
                float scaleW = matrix.MapVector(1f, 0f).Length;
                float scaleH = matrix.MapVector(0f, 1f).Length;
                float radiusScale = (scaleW + scaleH) / 2f; // 等比缩放时精确；非等比时取均值近似

                Group(-3, "");
                Group(1001, "DR_RECT");
                Group(1002, "{");
                for (int i = 0; i < 4; i++)
                {
                    var worldCorner = matrix.MapPoint(corners[i]);
                    Coord(1040, worldCorner.X);
                    Coord(1040, worldCorner.Y);
                }
                for (int i = 0; i < 4; i++)
                {
                    Coord(1040, radii[i] * radiusScale);
                }
                for (int i = 0; i < 4; i++)
                {
                    Coord(1040, 0.0);
                }
                Group(1002, "}");
            }

            /// <summary>
            /// 由弧的起点/弧中点/终点反算 DXF bulge（= tan(圆心角/4)，逆时针为正）。
            /// 有符号 sagitta = 弧中点到弦的垂距，bulge = 2·sagitta/弦长；
            /// 符号由弧中点在弦哪一侧决定，镜像变换下自动正确。
            /// </summary>
            private static double BulgeFromThreePoints(SKPoint s, SKPoint m, SKPoint e)
            {
                double cx = e.X - s.X, cy = e.Y - s.Y;
                double chord2 = cx * cx + cy * cy;
                if (chord2 < 1e-24) return 0.0;
                double cross = cx * (m.Y - s.Y) - cy * (m.X - s.X);
                return -2.0 * cross / chord2;
            }
            // 修复点：圆
            public void Circle(DrawCircle dto, string layer)
            {
                Group(0, "CIRCLE");
                Group(5, dto.UId.ToString());
                Group(8, layer);

                Coord(10, dto.SharpCenter.X);
                Coord(20, dto.SharpCenter.Y);
                // 修正点：半径组码改为 40 
                Coord(40, dto.RadiusX);
            }


            public void Ellipse(DrawCircle dto, string layer)
            {
                // 1. 实体名称
                Group(0, "ELLIPSE");
                Group(5, dto.UId.ToString());

                // 2. 图层
                Group(8, layer);

                // ★ 关键修复：AC1015(AutoCAD 2000+) 格式要求实体带子类标记(组码 100)，
                //    缺少 AcDbEllipse 子类标记会导致其他软件无法识别为椭圆。
                Group(100, "AcDbEntity");
                Group(100, "AcDbEllipse");

                // ★ 关键修复：直接从世界变换矩阵推导椭圆几何，避免旋转/倾斜丢失。
                // 公共属性 RadiusX/RadiusY 是“旋转后 AABB 包围盒”的半宽高，
                // 旋转已被折算进包围盒尺寸，据此导出会丢失方向。
                // 椭圆真实几何 = 以原点为中心、半轴为 DrawingRadiusX/DrawingRadiusY 的
                // 局部椭圆，经变换矩阵(含旋转/缩放/倾斜/平移)映射到世界坐标。
                var matrix = dto.GetTransformMatrix();
                var worldCenter = matrix.MapPoint(new SKPoint(0f, 0f));
                var axisXEnd = matrix.MapPoint(new SKPoint((float)dto.DrawingRadiusX, 0f));
                var axisYEnd = matrix.MapPoint(new SKPoint(0f, (float)dto.DrawingRadiusY));

                // 世界坐标下由局部两半轴映射得到的两条向量 u、v（相对中心）。
                // 注意：一般仿射变换（尤其倾斜，或倾斜叠加旋转/非均匀缩放）后，
                // u、v 不再互相垂直，它们是椭圆的“共轭半径”而非主/次轴。
                // 直接把 u 当主轴、|v|/|u| 当比率导出，其他软件会按
                // “主轴 + 垂直次轴”重建，得到错误的朝向与形状。
                double ux = axisXEnd.X - worldCenter.X;
                double uy = axisXEnd.Y - worldCenter.Y;
                double vx = axisYEnd.X - worldCenter.X;
                double vy = axisYEnd.Y - worldCenter.Y;

                // 由共轭半径解析求真实主/次轴（Rytz 构造）。
                // 椭圆参数方程 P(t) = u·cos t + v·sin t，|P(t)|² 的极值方向即主/次轴：
                //   tan(2·t0) = 2(u·v) / (|u|² - |v|²)。
                // 该式对纯旋转/缩放（u⊥v）会退化为原逻辑，对倾斜同样正确。
                double A = ux * ux + uy * uy;   // |u|²
                double C = vx * vx + vy * vy;   // |v|²
                double B = ux * vx + uy * vy;   // u·v
                double t0 = 0.5 * Math.Atan2(2.0 * B, A - C);

                double cos0 = Math.Cos(t0), sin0 = Math.Sin(t0);
                // 两条主方向候选：P(t0) 与 P(t0 + π/2)，二者必然互相垂直。
                double p1x = ux * cos0 + vx * sin0;
                double p1y = uy * cos0 + vy * sin0;
                double p2x = -ux * sin0 + vx * cos0;
                double p2y = -uy * sin0 + vy * cos0;

                double len1 = Math.Sqrt(p1x * p1x + p1y * p1y);
                double len2 = Math.Sqrt(p2x * p2x + p2y * p2y);

                // 主轴取较长的一条，保证 ratio ≤ 1（DXF ELLIPSE 规范要求）
                double majorX, majorY, majorLen, minorLen;
                if (len1 >= len2)
                {
                    majorX = p1x; majorY = p1y; majorLen = len1; minorLen = len2;
                }
                else
                {
                    majorX = p2x; majorY = p2y; majorLen = len2; minorLen = len1;
                }

                // 3. 中心点 (组码 10, 20, 30)
                Coord(10, worldCenter.X);
                Coord(20, worldCenter.Y);
                Coord(30, 0.0);

                // 4. 主轴端点向量 (组码 11, 21, 31)：方向即旋转，长度即长半轴
                Coord(11, majorX);
                Coord(21, majorY);
                Coord(31, 0.0);

                // 5. 长短轴比率 (组码 40) = 短半轴 / 长半轴，规范要求 ≤ 1
                double ratio = majorLen > 0 ? minorLen / majorLen : 1.0;
                Coord(40, ratio);

                // 6. 起始和结束参数 (组码 41, 42)：完整椭圆范围 0 ~ 2π
                Coord(41, 0.0);
                Coord(42, Math.PI * 2);
            }
            public void EllipseToLwPolyline(DrawCircle dto, string layer)
            {
                // 1. 多段线实体头
                Group(0, "LWPOLYLINE");
                Group(5, dto.UId.ToString());
                Group(8, layer);
                Group(100, "AcDbEntity");
                Group(100, "AcDbPolyline");

                // 2. 离散点数（可根据精度调整，如 64 或 128）
                int vertices = 128;
                Group(90, vertices.ToString());   // 顶点数
                Group(70, "1");                   // 闭合标志 (1 = 闭合)

                // 3. 椭圆参数
                double cx = dto.SharpCenter.X;
                double cy = dto.SharpCenter.Y;
                double rx = dto.RadiusX;
                double ry = dto.RadiusY;
                // DXF 角度：逆时针为正，与你的原始 Ellipse 逻辑保持一致（取负）
                double rotRad = -dto.Rotation * Math.PI / 180.0;
                double cosRot = Math.Cos(rotRad);
                double sinRot = Math.Sin(rotRad);

                // 4. 生成顶点
                for (int i = 0; i < vertices; i++)
                {
                    double t = i * 2.0 * Math.PI / vertices;   // 参数 t ∈ [0, 2π)
                    double cosT = Math.Cos(t);
                    double sinT = Math.Sin(t);

                    // 椭圆参数方程（带旋转）
                    double x = cx + rx * cosT * cosRot - ry * sinT * sinRot;
                    double y = cy + rx * cosT * sinRot + ry * sinT * cosRot;

                    Group(10, x.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Group(20, y.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Group(42, "0");   // 凸度 = 0 表示直线段
                }
            }

            /// <summary>
            /// 绘制圆弧
            /// 支持两种模式：
            /// 1. ThreePoint: 导出为多段线 (LWPOLYLINE)，保留 Start, Middle, End 三个点
            /// 2. CenterRadius: 导出为标准 ARC 实体
            /// </summary>
            public void Arc(DrawArc dto, string layer)
            {
                WriteStandardArc(dto, layer);
            }


            /// <summary>
            /// 写入标准圆心半径圆弧
            /// 使用圆弧的外接圆中心（曲率中心）而非 SharpCenter（包围盒中点），
            /// 坐标系与项目其他图形导出一致（Y-down），不做 Y 轴翻转。
            /// </summary>
            private void WriteStandardArc(DrawArc dto, string layer)
            {
                // 1. 获取世界坐标三点（包含旋转/缩放变换）
                var worldPoints = dto.GetWorldPoints();
                if (worldPoints.Count < 3) return;

                var wp1 = worldPoints[0];
                var wp2 = worldPoints[1];
                var wp3 = worldPoints[2];

                // 2. 从三点计算外接圆参数（几何圆心，而非包围盒中心）
                var circumResult = ArcMath.Circumcircle(wp1, wp2, wp3);
                if (!circumResult.HasValue) return; // 三点共线
                var (center, radius) = circumResult.Value;

                // 3. 计算起始角度和扫掠角度（基于外接圆中心，Y-down 屏幕坐标系）
                double startAngle = ArcMath.CalculateStartAngle(center, wp1);
                double sweepAngle = ArcMath.CalculateSweepAngle(center, wp1, wp2, wp3);

                // 4. 写入实体类型和图层
                Group(0, "ARC");
                Group(5, dto.UId.ToString());
                Group(8, layer);

                // 5. 写入圆心坐标（Y-down，与项目其他图形一致）
                Coord(10, center.X);
                Coord(20, center.Y);

                // 6. 写入半径
                Coord(40, radius);

                // 7. 处理角度（Y-down 屏幕坐标系，不翻转）
                double rawSweep = sweepAngle % 360;
                double rawEnd = startAngle + rawSweep;

                // 标准化到 [0, 360)
                double dxfStart = (startAngle % 360 + 360) % 360;
                double dxfEnd = (rawEnd % 360 + 360) % 360;

                // DXF 铁律：ARC 实体始终逆时针（正扫角），如果负扫角则翻转起止点
                if (rawSweep < 0)
                {
                    double temp = dxfStart;
                    dxfStart = dxfEnd;
                    dxfEnd = temp;
                }

                // 8. 写入角度组码
                Coord(50, dxfStart); // 组码 50: 起始角
                Coord(51, dxfEnd);   // 组码 51: 终止角

                // 9. 附加 XDATA（存储世界坐标三点，导入时优先使用，保留原始起点/终点顺序）
                Group(-3, "");
                Group(1001, "THREE_POINT_ARC");
                Group(1002, "{");
                Group(1040, wp1.X.ToString()); Group(1040, wp1.Y.ToString());
                Group(1040, wp2.X.ToString()); Group(1040, wp2.Y.ToString());
                Group(1040, wp3.X.ToString()); Group(1040, wp3.Y.ToString());
                Group(1002, "}");
            }
            public void TextLwpolyline(DrawText dto, string layer)
            {
                var outlinePoints = dto.OutlinePoints;
                if (outlinePoints == null || outlinePoints.Count < 2) return;

                // OutlinePoints 用 NaN 分隔不同轮廓
                WriteOutlinePointsAsPolylines(outlinePoints, dto.UId, layer);
            }
            /// <summary>
            /// 将文本导出为线段（LWPOLYLINE），每个字符轮廓作为独立的闭合多段线
            /// </summary>
            public void TextLwpolyline1(DrawText dto, string layer)
            {

                // 获取文本轮廓（每个轮廓是一个闭合的字符笔画路径）
                var contours = dto.Contours;
                if (contours == null || contours.Count == 0)
                {
                    // 如果轮廓未计算，尝试使用 OutlinePoints（降级处理）
                    var outlinePoints = dto.OutlinePoints;
                    if (outlinePoints == null || outlinePoints.Count < 2) return;

                    // OutlinePoints 用 NaN 分隔不同轮廓
                    WriteOutlinePointsAsPolylines(outlinePoints, dto.UId, layer);
                    return;
                }

                // 每个轮廓导出为一条 LWPOLYLINE
                int contourIndex = 0;
                foreach (var contour in contours)
                {
                    if (contour == null || contour.Length < 2) continue;

                    Group(0, "LWPOLYLINE");
                    Group(5, (dto.UId * 1000 + contourIndex).ToString()); // 每个轮廓用不同句柄
                    Group(8, layer);
                    Group(100, "AcDbEntity");
                    Group(100, "AcDbPolyline");

                    Group(90, contour.Length.ToString()); // 顶点数量

                    // 判断是否闭合（首尾点相同）
                    bool isClosed = contour.Length >= 2
                        && Math.Abs(contour[0].X - contour[contour.Length - 1].X) < 0.001f
                        && Math.Abs(contour[0].Y - contour[contour.Length - 1].Y) < 0.001f;
                    Group(70, isClosed ? "1" : "0");

                    // 写入每个顶点坐标
                    foreach (var pt in contour)
                    {
                        Coord(10, pt.X);
                        Coord(20, pt.Y);
                    }

                    contourIndex++;
                }
            }

            /// <summary>
            /// 将 OutlinePoints（用 NaN 分隔的多轮廓）导出为多条 LWPOLYLINE
            /// </summary>
            private void WriteOutlinePointsAsPolylines(List<Point2D> outlinePoints, long baseUId, string layer)
            {
                var currentContour = new List<Point2D>();
                int contourIndex = 0;

                foreach (var pt in outlinePoints)
                {
                    // NaN 表示轮廓分隔符
                    if (float.IsNaN(pt.X) || float.IsNaN(pt.Y))
                    {
                        // 写入当前轮廓
                        if (currentContour.Count >= 2)
                        {
                            WriteSingleContour(currentContour, baseUId * 1000 + contourIndex, layer);
                            contourIndex++;
                        }
                        currentContour.Clear();
                    }
                    else
                    {
                        currentContour.Add(pt);
                    }
                }

                // 写入最后一个轮廓
                if (currentContour.Count >= 2)
                {
                    WriteSingleContour(currentContour, baseUId * 1000 + contourIndex, layer);
                }
            }

            /// <summary>
            /// 写入单个轮廓为 LWPOLYLINE
            /// </summary>
            private void WriteSingleContour(List<Point2D> contour, long handle, string layer)
            {
                Group(0, "LWPOLYLINE");
                Group(5, handle.ToString());
                Group(8, layer);
                Group(100, "AcDbEntity");
                Group(100, "AcDbPolyline");

                Group(90, contour.Count.ToString());

                // 判断是否闭合
                bool isClosed = contour.Count >= 2
                    && Math.Abs(contour[0].X - contour[contour.Count - 1].X) < 0.001f
                    && Math.Abs(contour[0].Y - contour[contour.Count - 1].Y) < 0.001f;
                Group(70, isClosed ? "1" : "0");

                foreach (var pt in contour)
                {
                    Coord(10, pt.X);
                    Coord(20, pt.Y);
                }
            }
            // 文本（导出为 MTEXT 格式）
            public void Text(DrawText dto, string layer)
            {
                Group(0, "MTEXT");
                Group(5, dto.UId.ToString()); // 实体句柄
                Group(100, "AcDbEntity");
                Group(8, layer);
                Group(100, "AcDbMText");

                // 插入点 (组码 10, 20)
                Coord(10, dto.SharpCenter.X);
                Coord(20, dto.SharpCenter.Y);

                // 文字高度 (组码 40)
                Coord(40, dto.TextModel.FontSettings.FontSize);

                // 文字内容 (组码 1)
                // MTEXT 中换行用 \P 表示，需要将实际换行符转换
                string mtextContent = (dto.TextModel.Text ?? "")
                    .Replace("\r\n", "\\P")
                    .Replace("\n", "\\P");

                // 倾斜（斜体）：MTEXT 通过 {\Qangle;} 格式化代码控制倾斜角度
                if (dto.TextModel.FontSettings.IsItalic)
                    mtextContent = "{\\Q15;" + mtextContent + "}";

                // 下划线：MTEXT 通过 {\L...\l} 格式化代码控制下划线
                if (dto.TextModel.FontSettings.IsUnderline)
                    mtextContent = "{\\L" + mtextContent + "\\l}";

                // MTEXT 超过 250 字符时需要拆分：组码 3 存续行，组码 1 存末行
                // 每行最多 250 字符（MTEXT 限制）
                const int MaxLineLen = 250;
                if (mtextContent.Length > MaxLineLen)
                {
                    int pos = 0;
                    while (pos + MaxLineLen < mtextContent.Length)
                    {
                        Group(3, mtextContent.Substring(pos, MaxLineLen));
                        pos += MaxLineLen;
                    }
                    Group(1, mtextContent.Substring(pos)); // 末行
                }
                else
                {
                    Group(1, mtextContent);
                }

                // 旋转角度 (组码 50)
                if (Math.Abs(dto.Rotation) > 1e-6)
                    Coord(50, -dto.Rotation);

                // 字体名称 (组码 7) - 存储为 DxfTextFontName 枚举 int 值
                Group(7, ((int)DxfTextFontNameExtensions.ParseFontName(dto.TextModel.FontSettings.FontFamily)).ToString());

                // 附件点/对齐方式 (组码 71)
                // 1=TL,2=TC,3=TR, 4=ML,5=MC,6=MR, 7=BL,8=BC,9=BR
                int hAlign = dto.TextModel.FontSettings.HorizontalAlign switch
                {
                    SKTextAlign.Center => 2, // Center → TC/ML/MR depending on VAlign
                    SKTextAlign.Right => 3,  // Right → TR/MR/BR depending on VAlign
                    _ => 1                   // Left → TL/ML/BL depending on VAlign
                };
                int vAlign = dto.TextModel.FontSettings.VerticalAlign switch
                {
                    3 => 0, // Top → row 1 (TL/TC/TR)
                    2 => 3, // Middle → row 2 (ML/MC/MR), offset by 3 from hAlign base
                    1 => 6, // Bottom → row 3 (BL/BC/BR), offset by 6 from hAlign base
                    _ => 0  // Baseline → treat as Top
                };
                int attachmentPoint = hAlign + vAlign;
                // 确保在有效范围 1-9 内
                if (attachmentPoint < 1 || attachmentPoint > 9) attachmentPoint = 7; // 默认 BL
                Group(71, attachmentPoint.ToString());
            }
            public void TextSingle(DrawText dto, string layer)
            {
                Group(0, "TEXT");
                Group(5, dto.UId.ToString());
                Group(100, "AcDbEntity");
                Group(8, layer);
                Group(100, "AcDbText");

                // 插入点（组码 10,20）
                Coord(10, dto.SharpCenter.X);
                Coord(20, dto.SharpCenter.Y);

                // 文字高度
                Coord(40, dto.TextModel.FontSettings.FontSize);

                // 文字内容：单行文本不要换行符（直接替换或抛出异常）
                string content = (dto.TextModel.Text ?? "").Replace("\r\n", " ").Replace("\n", " ");
                Group(1, content);

                // 旋转角度（弧度）
                if (Math.Abs(dto.Rotation) > 1e-6)
                    Coord(50, -dto.Rotation);

                // 倾斜角度（弧度）：例如 15° = 15 * Math.PI / 180
                double obliqueAngleRad = 0;
                if (dto.TextModel.FontSettings.IsItalic)
                {
                    obliqueAngleRad = 15 * Math.PI / 180;   // 常用 15°，也可让用户配置
                                                            // 注意：DXF 中正值表示文字向右倾斜（顶部向右）
                                                            // 如果你的坐标系 Y 向上，可能需要取反
                }
                if (Math.Abs(obliqueAngleRad) > 1e-6)
                    Coord(51, obliqueAngleRad);

                // 字体 (组码 7)
                Group(7, ((int)DxfTextFontNameExtensions.ParseFontName(dto.TextModel.FontSettings.FontFamily)).ToString());

                // ---- 对齐方式处理 ----
                // 水平对齐 (72)
                int horizCode = dto.TextModel.FontSettings.HorizontalAlign switch
                {
                    SKTextAlign.Center => 1,
                    SKTextAlign.Right => 2,
                    _ => 0   // Left
                };
                // 垂直对齐 (73)
                int vertCode = dto.TextModel.FontSettings.VerticalAlign switch
                {
                    3 => 3,   // Top
                    2 => 2,   // Middle
                    1 => 1,   // Bottom
                    _ => 0    // Baseline
                };

                Group(72, horizCode.ToString());
                Group(73, vertCode.ToString());

                // 如果 72 或 73 不为 0，则需要指定对齐点 (11,21)
                if (horizCode != 0 || vertCode != 0)
                {
                    // 简单处理：对齐点使用与插入点相同的坐标（实际应根据文本边界计算）
                    Coord(11, dto.SharpCenter.X);
                    Coord(21, dto.SharpCenter.Y);
                }

                // 结束 AcDbText 的扩展（如果有 AcDbText 后续子类如 AcDbAttributeDefinition 可以继续）
                // 这里不需要额外的 Group(100, "AcDbText") 结束，因为组码 0 的 TEXT 已经闭合。
            }
            public void Lwpolyline(DrawBezier dto, string layer)
            {
                var points = dto.OutlinePoints;
                if (points.Count < 2) return;

                // 开始 LWPOLYLINE 实体
                Group(0, "LWPOLYLINE");
                Group(5, dto.UId.ToString());          // 句柄
                Group(8, layer);                       // 图层
                Group(100, "AcDbEntity");
                Group(100, "AcDbPolyline");

                // 顶点个数
                Group(90, points.Count.ToString());

                // 标志位：1=闭合，0=不闭合（也可按位组合其他标志，如128=plinegen，一般用0或1即可）
                int flags = dto.IsClosed ? 1 : 0;
                Group(70, flags.ToString());

                // 可选：常量宽度（设为0表示无宽度）
                Group(43, "0.0");

                // 可选：默认起始宽度和结束宽度（若需要可变宽度则使用 40/41，这里统一注释）
                // Group(40, "0.0");
                // Group(41, "0.0");

                // 输出所有顶点
                foreach (var pt in points)
                {
                    // 多段线顶点坐标 (X, Y)
                    Coord(10, pt.X);
                    Coord(20, pt.Y);
                    // 注意：LWPOLYLINE 没有 30 组码，Z 坐标由图元的标高（38组码）或扩展数据提供
                    // 如果需要 Z 值，可以单独设置 Group(38, zValue);

                    // 凸度：0 表示直线段（也是最通用的值）
                    Group(42, "0.0");
                }

                // 如果闭合且首尾点重复，通常 LWPOLYLINE 闭合时不需要重复首点，
                // 但如果您的 points 中已经包含了重复的终点，建议去重处理，或者继续保留（某些软件会警告）。
                // 这里不做额外处理，依靠 flags=1 实现闭合。
            }
            // 贝塞尔曲线
            public void Spline(DrawBezier dto, string layer)
            {
                var points = dto.OutlinePoints;
                if (points.Count < 3) return; // 样条线至少需要3个点

                Group(0, "SPLINE");
                Group(5, dto.UId.ToString());
                Group(8, layer);
                Group(100, "AcDbEntity");
                Group(100, "AcDbSpline");

                // 1. 设置标志位: 平面(4) + 闭合(1) = 5
                Group(70, dto.IsClosed ? "5" : "4");
                Group(71, "3"); // 阶数 (立方)

                int fitPointCount = points.Count;
                // 2. 估算控制点数量 (用于创建基础曲线)
                int controlPointCount = fitPointCount;
                // 3. 估算节点数量 (规则: knots = controlPoints + degree + 1)
                int knotCount = controlPointCount + 3 + 1;

                // 4. 写入这些关键参数
                Group(72, knotCount.ToString());      // 节点数
                Group(73, controlPointCount.ToString()); // 控制点数
                Group(74, fitPointCount.ToString());   // 拟合点数

                // 5. 写入拟合点坐标
                foreach (var pt in points)
                {
                    Coord(11, pt.X);
                    Coord(21, pt.Y);
                    Coord(31, 0.0);
                }

                // 6. 可选但强烈推荐: 写入公差组码
                Group(42, "0.0000001"); // 节点公差
                Group(43, "0.0000001"); // 控制点公差
                Group(44, "0.0000000001"); // 拟合公差
            }
            public void Lwpolyline(DrawArbitraryCurve dto, string layer)
            {
                var points = dto.OutlinePoints;
                if (points.Count < 2) return;

                // 开始 LWPOLYLINE 实体
                Group(0, "LWPOLYLINE");
                Group(5, dto.UId.ToString());          // 句柄
                Group(8, layer);                       // 图层
                Group(100, "AcDbEntity");
                Group(100, "AcDbPolyline");

                // 顶点个数
                Group(90, points.Count.ToString());

                // 标志位：1=闭合，0=不闭合（也可按位组合其他标志，如128=plinegen，一般用0或1即可）
                int flags = dto.IsClosed ? 1 : 0;
                Group(70, flags.ToString());

                // 可选：常量宽度（设为0表示无宽度）
                Group(43, "0.0");

                // 可选：默认起始宽度和结束宽度（若需要可变宽度则使用 40/41，这里统一注释）
                // Group(40, "0.0");
                // Group(41, "0.0");

                // 输出所有顶点
                foreach (var pt in points)
                {
                    // 多段线顶点坐标 (X, Y)
                    Coord(10, pt.X);
                    Coord(20, pt.Y);
                    // 注意：LWPOLYLINE 没有 30 组码，Z 坐标由图元的标高（38组码）或扩展数据提供
                    // 如果需要 Z 值，可以单独设置 Group(38, zValue);

                    // 凸度：0 表示直线段（也是最通用的值）
                    Group(42, "0.0");
                }

                // 如果闭合且首尾点重复，通常 LWPOLYLINE 闭合时不需要重复首点，
                // 但如果您的 points 中已经包含了重复的终点，建议去重处理，或者继续保留（某些软件会警告）。
                // 这里不做额外处理，依靠 flags=1 实现闭合。
            }
            // 任意曲线
            public void Spline(DrawArbitraryCurve dto, string layer)
            {
                var points = dto.OutlinePoints;
                if (points.Count < 2) return;

                Group(0, "SPLINE");
                Group(5, dto.UId.ToString());
                Group(8, layer);
                Group(100, "AcDbEntity");
                Group(100, "AcDbSpline");
                Group(70, dto.IsClosed ? "5" : "4");
                Group(71, "3");
                Group(74, points.Count.ToString());

                foreach (var pt in points)
                {
                    Coord(11, pt.X);
                    Coord(21, pt.Y);
                    Coord(31, 0.0);
                }
            }


            // 辅助方法
            private void Coord(int code, double v)
            {
                _w.WriteLine(code.ToString().PadLeft(3));
                _w.WriteLine(v.ToString("G8", CultureInfo.InvariantCulture));
            }
        }
    }
}
