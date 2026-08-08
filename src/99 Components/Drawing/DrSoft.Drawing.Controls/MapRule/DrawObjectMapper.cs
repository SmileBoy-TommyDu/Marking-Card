using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Windows.Documents;
using System.Windows.Media.Media3D;
using static System.Net.Mime.MediaTypeNames;

namespace DrSoft.Drawing.Controls.Mapping
{
    public static class DrawObjectMapper
    {
        /// <summary>
        /// DrawObject → DrawObjectDto。
        /// 根据运行时图元类型分发到对应的具体映射方法。
        /// </summary>
        public static DrawObjectDto? Map(DrawObject? source)
        {
            if (source == null)
            {
                return null;
            }

            return source switch
            {
                DrawText text => MapText(text),
                DrawArc arc => MapArc(arc),
                DrawCircle circle => MapCircle(circle),
                DrawRectangle rectangle => MapRectangle(rectangle),
                DrawPolyLines polyLines => MapPolyLines(polyLines),
                DrawPolygon polygon => MapPolygon(polygon),
                DrawDot dot => MapDot(dot),
                DrawBezier bezier => MapBezier(bezier),
                DrawArbitraryCurve arbitraryCurve => MapArbitraryCurve(arbitraryCurve),
                DrawingGroup group => MapGroup(group),
                DrawCombination combination => MapCombination(combination),
                DrawingHatch hatch => MapHatch(hatch),
                _ => MapBase(source, new DrawObjectDto())
            };
        }

        /// <summary>
        /// DrawObjectDto → DrawObject。
        /// 根据 DTO 的具体类型分发到对应的业务对象构造逻辑。
        /// </summary>
        public static DrawObject? Map(DrawObjectDto? source)
        {
            if (source == null)
            {
                return null;
            }

            return source switch
            {
                DrawTextDto text => MapText(text),
                DrawArcDto arc => MapArc(arc),
                DrawCircleDto circle => MapCircle(circle),
                DrawRectangleDto rectangle => MapRectangle(rectangle),
                DrawPolyLinesDto polyLines => MapPolyLines(polyLines),
                DrawPolygonDto polygon => MapPolygon(polygon),
                DrawDotDto dot => MapDot(dot),
                DrawBezierDto bezier => MapBezier(bezier),
                DrawArbitraryCurveDto arbitraryCurve => MapArbitraryCurve(arbitraryCurve),
                GroupDto group => MapGroup(group),
                CombinationDto combination => MapCombination(combination),
                HatchDto hatch => MapHatch(hatch),
                _ => null
            };
        }

        public static List<DrawObjectDto> MapShapes(IEnumerable<IShape>? shapes)
        {
            if (shapes == null)
            {
                return new List<DrawObjectDto>();
            }

            if (shapes is ICollection<IShape> collection)
            {
                var result = new List<DrawObjectDto>(collection.Count);
                foreach (var shape in collection)
                {
                    if (shape is DrawObject drawObject)
                    {
                        var dto = Map(drawObject);
                        if (dto != null)
                        {
                            result.Add(dto);
                        }
                    }
                }

                return result;
            }

            return MapShapesCore(shapes);
        }
        /// <summary>
        /// 但当源对象实现 IContainer 时不拷贝其子级（Children 保持为空）。
        /// 用于只需要浅层映射，不需要递归子节点的场景。
        /// </summary>
        public static List<DrawObjectDto> MapWithoutChildren(IEnumerable<IShape>? shapes)
        {
            if (shapes == null)
            {
                return new List<DrawObjectDto>();
            }

            if (shapes is ICollection<IShape> collection)
            {
                var result = new List<DrawObjectDto>(collection.Count);
                foreach (var shape in collection)
                {
                    if (shape is DrawObject drawObject)
                    {
                        var dto = MapShallow(drawObject);
                        if (dto != null)
                        {
                            result.Add(dto);
                        }
                    }
                }

                return result;
            }

            return MapShapesCore(shapes, shallow: true);
        }

        public static DrawObjectDto? MapWithoutChildren(DrawObject? source)
        {
            // 单对象浅映射入口：用于只关心选区/变换元数据的 UI 事件负载，
            // 避免递归 Children 和昂贵的 OutlinePoints 计算。
            return MapShallow(source);
        }

        private static List<DrawObjectDto> MapShapesCore(IEnumerable<IShape> shapes, bool shallow = false)
        {
            var result = new List<DrawObjectDto>();
            foreach (var shape in shapes)
            {
                if (shape is not DrawObject drawObject)
                {
                    continue;
                }

                var dto = shallow ? MapShallow(drawObject) : Map(drawObject);
                if (dto != null)
                {
                    result.Add(dto);
                }
            }

            return result;
        }

        /// <summary>
        /// 将 DrawObject 映射为 DTO，但不递归映射任何子级（Children 不会被填充）。
        /// 用于浅拷贝场景，避免递归导致的子对象复制。
        /// </summary>
        private static DrawObjectDto? MapShallow(DrawObject? source)
        {
            if (source == null) return null;

            return source switch
            {
                DrawText text => MapTextShallow(text),
                DrawArc arc => MapArcShallow(arc),
                DrawCircle circle => MapCircleShallow(circle),
                DrawRectangle rectangle => MapRectangleShallow(rectangle),
                DrawPolyLines polyLines => MapBaseShallow(polyLines, new DrawPolyLinesDto()),
                DrawPolygon polygon => MapPolygonShallow(polygon),
                DrawDot dot => MapBaseShallow(dot, new DrawDotDto()),
                DrawBezier bezier => MapBaseShallow(bezier, new DrawBezierDto()),
                DrawArbitraryCurve arbitraryCurve => MapBaseShallow(arbitraryCurve, new DrawArbitraryCurveDto()),
                // 对于容器类型，只复制容器自身的公共属性，不填充 Children
                DrawingGroup group => MapBaseShallow(group, new GroupDto()),
                DrawCombination combination => MapBaseShallow(combination, new CombinationDto()),
                DrawingHatch hatch => MapHatchShallow(hatch),
                _ => MapBaseShallow(source, new DrawObjectDto())
            };
        }



        /// <summary>
        /// 业务对象公共属性 → DTO 公共属性。
        /// 保持与旧映射规则一致：
        /// 1. Rotation 取反（DTO 存储方向与业务对象相反）
        /// 2. 基础几何属性与状态属性统一在这里集中复制
        /// 3. Points / OutlinePoints / IntersectionSkipPoints 在这里统一转换
        /// </summary>
        private static TDto MapBase<TDto>(DrawObject source, TDto destination)
            where TDto : DrawObjectDto
        {
            MapShared(source, destination);

            var outlinePoints = source.OutlinePoints;
            if (outlinePoints != null && outlinePoints.Count > 0)
            {
                destination.OutlinePoints = MapPoints(outlinePoints);
            }

            return destination;
        }

        private static TDto MapBaseShallow<TDto>(DrawObject source, TDto destination)
            where TDto : DrawObjectDto
        {
            // 浅映射只保留公共几何/状态字段，不计算 OutlinePoints。
            MapShared(source, destination);
            if (source is IContainer container)
            {
                destination.ChildrenCount = container.Children.Count;
            }
            return destination;
        }

        private static void MapShared<TDto>(DrawObject source, TDto destination)
            where TDto : DrawObjectDto
        {
            // 公共字段统一在这里复制，避免完整映射和浅映射各自维护一套逻辑。
            destination.UId = source.UId;
            destination.Name = source.Name;
            destination.Direction = source.Direction;
            destination.ShowJumpLine = source.ShowJumpLine;
            destination.IsClockwise = source.IsClockwise;
            destination.IsVisible = source.IsVisible;
            destination.IsSelected = source.IsSelected;
            destination.IsLocked = source.IsLocked;
            destination.LayerId = source.LayerId;
            destination.Type = (ShapeType)source.Type;
            destination.SharpCenter = new Point2D(source.SharpCenter.X, source.SharpCenter.Y);
            destination.RotationCenter = new Point2D(source.RotationCenter.X, source.RotationCenter.Y);
            destination.X = source.X;
            destination.Y = source.Y;
            destination.Width = source.Width;
            destination.Height = source.Height;
            destination.Rotation = source.Rotation;
            destination.ScaleX = source.ScaleX;
            destination.ScaleY = source.ScaleY;
            destination.SkewX = source.SkewX;
            destination.SkewY = source.SkewY;
            destination.IntersectionSkipRadius = source.IntersectionSkipRadius;

            var obb = source.GetOBB();
            destination.OBBInfo = (obb!.Corners.Select(p => new Point2D(p.X, p.Y)).ToArray(), new Point2D(obb.Center.X, obb.Center.Y));

            if (source.Points.Count > 0)
            {
                destination.Points = MapPoints(source.Points);
            }

            if (source.IntersectionSkipPoints.Count > 0)
            {
                // 使用 WorldIntersectionSkipPoints（本地→世界变换后），
                // 供打标指令生成器 ApplyIntersectionSkip 使用
                var worldSkipPoints = source.WorldIntersectionSkipPoints;
                destination.IntersectionSkipPoints = worldSkipPoints
                    .Select(wp => new Point2D(wp.X, wp.Y)).ToList();
            }
        }

        /// <summary>
        /// DTO 公共属性 → 业务对象公共属性。
        /// </summary>
        private static void MapBase(DrawObjectDto source, DrawObject destination)
        {
            destination.UId = source.UId;
            destination.Name = source.Name ?? string.Empty;
            destination.Direction = source.Direction;
            destination.ShowJumpLine = source.ShowJumpLine;
            destination.IsClockwise = source.IsClockwise;
            destination.IsVisible = source.IsVisible;
            destination.IsSelected = source.IsSelected;
            destination.IsLocked = source.IsLocked;
            destination.LayerId = source.LayerId;
            destination.Type = (ShapeType)(int)source.Type;
            destination.Rotation = -source.Rotation;
            destination.ScaleX = source.ScaleX;
            destination.ScaleY = source.ScaleY;
            destination.SkewX = source.SkewX;
            destination.SkewY = source.SkewY;

            if (source.PointsOrNull is { Count: > 0 } sourcePoints)
            {
                destination.Points = MapSKPoints(sourcePoints);
            }

            destination.SetRotationCenter(new SKPoint((float)source.RotationCenter.X, (float)source.RotationCenter.Y));

            if (source.IntersectionSkipPointsOrNull is { Count: > 0 } sourceSkipPoints)
            {
                destination.IntersectionSkipPoints = MapSKPoints(sourceSkipPoints);
            }

            destination.IntersectionSkipRadius = source.IntersectionSkipRadius;
        }

        /// <summary>
        /// DrawLine ↔ DrawLineDto 只需要处理公共基类属性，
        /// 具体转换复用 MapBase 即可。
        /// </summary>

        private static DrawPolyLinesDto MapPolyLines(DrawPolyLines source) => MapBase(source, new DrawPolyLinesDto());

        private static DrawPolygonDto MapPolygon(DrawPolygon source)
        {
            var dto = MapBase(source, new DrawPolygonDto());
            dto.SideCount = source.SideCount;
            dto.IsStar = source.IsStar;
            return dto;
        }

        private static DrawDotDto MapDot(DrawDot source) => MapBase(source, new DrawDotDto());

        private static DrawBezierDto MapBezier(DrawBezier source) => MapBase(source, new DrawBezierDto());

        private static DrawArbitraryCurveDto MapArbitraryCurve(DrawArbitraryCurve source) => MapBase(source, new DrawArbitraryCurveDto());

        private static DrawCircleDto MapCircle(DrawCircle source)
        {
            var dto = MapBase(source, new DrawCircleDto());
            dto.RadiusX = source.RadiusX;
            dto.RadiusY = source.RadiusY;
            dto.IsEllipse = source.IsEllipse;
            // 圆的 Points（圆心 + 边缘点，共 2 个）可由 RadiusX + SharpCenter 完全重建，
            // 导入侧 MapCircle 已实现重建逻辑，此处跳过存储，避免 10M 圆产生 ~880 MB GC 压力
            dto.Points = null!;
            return dto;
        }

        private static DrawRectangleDto MapRectangle(DrawRectangle source)
        {
            var dto = MapBase(source, new DrawRectangleDto());
            dto.HasRoundedCorners = source.hasRoundedCorners;
            dto.HasChamfer = source.hasChamfer;
            dto.CornerRadiusTopLeft = source.CornerRadiusTopLeft;
            dto.CornerRadiusTopRight = source.CornerRadiusTopRight;
            dto.CornerRadiusBottomRight = source.CornerRadiusBottomRight;
            dto.CornerRadiusBottomLeft = source.CornerRadiusBottomLeft;
            dto.ChamferTopLeft = source.ChamferTopLeft;
            dto.ChamferTopRight = source.ChamferTopRight;
            dto.ChamferBottomLeft = source.ChamferBottomLeft;
            dto.ChamferBottomRight = source.ChamferBottomRight;
            if (source.Vertices.Count > 0)
            {
                dto.Vertices = MapPoints(source.Vertices);
            }
            return dto;
        }

        private static DrawArcDto MapArc(DrawArc source)
        {
            var dto = MapBase(source, new DrawArcDto());
            dto.Radius = source.Radius;
            dto.StartAngle = source.StartAngle;
            dto.SweepAngle = source.SweepAngle;
            dto.TypeOfArc = source.TypeOfArc == DrawArc.ArcType.CenterRadius
                ? ArcTypeDto.CenterRadius
                : ArcTypeDto.ThreePoint;
            return dto;
        }

        private static DrawArcDto MapArcShallow(DrawArc source)
        {
            var dto = MapBaseShallow(source, new DrawArcDto());
            dto.Radius = source.Radius;
            dto.StartAngle = source.StartAngle;
            dto.SweepAngle = source.SweepAngle;
            dto.TypeOfArc = source.TypeOfArc == DrawArc.ArcType.CenterRadius
                ? ArcTypeDto.CenterRadius
                : ArcTypeDto.ThreePoint;
            return dto;
        }

        private static DrawTextDto MapText(DrawText source)
        {
            var dto = MapBase(source, new DrawTextDto());
            dto.Text = source.TextModel?.Text;
            dto.FontFamily = source.TextModel?.FontSettings?.FontFamily ?? string.Empty;
            dto.FontSize = source.TextModel?.FontSettings?.FontSize ?? 0;
            dto.IsBold = source.TextModel?.FontSettings?.IsBold ?? false;
            dto.IsItalic = source.TextModel?.FontSettings?.IsItalic ?? false;
            dto.IsUnderline = source.TextModel?.FontSettings?.IsUnderline ?? false;
            dto.IsVerticalLayout = source.TextModel?.FontSettings?.IsVerticalLayout ?? false;
            dto.HorizontalAlign = (int)(source.TextModel?.FontSettings?.HorizontalAlign ?? SKTextAlign.Left);
            dto.LineHeight = source.TextModel?.FontSettings?.LineHeight ?? 0;
            dto.CharacterSpacing = source.TextModel?.FontSettings?.CharacterSpacing ?? 0;
            dto.CurrentCenterPoint = new Point2D(source.CurrentCenterPoint.X, source.CurrentCenterPoint.Y);
            return dto;
        }

        private static DrawTextDto MapTextShallow(DrawText source)
        {
            var dto = MapBaseShallow(source, new DrawTextDto());
            dto.Text = source.TextModel?.Text;
            dto.FontFamily = source.TextModel?.FontSettings?.FontFamily ?? string.Empty;
            dto.FontSize = source.TextModel?.FontSettings?.FontSize ?? 0;
            dto.IsBold = source.TextModel?.FontSettings?.IsBold ?? false;
            dto.IsItalic = source.TextModel?.FontSettings?.IsItalic ?? false;
            dto.IsUnderline = source.TextModel?.FontSettings?.IsUnderline ?? false;
            dto.IsVerticalLayout = source.TextModel?.FontSettings?.IsVerticalLayout ?? false;
            dto.HorizontalAlign = (int)(source.TextModel?.FontSettings?.HorizontalAlign ?? SKTextAlign.Left);
            dto.LineHeight = source.TextModel?.FontSettings?.LineHeight ?? 0;
            dto.CharacterSpacing = source.TextModel?.FontSettings?.CharacterSpacing ?? 0;
            dto.CurrentCenterPoint = new Point2D(source.CurrentCenterPoint.X, source.CurrentCenterPoint.Y);
            return dto;
        }

        private static DrawCircleDto MapCircleShallow(DrawCircle source)
        {
            var dto = MapBaseShallow(source, new DrawCircleDto());
            dto.RadiusX = source.RadiusX;
            dto.RadiusY = source.RadiusY;
            dto.IsEllipse = source.IsEllipse;
            return dto;
        }

        private static DrawRectangleDto MapRectangleShallow(DrawRectangle source)
        {
            var dto = MapBaseShallow(source, new DrawRectangleDto());
            dto.HasChamfer = source.hasChamfer;
            dto.HasRoundedCorners = source.hasRoundedCorners;
            dto.CornerRadiusTopLeft = source.CornerRadiusTopLeft;
            dto.CornerRadiusTopRight = source.CornerRadiusTopRight;
            dto.CornerRadiusBottomRight = source.CornerRadiusBottomRight;
            dto.CornerRadiusBottomLeft = source.CornerRadiusBottomLeft;
            dto.ChamferTopLeft = source.ChamferTopLeft;
            dto.ChamferTopRight = source.ChamferTopRight;
            dto.ChamferBottomRight = source.ChamferBottomRight;
            dto.ChamferBottomLeft = source.ChamferBottomLeft;
            if (source.Vertices.Count > 0)
            {
                dto.Vertices = MapPoints(source.Vertices);
            }

            return dto;
        }
        private static DrawPolygonDto MapPolygonShallow(DrawPolygon source)
        {
            var dto = MapBaseShallow(source, new DrawPolygonDto());
            dto.SideCount = source.SideCount;
            dto.IsStar = source.IsStar;
            return dto;
        }

        private static HatchDto MapHatchShallow(DrawingHatch source)
        {
            var dto = MapBaseShallow(source, new HatchDto());
            return dto;
        }
        private static GroupDto MapGroup(DrawingGroup source)
        {
            var dto = MapBase(source, new GroupDto());
            dto.ChildrenCount = source.Children.Count;
            if (source.Children.Count > 0)
            {
                dto.Children = MapChildren(source.Children);
            }
            return dto;
        }

        private static CombinationDto MapCombination(DrawCombination source)
        {
            var dto = MapBase(source, new CombinationDto());
                        dto.ChildrenCount = source.Children.Count;
            if (source.Children.Count > 0)
            {
                dto.Children = MapChildren(source.Children);
            }
            return dto;
        }

        private static HatchDto MapHatch(DrawingHatch source)
        {
            var dto = MapBase(source, new HatchDto());
            dto.ChildrenCount = source.Children.Count;
            if (source.Children.Count > 0)
            {
                dto.Children = MapChildren(source.Children);
            }
            return dto;
        }

        private static DrawPolyLines MapPolyLines(DrawPolyLinesDto source)
        {
            var destination = new DrawPolyLines();
            MapBase(source, destination);
            return destination;
        }

        private static DrawPolygon MapPolygon(DrawPolygonDto source)
        {
            var destination = new DrawPolygon();
            MapBase(source, destination);
            return destination;
        }

        private static DrawDot MapDot(DrawDotDto source)
        {
            var destination = new DrawDot();
            MapBase(source, destination);
            return destination;
        }

        private static DrawBezier MapBezier(DrawBezierDto source)
        {
            var points = source.PointsOrNull is { Count: > 0 } sourcePoints
                ? MapSKPoints(sourcePoints)
                : new List<SKPoint>();

            var destination = new DrawBezier(points);
            MapBase(source, destination);
            return destination;
        }

        private static DrawArbitraryCurve MapArbitraryCurve(DrawArbitraryCurveDto source)
        {
            var points = source.PointsOrNull is { Count: > 0 } sourcePoints
                ? MapSKPoints(sourcePoints)
                : new List<SKPoint>();

            var destination = new DrawArbitraryCurve(points);
            MapBase(source, destination);
            return destination;
        }

        private static DrawCircle MapCircle(DrawCircleDto source)
        {
            var destination = new DrawCircle();
            MapBase(source, destination);
            destination.RadiusX = source.RadiusX;
            destination.RadiusY = source.RadiusY;
            destination.IsEllipse = source.IsEllipse;
            if (source.PointsOrNull is not { Count: > 0 } && source.RadiusX > 0)
            {
                var center = destination.SharpCenter;
                destination.Points = new List<SKPoint>(2)
                {
                    center,
                    new(center.X + source.RadiusX, center.Y)
                };
            }

            if (destination.RotationCenter == default)
            {
                destination.SetRotationCenter(destination.SharpCenter);
            }

            return destination;
        }

        private static DrawRectangle MapRectangle(DrawRectangleDto source)
        {
            var destination = new DrawRectangle();
            MapBase(source, destination);
            destination.hasChamfer = source.HasChamfer;
            destination.hasRoundedCorners = source.HasRoundedCorners;
            destination.CornerRadiusTopLeft = source.CornerRadiusTopLeft;
            destination.CornerRadiusTopRight = source.CornerRadiusTopRight;
            destination.CornerRadiusBottomRight = source.CornerRadiusBottomRight;
            destination.CornerRadiusBottomLeft = source.CornerRadiusBottomLeft;
            destination.ChamferTopLeft = source.ChamferTopLeft;
            destination.ChamferTopRight = source.ChamferTopRight;
            destination.ChamferBottomRight = source.ChamferBottomRight;
            destination.ChamferBottomLeft = source.ChamferBottomLeft;
            if (source.Vertices.Count > 0)
            {
                destination.Vertices = MapPointsToPoint2D(source.Vertices);
            }
            return destination;
        }

        private static DrawArc MapArc(DrawArcDto source)
        {
            // ThreePoint 类型：直接使用三点构造恢复圆弧
            if (source.TypeOfArc == ArcTypeDto.ThreePoint && source.PointsOrNull is { Count: >= 3 } arcPoints)
            {
                var start = new Point2D((float)arcPoints[0].X, (float)arcPoints[0].Y);
                var mid = new Point2D((float)arcPoints[1].X, (float)arcPoints[1].Y);
                var end = new Point2D((float)arcPoints[2].X, (float)arcPoints[2].Y);
                var destination = new DrawArc(start, mid, end, source.UseCenter);
                MapBase(source, destination);
                return destination;
            }

            // CenterRadius 类型：转换为三点弧，适配 DrawArc 当前内部实现
            if (source.TypeOfArc == ArcTypeDto.CenterRadius
                && source.Radius > 0
                && source.SharpCenter != default)
            {
                float cx = (float)source.SharpCenter.X;
                float cy = (float)source.SharpCenter.Y;
                float r = (float)source.Radius;
                float startRad = (float)(source.StartAngle * Math.PI / 180.0);
                float sweepRad = (float)(source.SweepAngle * Math.PI / 180.0);

                var start = new Point2D(
                    cx + r * (float)Math.Cos(startRad),
                    cy + r * (float)Math.Sin(startRad));
                var mid = new Point2D(
                    cx + r * (float)Math.Cos(startRad + sweepRad / 2),
                    cy + r * (float)Math.Sin(startRad + sweepRad / 2));
                var end = new Point2D(
                    cx + r * (float)Math.Cos(startRad + sweepRad),
                    cy + r * (float)Math.Sin(startRad + sweepRad));

                var destination = new DrawArc(start, mid, end, source.UseCenter);
                MapBase(source, destination);
                return destination;
            }

            // 退化数据兜底：返回空弧对象，再复制基础属性
            var fallback = new DrawArc();
            MapBase(source, fallback);
            return fallback;
        }

        private static DrawText MapText(DrawTextDto source)
        {
            var center = source.CurrentCenterPoint != default
                ? new Point2D((float)source.CurrentCenterPoint.X, (float)source.CurrentCenterPoint.Y)
                : new Point2D((float)source.SharpCenter.X, (float)source.SharpCenter.Y);

            var destination = new DrawText(source.Text ?? string.Empty, new SKPoint(center.X, center.Y), new TextModel
            {
                Text = source.Text ?? string.Empty,
                FontSettings = new FontSettings
                {
                    FontFamily = source.FontFamily,
                    FontSize = source.FontSize,
                    IsBold = source.IsBold,
                    IsItalic = source.IsItalic,
                    IsUnderline = source.IsUnderline,
                    IsVerticalLayout = source.IsVerticalLayout,
                    HorizontalAlign = (SKTextAlign)source.HorizontalAlign,
                    LineHeight = source.LineHeight,
                    CharacterSpacing = source.CharacterSpacing
                }
            });

            MapBase(source, destination);
            return destination;
        }

        private static DrawingGroup MapGroup(GroupDto source)
        {
            var destination = new DrawingGroup();
            MapBase(source, destination);
            if (source.ChildrenOrNull is { Count: > 0 } children)
            {
                destination.Children.Clear();
                foreach (var child in children)
                {
                    var mappedChild = Map(child);
                    if (mappedChild != null)
                    {
                        destination.Children.Add(mappedChild);
                    }
                }
            }
            return destination;
        }
        
        private static DrawCombination MapCombination(CombinationDto source)
        {
            var destination = new DrawCombination();
            MapBase(source, destination);
            if (source.ChildrenOrNull is { Count: > 0 } children)
            {
                destination.Children.Clear();
                foreach (var child in children)
                {
                    var mappedChild = Map(child);
                    if (mappedChild != null)
                    {
                        destination.Children.Add(mappedChild);
                    }
                }
            }
            destination.RebuildFromChildren();
            return destination;
        }

        private static DrawingHatch MapHatch(HatchDto source)
        {
            var destination = new DrawingHatch();
            MapBase(source, destination);
            if (source.ChildrenOrNull is { Count: > 0 } children2)
            {
                destination.Children.Clear();
                foreach (var child in children2)
                {
                    var mappedChild = Map(child);
                    if (mappedChild != null)
                    {
                        destination.Children.Add(mappedChild);
                    }
                }
            }

            destination.UpdateSetProperty(new List<SKPoint>());
            return destination;
        }

        private static List<Point2D> MapPoints(IReadOnlyList<SKPoint> points)
        {
            var result = new List<Point2D>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                result.Add(new Point2D(point.X, point.Y));
            }

            return result;
        }

        private static List<Point2D> MapPoints(IReadOnlyList<Point2D> points)
        {
            var result = new List<Point2D>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                result.Add(new Point2D(point.X, point.Y));
            }

            return result;
        }

        private static List<SKPoint> MapSKPoints(IReadOnlyList<Point2D> points)
        {
            var result = new List<SKPoint>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                result.Add(new SKPoint((float)point.X, (float)point.Y));
            }

            return result;
        }

        private static List<Point2D> MapPointsToPoint2D(IReadOnlyList<Point2D> points)
        {
            var result = new List<Point2D>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                result.Add(new Point2D((float)point.X, (float)point.Y));
            }

            return result;
        }

        private static List<DrawObjectDto> MapChildren(IReadOnlyList<IShape> children)
        {
            var result = new List<DrawObjectDto>(children.Count);
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is not DrawObject drawObject)
                {
                    continue;
                }

                var dto = Map(drawObject);
                if (dto != null)
                {
                    result.Add(dto);
                }
            }

            return result;
        }
    }
}
