using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.Helpers;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public static partial class DrawObjectExtensions
    {

        internal static void ApplyAbsoluteRotation(
        this IReadOnlyList<DrawObject> shapes,
        SKPoint rotationCenter,
        float angle)
        {
            if (shapes == null || shapes.Count == 0)
                return;

            if (shapes.Count == 1)
            {
                var originalRotate = shapes[0].Rotation;
                shapes.ApplyRotation(angle - originalRotate, rotationCenter, commit: true);
                shapes[0].SetRotationCenter(rotationCenter);
                return;
            }
            else
            {
                shapes.ApplyRotation(angle, rotationCenter, commit: true);
            }
        }

        internal sealed record PartitionDimensionPreparation(
            float PartWidth,
            float PartHeight,
            float StepX,
            float StepY);

        internal sealed record PartitionPreparation(
            List<IShape> NewShapes);

        internal sealed record CurveConversionPreparation(
            List<DrawCombination> Combinations,
            List<IShape> ConvertedSources);

        internal sealed record DotConversionPreparation(
            List<DrawObject> Sources,
            List<DrawObject> Leaves);
        internal sealed record DotGenerationPreparation(
            List<IShape> NewShapes);

        internal static List<IShape> CollectUnlockedShapes(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<IShape>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;

                targets.Add(shape);
            }

            return targets;
        }

        public static SKRect CalculateSharpsBounds(this IEnumerable<IShape> sharps)
        {
            if (sharps == null || sharps.Count() == 0)
                return SKRect.Empty;

            // 单次遍历、零分配，替代原 LINQ 多次迭代 + ToList 分配
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasBounds = false;

            foreach (var item in sharps)
            {
                if (item is DrawObject child)
                {
                    var b = child.GetAABB();
                    if (b.IsEmpty) continue;
                    if (b.Left < minX) minX = b.Left;
                    if (b.Top < minY) minY = b.Top;
                    if (b.Right > maxX) maxX = b.Right;
                    if (b.Bottom > maxY) maxY = b.Bottom;
                    hasBounds = true;
                }
            }

            return hasBounds ? new SKRect(minX, minY, maxX, maxY) : SKRect.Empty;
        }

        public static void ApplyAlignment(
            this IReadOnlyList<DrawObject> shapes,
            AlignTypeDto alignType,
            SKRect alignBounds,
            DrawObject? referenceShape = null)
        {
            if (shapes == null || shapes.Count == 0)
                return;

            foreach (var shape in shapes)
            {
                if (referenceShape != null && ReferenceEquals(shape, referenceShape))
                    continue;

                var bbox = shape.GetAABB();
                SKPoint offset = alignType switch
                {
                    AlignTypeDto.Left => new SKPoint(alignBounds.Left - bbox.Left, 0),
                    AlignTypeDto.Right => new SKPoint(alignBounds.Right - bbox.Right, 0),
                    AlignTypeDto.Center => new SKPoint(alignBounds.MidX - bbox.MidX, 0),
                    AlignTypeDto.Bottom => new SKPoint(0, alignBounds.Top - bbox.Top),
                    AlignTypeDto.Top => new SKPoint(0, alignBounds.Bottom - bbox.Bottom),
                    AlignTypeDto.Middle => new SKPoint(0, alignBounds.MidY - bbox.MidY),
                    _ => SKPoint.Empty
                };
                if (offset != SKPoint.Empty)
                {
                    shape.Translate(offset.X, offset.Y);
                }
            }
        }

        internal static void ApplySkew(
            this IReadOnlyList<DrawObject> shapes,
            float angleX,
            float angleY,
            Func<SKPoint> resolveMultiRotationCenter)
        {
            if (shapes == null || shapes.Count == 0)
                return;

            if (shapes.Count == 1)
            {
                var shape = shapes[0];
                shape.Skew(angleX, angleY, shape.SharpCenter, true);
                return;
            }

            var rotationCenter = resolveMultiRotationCenter();
            foreach (var shape in shapes)
            {
                shape.Skew(angleX, angleY, rotationCenter, true);
            }
        }

        internal static void ApplyClosePath(
            this IReadOnlyList<DrawObject> shapes)
        {
            if (shapes == null || shapes.Count == 0)
                return;

            foreach (var shape in shapes)
            {
                shape.ApplyClosePath();
            }
        }

        internal static void ApplyGeometry(
            this IReadOnlyList<DrawCircle> circles,
            float centerX,
            float centerY,
            float radiusX,
            float radiusY)
        {
            if (circles == null || circles.Count == 0)
                return;

            foreach (var circle in circles)
            {
                circle.AdjustGeometry(centerX, centerY, radiusX, radiusY);
            }
        }

        internal static void ApplyThreePointArc(
            this IReadOnlyList<DrawArc> arcs,
            SKPoint startPoint,
            SKPoint middlePoint,
            SKPoint endPoint)
        {
            if (arcs == null || arcs.Count == 0)
                return;

            foreach (var arc in arcs)
            {
                arc.UpdateThreePointArc(startPoint, middlePoint, endPoint);
            }
        }

        internal static void TranslateAllBy(
            this IReadOnlyList<DrawObject> shapes,
            float dx,
            float dy)
        {
            if (shapes == null || shapes.Count == 0)
                return;

            foreach (var shape in shapes)
            {
                shape.Translate(dx, dy);
            }
        }

        internal static void ApplyPolygonShape(
            this IReadOnlyList<DrawPolygon> polygons,
            int sideCount,
            PolygonType polygonType)
        {
            if (polygons == null || polygons.Count == 0)
                return;

            foreach (var polygon in polygons)
            {
                polygon.AdjustShape(sideCount, polygonType);
            }
        }

        internal static void ApplyCornerRadius(
            this IReadOnlyList<DrawRectangle> rectangles,
            RoundMode mode,
            double topLeft,
            double topRight,
            double bottomRight,
            double bottomLeft)
        {
            if (rectangles == null || rectangles.Count == 0)
                return;

            foreach (var rectangle in rectangles)
            {
                rectangle.AdjustCornerRadius(mode, topLeft, topRight, bottomRight, bottomLeft);
            }
        }

        internal static void ApplyChamfer(
            this IReadOnlyList<DrawRectangle> rectangles,
            RoundMode mode,
            double topLeft,
            double topRight,
            double bottomRight,
            double bottomLeft)
        {
            if (rectangles == null || rectangles.Count == 0)
                return;

            foreach (var rectangle in rectangles)
            {
                rectangle.AdjustChamfer(mode, topLeft, topRight, bottomRight, bottomLeft);
            }
        }

        internal static List<double> CreateCircleCopyAngles(
            int count,
            double startAngle,
            double intervalAngle,
            bool isAverageDistribute,
            bool isCounterClockwise)
        {
            var angles = new List<double>(Math.Max(0, count));
            if (count < 1)
                return angles;

            if (isAverageDistribute)
            {
                double step = 360.0 / count;
                for (int i = 0; i < count; i++)
                    angles.Add(startAngle + i * step);

                return angles;
            }

            double sign = isCounterClockwise ? 1.0 : -1.0;
            for (int i = 0; i < count; i++)
                angles.Add(startAngle + sign * i * intervalAngle);

            return angles;
        }

        internal static MatrixCopyPreparation PrepareMatrixCopy(
            int columnCount,
            double columnSpace,
            int rowCount,
            double rowSpace)
        {
            return new MatrixCopyPreparation(
                columnCount,
                rowCount,
                (float)columnSpace,
                (float)rowSpace);
        }

        internal static void ApplyJumpPointState(
            this IReadOnlyList<DrawObject> draws,
            float skipRadius,
            Func<DrawObject, DrawObject, List<SKPoint>> computeIntersections)
        {
            if (draws == null || draws.Count == 0)
                return;

            foreach (var draw in draws)
            {
                draw.ResetJumpPointState(skipRadius);
            }

            // 1. 单图形自交检测：路径自身交叉点
            foreach (var draw in draws)
            {
                var selfIntersections = draw.ComputeSelfIntersections();
                if (selfIntersections.Count > 0)
                {
                    foreach (var (point, direction) in selfIntersections)
                    {
                        draw.IntersectionSkipPoints.Add(point);
                        draw.IntersectionSkipBridgeDirections.Add(direction);
                    }
                    draw.SelfIntersectionSkipCount = selfIntersections.Count;
                }
            }

            // 2. 多图形之间的交叉检测
            // 交叉点在世界坐标系计算，然后转换到 draws[i] 的本地坐标存储。
            for (int i = 0; i < draws.Count - 1; i++)
            {
                for (int j = i + 1; j < draws.Count; j++)
                {
                    var intersections = computeIntersections(draws[i], draws[j]);
                    if (intersections.Count > 0)
                    {
                        // 将世界坐标交点转换到 draws[i] 的本地坐标
                        var inverseMatrix = draws[i].GetInverseMatrix();
                        foreach (var worldPt in intersections)
                        {
                            var localPt = inverseMatrix.MapPoint(worldPt);
                            draws[i].IntersectionSkipPoints.Add(localPt);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 计算单图形路径的自交点。
        /// 对路径采样后，检测不相邻线段之间的交叉（跳过相邻段共享端点的误判）。
        /// </summary>
        internal static List<(SKPoint Point, SKPoint Direction)> ComputeSelfIntersections(this DrawObject source, float step = 0.5f)
        {
            var result = new List<(SKPoint, SKPoint)>();
            // 使用本地路径采样，使跳点数据存储在本地坐标系，
            // 渲染时通过变换矩阵自动跟随图形旋转/缩放/平移。
            var segments = source.SampleLocalPathToSegments(step);
            if (segments.Count < 3)
                return result;

            for (int i = 0; i < segments.Count - 2; i++)
            {
                // j 从 i+2 开始，跳过相邻线段（共享端点不是真正交叉）
                for (int j = i + 2; j < segments.Count; j++)
                {
                    // 跳过首尾相邻段（闭合路径场景：第一段和最后一段也是相邻的）
                    if (i == 0 && j == segments.Count - 1)
                        continue;

                    if (TryComputeSegmentIntersection(
                        segments[i].P1, segments[i].P2,
                        segments[j].P1, segments[j].P2,
                        out var intersection))
                    {
                        // 闭合轮廓的首尾端点接触不是自交穿越，不能生成跳点裁剪。
                        var isEndpointIntersection =
                            intersection.ArePointsClose(
                            segments[i].P1,
                            segments[i].P2,
                            segments[j].P1,
                            segments[j].P2);

                        if (isEndpointIntersection)
                        {
                            continue;
                        }

                        // "over" 段为路径中靠前的段（段 i），其方向作为桥接方向
                        float dx = segments[i].P2.X - segments[i].P1.X;
                        float dy = segments[i].P2.Y - segments[i].P1.Y;
                        float len = MathF.Sqrt(dx * dx + dy * dy);
                        if (len > 1e-9f)
                        {
                            result.Add((intersection, new SKPoint(dx / len, dy / len)));
                        }
                        else
                        {
                            result.Add((intersection, new SKPoint(1, 0)));
                        }
                    }
                }
            }

            return result;
        }

        internal static List<SKPoint> ComputePathIntersections(
            this DrawObject source,
            DrawObject other)
        {
            var result = new List<SKPoint>();
            if (!source.GetAABB().IntersectsWith(other.GetAABB()))
                return result;

            var sourceSegments = source.SamplePathToSegments();
            var otherSegments = other.SamplePathToSegments();
            if (sourceSegments.Count == 0 || otherSegments.Count == 0)
                return result;

            foreach (var sourceSegment in sourceSegments)
            {
                foreach (var otherSegment in otherSegments)
                {
                    if (TryComputeSegmentIntersection(
                        sourceSegment.P1,
                        sourceSegment.P2,
                        otherSegment.P1,
                        otherSegment.P2,
                        out var intersection))
                    {
                        result.Add(intersection);
                    }
                }
            }

            return result;
        }

        internal static bool TryComputeSegmentIntersection(
            SKPoint p1,
            SKPoint p2,
            SKPoint p3,
            SKPoint p4,
            out SKPoint intersection)
        {
            intersection = SKPoint.Empty;
            float rX = p2.X - p1.X;
            float rY = p2.Y - p1.Y;
            float sX = p4.X - p3.X;
            float sY = p4.Y - p3.Y;
            float denominator = rX * sY - rY * sX;
            if (Math.Abs(denominator) < 1e-9f)
                return false;

            float t = ((p3.X - p1.X) * sY - (p3.Y - p1.Y) * sX) / denominator;
            float u = ((p3.X - p1.X) * rY - (p3.Y - p1.Y) * rX) / denominator;
            if (t is < 0f or > 1f || u is < 0f or > 1f)
                return false;

            intersection = new SKPoint(p1.X + t * rX, p1.Y + t * rY);
            return true;
        }

        internal static List<IShape> CreateMatrixCopyResult(
            this IReadOnlyList<DrawObject> shapes,
            int columnCount,
            int rowCount,
            float horizontalSpacing,
            float verticalSpacing)
        {
            var results = new List<IShape>();
            if (shapes == null || shapes.Count == 0)
                return results;

            results.AddRange(shapes);

            for (int row = 0; row < rowCount; row++)
            {
                for (int col = 0; col < columnCount; col++)
                {
                    if (row == 0 && col == 0)
                        continue;

                    float offsetX = col * horizontalSpacing;
                    float offsetY = row * verticalSpacing;

                    foreach (var shape in shapes)
                    {
                        var clone = shape.Clone();
                        if (clone is DrawObject drawObject)
                        {
                            drawObject.ApplyMatrixCopyOffset(new SKPoint(offsetX, offsetY));
                        }

                        results.Add(clone);
                    }
                }
            }

            return results;
        }

        internal static List<IShape> CreateCircleCopyResult(
            this IReadOnlyList<IShape> shapes,
            IReadOnlyList<double> angles,
            float radius,
            bool rotateWithCircle,
            bool counterClockwise)
        {
            var results = new List<IShape>();
            if (shapes == null || shapes.Count == 0)
                return results;
            if (angles == null || angles.Count == 0)
                return results;

            results.AddRange(shapes);

            double startRad = angles[0] * Math.PI / 180.0;

            for (int i = 1; i < angles.Count; i++)
            {
                double angleRad = angles[i] * Math.PI / 180.0;
                double angleDiff = angles[i] - angles[0];

                float dx = (float)(radius * (Math.Cos(angleRad) - Math.Cos(startRad)));
                float dy = (float)(radius * (Math.Sin(angleRad) - Math.Sin(startRad)));
                var centerOffset = new SKPoint(dx, -dy);

                foreach (var shape in shapes)
                {
                    var clone = shape.Clone();
                    if (clone is DrawObject drawObject)
                    {
                        drawObject.ApplyCircleCopyTransform(
                            centerOffset,
                            (float)angleDiff,
                            rotateWithCircle,
                            counterClockwise);
                    }

                    results.Add(clone);
                }
            }

            return results;
        }

        internal static GraphicResult<CopyContainerPreparation> CreateCopyContainerPreparation(
            this IEnumerable<ILayerViewModel> layerViewModels,
            IReadOnlyList<IShape> sourceShapes,
            IReadOnlyList<IShape> resultShapes)
        {
            if (layerViewModels == null || sourceShapes == null || sourceShapes.Count == 0)
            {
                return GraphicResult<CopyContainerPreparation>.Fail(
                    GraphicErrorCode.CanvasNotFound,
                    "当前没有活动图层");
            }

            var targetLayer = layerViewModels.FirstOrDefault(layer => layer.Contains(sourceShapes[0]));
            if (targetLayer == null)
            {
                return GraphicResult<CopyContainerPreparation>.Fail(
                    GraphicErrorCode.CanvasNotFound,
                    "当前没有活动图层");
            }

            var combination = DrawCombination.CreateContainerResult(
                resultShapes,
                sourceShapes[0] as DrawObject);
            return GraphicResult<CopyContainerPreparation>.Ok(
                new CopyContainerPreparation(targetLayer, combination));
        }

        internal static GraphicResult<SelectionContainerPreparation> CreateSelectionContainerPreparation(
            this IEnumerable<ILayerViewModel> layerViewModels,
            IReadOnlyList<IShape> sourceShapes,
            Func<IReadOnlyList<IShape>, IShape> createContainer)
        {
            if (layerViewModels == null || sourceShapes == null || sourceShapes.Count == 0)
            {
                return GraphicResult<SelectionContainerPreparation>.Fail(
                    GraphicErrorCode.CanvasNotFound,
                    "当前没有活动图层");
            }

            var targetLayer = layerViewModels.FirstOrDefault(layer => layer.Contains(sourceShapes[0]));
            if (targetLayer == null)
            {
                return GraphicResult<SelectionContainerPreparation>.Fail(
                    GraphicErrorCode.CanvasNotFound,
                    "当前没有活动图层");
            }

            var container = createContainer(sourceShapes);
            return GraphicResult<SelectionContainerPreparation>.Ok(
                new SelectionContainerPreparation(targetLayer, container));
        }

        internal static List<ContainerReleasePreparation> CreateContainerReleasePreparations<TContainer>(
            this IEnumerable<ILayerViewModel> layerViewModels,
            IEnumerable<TContainer> sourceShapes,
            Func<TContainer, IReadOnlyList<IShape>> createReleasedChildren)
            where TContainer : class, IShape
        {
            var preparations = new List<ContainerReleasePreparation>();
            if (layerViewModels == null || sourceShapes == null)
                return preparations;

            var layers = layerViewModels.ToList();
            foreach (var sourceShape in sourceShapes)
            {
                var targetLayer = layers.FirstOrDefault(layer => layer.Contains(sourceShape));
                if (targetLayer == null)
                    continue;

                // 查找 sourceShape 在图层中的父容器与索引
                var layerModel = (targetLayer as LayerViewModel)?.Model
                    ?? throw new InvalidOperationException("TargetLayer 必须是 LayerViewModel");
                var (parentContainer, insertIndex) = FindContainerAndIndex(layerModel, sourceShape);

                var releasedChildren = createReleasedChildren(sourceShape)?.ToList()
                    ?? new List<IShape>();
                preparations.Add(new ContainerReleasePreparation(
                    targetLayer,
                    sourceShape,
                    releasedChildren,
                    parentContainer,
                    insertIndex));
            }

            return preparations;
        }

        /// <summary>
        /// 在图层中递归查找指定图形的父容器与索引位置。
        /// 若图形直接位于图层顶层，返回 (null, layerIndex)。
        /// </summary>
        private static (IShape? ParentContainer, int Index) FindContainerAndIndex(DrawingLayer layer, IShape target)
        {
            var layerShapes = layer.AllShapesInternal;
            for (int i = 0; i < layerShapes.Count; i++)
            {
                var topLevel = layerShapes[i];
                if (ReferenceEquals(topLevel, target) || topLevel.UId == target.UId)
                    return (null, i);

                if (TryFindParentInContainer(topLevel, target, out var parent, out var index))
                    return (parent, index);
            }

            return (null, -1);
        }

        /// <summary>
        /// 递归在容器及其子容器中查找 target 的直接父容器与索引。
        /// </summary>
        private static bool TryFindParentInContainer(
            IShape current, IShape target, out IShape? parent, out int index)
        {
            parent = null;
            index = -1;

            if (current is not IContainer container || container.Children == null)
                return false;

            var children = container.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (ReferenceEquals(child, target) || child.UId == target.UId)
                {
                    parent = current;
                    index = i;
                    return true;
                }

                if (TryFindParentInContainer(child, target, out parent, out index))
                    return true;
            }

            return false;
        }

        internal static GraphicResult<ActiveLayerResultPreparation> CreateActiveLayerResultPreparation(
            this ILayerViewModel? activeLayer,
            IReadOnlyList<IShape> resultShapes,
            string emptyResultMessage)
        {
            if (resultShapes == null || resultShapes.Count == 0)
            {
                return GraphicResult<ActiveLayerResultPreparation>.Fail(
                    GraphicErrorCode.EmptyResult,
                    emptyResultMessage);
            }

            if (activeLayer == null)
            {
                return GraphicResult<ActiveLayerResultPreparation>.Fail(
                    GraphicErrorCode.CanvasNotFound,
                    "当前没有活动图层");
            }

            return GraphicResult<ActiveLayerResultPreparation>.Ok(
                new ActiveLayerResultPreparation(activeLayer, resultShapes));
        }

        internal static IDrawingCommand CreateSelectionReplacementCommand(
            this IEnumerable<ILayerViewModel> layerViewModels,
            IReadOnlyList<IShape> selectedShapes,
            ActiveLayerResultPreparation resultPreparation,
            string description)
        {
            return new CompositeCommand(description,
            new List<IDrawingCommand>
            {
                new CommandRemove(layerViewModels, selectedShapes, suppressSelectionPublish: true),
                new CommandAdd(resultPreparation.TargetLayer, resultPreparation.ResultShapes, suppressSelectionPublish: true),
            });
        }

        internal static void ExecuteSelectionReplacement(
            this DrawingCanvas canvas,
            IReadOnlyList<IShape> selectedShapes,
            ActiveLayerResultPreparation resultPreparation,
            string description,
            bool requestRedraw = false,
            bool publishSelectChanged = false)
        {
            canvas.CommandManager.Execute(canvas.LayerViewModels
                .CreateSelectionReplacementCommand(
                    selectedShapes,
                    resultPreparation,
                    description));

            canvas.SetSelectedShapes();

            var context = DocumentContext.Instance;
            if (requestRedraw)
                context?.RequestRedraw();
        }

        internal static void ExecuteLayerAdd(
            this DrawingCanvas canvas,
            ILayerViewModel targetLayer,
            IReadOnlyList<IShape> shapes,
            bool resetPartialRenderWhenJumpLineVisible = false,
            bool requestRedraw = false,
            bool invokeRedraw = false,
            bool suppressSelectionPublish = false)
        {
            canvas.CommandManager.Execute(new CommandAdd(targetLayer, shapes, suppressSelectionPublish: suppressSelectionPublish));

            var context = DocumentContext.Instance;
            if (resetPartialRenderWhenJumpLineVisible && context?.ShowJumpLine == true)
                context.IsPartialRender = false;

            if (requestRedraw)
                context?.RequestRedraw();

            if (invokeRedraw)
                context?.RequestRedraw();
        }

        internal static IDrawingCommand CreateContainerReplacementCommand(
            this IEnumerable<ILayerViewModel> layerViewModels,
            IReadOnlyList<IShape> selectedShapes,
            ILayerViewModel targetLayer,
            IShape containerShape,
            string description)
        {
            return new CompositeCommand(description,
            new List<IDrawingCommand>
            {
                new CommandRemove(layerViewModels, selectedShapes, suppressSelectionPublish: true),
            new CommandAdd(targetLayer, new IShape[] { containerShape }, suppressSelectionPublish: true),
        });
        }

        internal static void ExecuteContainerReplacement(
            this DrawingCanvas canvas,
            IReadOnlyList<IShape> selectedShapes,
            ILayerViewModel targetLayer,
            IShape containerShape,
            string description,
            bool requestRedraw = false,
            bool publishSelectChanged = false,
            bool publishSelectSharpsChange = false)
        {
            canvas.CommandManager.Execute(canvas.LayerViewModels
                .CreateContainerReplacementCommand(
                    selectedShapes,
                    targetLayer,
                    containerShape,
                    description));

            canvas.SetSelectedShapes();

            var context = DocumentContext.Instance;
            if (requestRedraw)
                context?.RequestRedraw();
        }

        internal static void ExecuteSelectionRemoval(
            this DrawingCanvas canvas,
            IReadOnlyList<IShape> selectedShapes,
            bool resetPartialRenderWhenJumpLineVisible = false,
            bool requestRedraw = false,
            bool publishSelectChanged = false)
        {
            canvas.CommandManager.Execute(new CommandRemove(canvas.LayerViewModels, selectedShapes, suppressSelectionPublish: true));
            canvas.ClearSelectedShapes();

            var context = DocumentContext.Instance;
            if (resetPartialRenderWhenJumpLineVisible && context?.ShowJumpLine == true)
                context.IsPartialRender = false;

            if (requestRedraw)
                context?.RequestRedraw();
        }

        internal static IDrawingCommand CreateContainerReleaseCommand(
            this IEnumerable<ContainerReleasePreparation> preparations,
            string description)
        {
            // 按原位置索引从大到小处理，避免同一父容器内先移除低索引导致后续索引偏移。
            var sortedPreparations = preparations
                .OrderByDescending(p => p.InsertIndex)
                .ToList();

            var commands = new List<IDrawingCommand>(sortedPreparations.Count);
            foreach (var preparation in sortedPreparations)
            {
                commands.Add(new CommandContainerRelease(preparation));
            }

            return new CompositeCommand(description, commands);
        }

        internal static void ExecuteContainerRelease(
            this DrawingCanvas canvas,
            IReadOnlyList<ContainerReleasePreparation> preparations,
            string description,
            bool requestRedraw = false,
            bool publishSelectChanged = false,
            bool publishSelectSharpsChange = false)
        {
            canvas.CommandManager.Execute(preparations.CreateContainerReleaseCommand(description));

            // 容器释放命令内部已经维护并刷新了选区；
            // 这里根据调用方需求决定是否静默刷新或发布选择变更事件。
            if (publishSelectChanged || publishSelectSharpsChange)
            {
                canvas.SetSelectedShapes();
            }
            else
            {
                canvas.RefreshSelectedShapesSilently();
            }

            var context = DocumentContext.Instance;
            if (requestRedraw)
                context?.RequestRedraw();
        }

        internal static void ExecuteEditCommand(
            this DrawingCanvas canvas,
            IEnumerable<DrawObject> shapes,
            string description,
            Action applyChanges,
            bool requestRedraw = false)
        {
            var command = new CommandEdit(shapes, description);
            applyChanges();

            command.CaptureAfterState();
            canvas.CommandManager.PushExecutedCommand(command);

            if (requestRedraw)
                DocumentContext.Instance?.RequestRedraw();

            DocumentContext.Instance?.PublishTransformChange();
        }

        internal static void ExecuteTransformCommand(
            this DrawingCanvas canvas,
            IEnumerable<DrawObject> shapes,
            string description,
            Action applyChanges,
            bool includesChildren = true,
            bool resetPartialRenderWhenJumpLineVisible = false)
        {
            var commandShapes = includesChildren
                ? CommandTransform.CollectWithChildren(shapes)
                : shapes as DrawObject[] ?? shapes.ToArray();
            var command = new CommandTransform(commandShapes, description, includesChildren);
            applyChanges();

            command.CaptureAfterState();
            canvas.CommandManager.PushExecutedCommand(command);

            var context = DocumentContext.Instance;
            if (resetPartialRenderWhenJumpLineVisible && context?.ShowJumpLine == true)
                context.IsPartialRender = false;

            context?.PublishTransformChange();
        }

        internal static void ExecuteMirrorCommand(
            this DrawingCanvas canvas,
            IEnumerable<IShape> shapes,
            Action applyChanges,
            bool resetPartialRenderWhenJumpLineVisible = false)
        {
            var commandShapes = CommandTransform.CollectWithChildren(shapes.OfType<DrawObject>());
            var command = new CommandTransform(commandShapes, "镜像", includesChildren: true);
            applyChanges();
            command.CaptureAfterState();

            var context = DocumentContext.Instance;
            if (resetPartialRenderWhenJumpLineVisible && context?.ShowJumpLine == true)
                context.IsPartialRender = false;

            canvas.CommandManager.PushExecutedCommand(command);
            context?.PublishTransformChange();
        }

        internal static void ApplyLockState(
            this IEnumerable<IShape> shapes,
            bool isLocked)
        {
            if (shapes == null)
                return;

            foreach (var shape in shapes)
            {
                if (shape is DrawObject drawObject)
                {
                    drawObject.ApplyLockState(isLocked);
                    continue;
                }

                shape.IsLocked = isLocked;
            }
        }

        internal static BooleanPathPreparation PrepareBooleanPathEntries(
            this IReadOnlyList<DrawObject> shapes,
            DrawObject? lastSelectedShape = null,
            bool reverseAfterLastSelected = false)
        {
            var orderedShapes = shapes?.ToList() ?? new List<DrawObject>();
            if (lastSelectedShape != null && orderedShapes.Contains(lastSelectedShape))
            {
                orderedShapes.Remove(lastSelectedShape);
                orderedShapes.Add(lastSelectedShape);
                if (reverseAfterLastSelected)
                    orderedShapes.Reverse();
            }

            var entries = new List<BooleanPathEntry>(orderedShapes.Count);
            foreach (var draw in orderedShapes)
            {
                var pathInfo = draw.CreateWorldPathInfo();
                if (!pathInfo.HasValue)
                    continue;

                entries.Add(new BooleanPathEntry(pathInfo.Value.Path, pathInfo.Value.IsClosed, draw));
            }

            return new BooleanPathPreparation(orderedShapes, entries);
        }

        internal static List<IShape> CreateKeepMainResult(
            this IReadOnlyList<BooleanPathEntry> pathEntries,
            DrawObject styleSource)
        {
            var allNewShapes = new List<IShape>();
            if (pathEntries == null || pathEntries.Count == 0)
                return allNewShapes;

            var closedEntries = pathEntries.Where(entry => entry.IsClosed).ToList();
            SKPath? closedUnion = null;
            try
            {
                if (closedEntries.Count >= 2)
                {
                    closedUnion = new SKPath(closedEntries[0].WorldPath);
                    allNewShapes.Add(closedEntries[0].Source);

                    for (int i = 1; i < closedEntries.Count; i++)
                    {
                        var entry = closedEntries[i];
                        if (!closedUnion.Bounds.IntersectsWith(entry.WorldPath.Bounds))
                        {
                            allNewShapes.Add(entry.Source);
                            continue;
                        }

                        allNewShapes.AddRange(styleSource.CreateClippedBooleanChildrenFromWorldPath(
                            entry.WorldPath,
                            closedUnion,
                            entry.Source.Name,
                            keepInside: false));
                    }
                }
                else if (closedEntries.Count == 1)
                {
                    allNewShapes.AddRange(pathEntries.Select(entry => entry.Source));
                }

                if (closedEntries.Count >= 2)
                {
                    allNewShapes.AddRange(pathEntries
                        .Where(entry => !entry.IsClosed)
                        .Select(entry => entry.Source));
                }

                return allNewShapes;
            }
            finally
            {
                closedUnion?.Dispose();
            }
        }

        internal static GraphicResult<IShape> CreateBooleanOperationShapeResult(
            this IReadOnlyList<DrawObject> orderedShapes,
            IReadOnlyList<BooleanPathEntry> pathEntries,
            Func<DrawObject, GraphicResult<List<IShape>>> assembleShapes,
            string insufficientPathMessage = "可获取有效路径的图形不足两个",
            string emptyResultMessage = "向量合并后无法生成有效图形")
        {
            if (orderedShapes == null || orderedShapes.Count == 0 || pathEntries == null || pathEntries.Count < 2)
            {
                return GraphicResult<IShape>.Fail(
                    GraphicErrorCode.ShapeTypeMismatch,
                    insufficientPathMessage);
            }

            var styleSource = orderedShapes[0];
            var assembled = assembleShapes(styleSource);
            if (!assembled.IsSuccess)
                return GraphicResult<IShape>.Fail(assembled.ErrorCode, assembled.Message);

            var allNewShapes = assembled.Value!;
            if (allNewShapes.Count == 0)
            {
                return GraphicResult<IShape>.Fail(
                    GraphicErrorCode.EmptyResult,
                    emptyResultMessage);
            }

            return GraphicResult<IShape>.Ok(DrawCombination.CreateBooleanResult(allNewShapes, styleSource));
        }

        internal static GraphicResult<List<IShape>> CreateVectorCombineResult(
            this IReadOnlyList<BooleanPathEntry> pathEntries,
            DrawObject styleSource,
            SKPathOp pathOp)
        {
            var allNewShapes = new List<IShape>();
            if (pathEntries == null || pathEntries.Count == 0)
                return GraphicResult<List<IShape>>.Ok(allNewShapes);

            var closedEntries = pathEntries.Where(entry => entry.IsClosed).ToList();
            var openEntries = pathEntries.Where(entry => !entry.IsClosed).ToList();
            bool preserveClosedResult = openEntries.Count == 0 || pathOp != SKPathOp.Intersect;
            SKPath? closedUnion = null;
            try
            {
                if (closedEntries.Count >= 2)
                {
                    var result = new SKPath(closedEntries[0].WorldPath);
                    string name = closedEntries[0].Source.Name;
                    for (int i = 1; i < closedEntries.Count; i++)
                    {
                        var combined = result.Op(closedEntries[i].WorldPath, pathOp);
                        result.Dispose();
                        if (combined == null || combined.IsEmpty)
                        {
                            combined?.Dispose();
                            return GraphicResult<List<IShape>>.Fail(
                                GraphicErrorCode.EmptyResult,
                                "向量合并运算结果为空");
                        }

                        result = combined;
                        name = $"{name}_{closedEntries[i].Source.Name}";
                    }

                    if (preserveClosedResult)
                    {
                        allNewShapes.AddRange(styleSource.CreateBooleanChildrenFromWorldPath(result, name));
                    }

                    closedUnion = result;
                }
                else if (closedEntries.Count == 1)
                {
                    if (preserveClosedResult)
                    {
                        allNewShapes.AddRange(styleSource.CreateBooleanChildrenFromWorldPath(
                            closedEntries[0].WorldPath,
                            closedEntries[0].Source.Name));
                    }

                    closedUnion = closedEntries[0].WorldPath;
                }
                if (openEntries.Count > 0 && closedUnion == null && pathOp == SKPathOp.Intersect)
                {
                    return GraphicResult<List<IShape>>.Fail(
                        GraphicErrorCode.ShapeTypeMismatch,
                        "开口路径的交集需要至少一个闭合图形作为裁剪区域");
                }

                foreach (var entry in openEntries)
                {
                    if (pathOp == SKPathOp.Union)
                    {
                        if (closedUnion == null)
                        {
                            allNewShapes.AddRange(styleSource.CreateBooleanChildrenFromWorldPath(
                                entry.WorldPath,
                                entry.Source.Name));
                        }
                        else
                        {
                            allNewShapes.AddRange(styleSource.CreateClippedBooleanChildrenFromWorldPath(
                                entry.WorldPath,
                                closedUnion,
                                entry.Source.Name,
                                keepInside: false));
                        }

                        continue;
                    }

                    if (closedUnion == null && pathOp == SKPathOp.ReverseDifference)
                    {
                        allNewShapes.AddRange(styleSource.CreateSplitBooleanChildrenFromWorldPath(
                            entry.WorldPath,
                            openEntries
                                .Where(other => !ReferenceEquals(other.Source, entry.Source))
                                .Select(other => other.WorldPath),
                            entry.Source.Name));
                        continue;
                    }

                    if (pathOp is SKPathOp.Intersect or SKPathOp.ReverseDifference)
                    {
                        allNewShapes.AddRange(styleSource.CreateClippedBooleanChildrenFromWorldPath(
                            entry.WorldPath,
                            closedUnion!,
                            entry.Source.Name,
                            keepInside: true));
                    }
                }

                return GraphicResult<List<IShape>>.Ok(allNewShapes);
            }
            finally
            {
                if (closedUnion != null && closedEntries.Count >= 2)
                    closedUnion.Dispose();
            }
        }

        internal static List<DrawObject> CollectCurveConversionSources(
            this IEnumerable<IShape> shapes)
        {
            var sources = new List<DrawObject>();
            if (shapes == null)
                return sources;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawObject drawObject)
                    continue;
                if (shape is DrawDot)
                    continue;

                sources.Add(drawObject);
            }

            return sources;
        }

        internal static CurveConversionPreparation CreateCurveConversionResult(
            this IReadOnlyList<DrawObject> curveSources)
        {
            var combinations = new List<DrawCombination>();
            var convertedSources = new List<IShape>();
            if (curveSources == null || curveSources.Count == 0)
                return new CurveConversionPreparation(combinations, convertedSources);

            foreach (var drawObject in curveSources)
            {
                var children = drawObject.CreateCurveChildren();
                if (children.Count == 0)
                    continue;

                combinations.Add(DrawCombination.CreateCurveResult(children, drawObject));
                convertedSources.Add(drawObject);
            }

            return new CurveConversionPreparation(combinations, convertedSources);
        }

        internal static PartitionPreparation CreatePartitionResult(
            this IEnumerable<IShape> shapes,
            float partWidth,
            float partHeight,
            float stepX,
            float stepY)
        {
            var newShapes = new List<IShape>();
            if (shapes == null)
                return new PartitionPreparation(newShapes);

            foreach (var shape in shapes)
            {
                if (shape is not DrawObject drawObject)
                    continue;

                newShapes.AddRange(drawObject.CreatePartitionShapes(
                    partWidth,
                    partHeight,
                    stepX,
                    stepY));
            }

            return new PartitionPreparation(newShapes);
        }

        internal static GraphicResult<PartitionDimensionPreparation> PreparePartitionDimensions(
            double partWidth,
            double partHeight,
            double overlapX,
            double overlapY,
            SKRect selectionBounds)
        {
            if (partWidth == 0 && partHeight == 0)
            {
                return GraphicResult<PartitionDimensionPreparation>.Fail(
                    GraphicErrorCode.InvalidArgument,
                    "分割区块长度和宽度必须大于等于0");
            }

            if (partHeight == 0)
                partHeight = selectionBounds.Height;
            if (partWidth == 0)
                partWidth = selectionBounds.Width;

            float pw = (float)partWidth;
            float ph = (float)partHeight;
            float ox = (float)overlapX;
            float oy = (float)overlapY;
            float stepX = pw - ox;
            float stepY = ph - oy;

            if (stepX <= 0 || stepY <= 0)
            {
                return GraphicResult<PartitionDimensionPreparation>.Fail(
                    GraphicErrorCode.InvalidArgument,
                    "重叠长度不能大于等于分割尺寸");
            }

            return GraphicResult<PartitionDimensionPreparation>.Ok(
                new PartitionDimensionPreparation(pw, ph, stepX, stepY));
        }

        internal static DotConversionPreparation PrepareDotConversionLeaves(
            this IEnumerable<IShape> shapes)
        {
            var sources = new List<DrawObject>();
            var leaves = new List<DrawObject>();
            if (shapes == null)
                return new DotConversionPreparation(sources, leaves);

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawObject drawObject)
                    continue;
                if (shape is DrawDot)
                    continue;

                sources.Add(drawObject);
                leaves.AddRange(drawObject.Flatten().OfType<DrawObject>().Where(leaf => !leaf.IsLocked));
            }

            return new DotConversionPreparation(sources, leaves);
        }

        internal static DotGenerationPreparation CreateDotGenerationResult(
            this IEnumerable<DrawObject> leaves,
            float gap,
            float radius,
            bool isCircle,
            bool needCornerPoints,
            float cornerAngleThreshold)
        {
            var newShapes = new List<IShape>();
            if (leaves == null)
                return new DotGenerationPreparation(newShapes);

            foreach (var leaf in leaves)
            {
                newShapes.AddRange(leaf.CreateDotChildren(
                    gap,
                    radius,
                    isCircle,
                    needCornerPoints,
                    cornerAngleThreshold));
            }

            return new DotGenerationPreparation(newShapes);
        }

        internal static List<DrawObject> CollectDimensionTargets(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawObject>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawObject drawObject)
                    continue;
                if (shape.Type == ShapeType.Point)
                    continue;
                if (shape is DrawingHatch h/* && h.TargetShapes.Count > 0*/)
                    continue;

                targets.Add(drawObject);
            }

            return targets;
        }

        internal static List<DrawObject> CollectCenterTargets(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawObject>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawObject drawObject)
                    continue;
                if (shape is DrawingHatch hh && hh.Boundaries.Count > 0)
                    continue;

                targets.Add(drawObject);
            }

            return targets;
        }

        internal static List<DrawObject> CollectUnlockedDrawObjects(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawObject>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawObject drawObject)
                    continue;

                targets.Add(drawObject);
            }

            return targets;
        }

        internal static List<DrawRectangle> CollectUnlockedRectangles(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawRectangle>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawRectangle rectangle)
                    continue;

                targets.Add(rectangle);
            }

            return targets;
        }

        internal static List<DrawCircle> CollectUnlockedCircles(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawCircle>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawCircle circle)
                    continue;

                targets.Add(circle);
            }

            return targets;
        }

        internal static List<DrawArc> CollectUnlockedArcs(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawArc>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawArc arc)
                    continue;

                targets.Add(arc);
            }

            return targets;
        }

        internal static List<DrawPolygon> CollectUnlockedPolygons(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawPolygon>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawPolygon polygon)
                    continue;

                targets.Add(polygon);
            }

            return targets;
        }

        internal static List<IShape> CollectCopyTargets(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<IShape>();
            if (shapes == null)
                return targets;

            bool hasUnlockedShape = false;
            foreach (var shape in shapes)
            {
                targets.Add(shape);
                if (!shape.IsLocked)
                    hasUnlockedShape = true;
            }

            return hasUnlockedShape ? targets : new List<IShape>();
        }

        internal static List<DrawingGroup> CollectSelectedGroups(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawingGroup>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape is not DrawingGroup group)
                    continue;

                targets.Add(group);
            }

            return targets;
        }

        internal static List<DrawCombination> CollectSelectedCombinations(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawCombination>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape is not DrawCombination combination)
                    continue;

                targets.Add(combination);
            }

            return targets;
        }

        internal static List<DrawObject> CollectSelectedDrawObjects(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawObject>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape is not DrawObject drawObject)
                    continue;

                targets.Add(drawObject);
            }

            return targets;
        }

        internal static List<DrawObject> CollectOpenClosableTargets(
            this IEnumerable<IShape> shapes)
        {
            var targets = new List<DrawObject>();
            if (shapes == null)
                return targets;

            foreach (var shape in shapes)
            {
                if (shape.IsLocked)
                    continue;
                if (shape is not DrawObject drawObject)
                    continue;
                if (shape is not DrSoft.Drawing.Controls.IClosable closable || closable.IsClosed)
                    continue;

                targets.Add(drawObject);
            }

            return targets;
        }

        public static void ApplyDistribution(
            this IReadOnlyList<DrawObject> shapes,
            DistributeTypeDto distributeType,
            SKRect areaBounds,
            bool isCanvasArea)
        {
            if (shapes == null || shapes.Count < 2)
                return;

            switch (distributeType)
            {
                case DistributeTypeDto.AlignLeftDistribute:
                    DistributeByEdge(shapes, areaBounds, edge => edge.Left, false, isCanvasArea, isLeadingEdge: true);
                    break;
                case DistributeTypeDto.AlignCenterDistribute:
                    DistributeByCenter(shapes, areaBounds, bb => (bb.Left + bb.Right) / 2f, bb => bb.Width, false, isCanvasArea);
                    break;
                case DistributeTypeDto.AlignRightDistribute:
                    DistributeByEdge(shapes, areaBounds, edge => edge.Right, false, isCanvasArea, isLeadingEdge: false);
                    break;
                case DistributeTypeDto.AlignHorizontalSpaceDistribute:
                    DistributeEqualSpacing(shapes, areaBounds, false, isCanvasArea);
                    break;
                case DistributeTypeDto.AlignTopDistribute:
                    DistributeByEdge(shapes, areaBounds, edge => edge.Top, true, isCanvasArea, isLeadingEdge: true);
                    break;
                case DistributeTypeDto.AlignMiddleDistribute:
                    DistributeByCenter(shapes, areaBounds, bb => (bb.Top + bb.Bottom) / 2f, bb => bb.Height, true, isCanvasArea);
                    break;
                case DistributeTypeDto.AlignBottomDistribute:
                    DistributeByEdge(shapes, areaBounds, edge => edge.Bottom, true, isCanvasArea, isLeadingEdge: false);
                    break;
                case DistributeTypeDto.AlignVerticalSpaceDistribute:
                    DistributeEqualSpacing(shapes, areaBounds, true, isCanvasArea);
                    break;
            }
        }

        private static void DistributeByEdge(
            IReadOnlyList<DrawObject> shapes,
            SKRect areaBounds,
            Func<SKRect, float> getEdge,
            bool isVertical,
            bool isCanvasArea,
            bool isLeadingEdge)
        {
            var shapeEdges = shapes
                .Select(s => new { Shape = s, BBox = s.GetAABB(), Edge = getEdge(s.GetAABB()) })
                .OrderBy(x => x.Edge)
                .ToList();

            if (shapeEdges.Count < 2)
                return;

            float firstEdge;
            float lastEdge;
            if (isCanvasArea)
            {
                float areaStart = isVertical ? areaBounds.Top : areaBounds.Left;
                float areaEnd = isVertical ? areaBounds.Bottom : areaBounds.Right;

                if (isLeadingEdge)
                {
                    var lastBB = shapeEdges[^1].BBox;
                    float lastSize = isVertical ? lastBB.Height : lastBB.Width;
                    firstEdge = areaStart;
                    lastEdge = areaEnd - lastSize;
                }
                else
                {
                    var firstBB = shapeEdges[0].BBox;
                    float firstSize = isVertical ? firstBB.Height : firstBB.Width;
                    firstEdge = areaStart + firstSize;
                    lastEdge = areaEnd;
                }
            }
            else
            {
                firstEdge = shapeEdges[0].Edge;
                lastEdge = shapeEdges[^1].Edge;
            }

            if (Math.Abs(lastEdge - firstEdge) < 0.001f)
                return;

            float step = (lastEdge - firstEdge) / (shapeEdges.Count - 1);
            int startIdx = isCanvasArea ? 0 : 1;
            int endIdx = isCanvasArea ? shapeEdges.Count : shapeEdges.Count - 1;

            for (int i = startIdx; i < endIdx; i++)
            {
                float targetEdge = firstEdge + step * i;
                float offset = targetEdge - shapeEdges[i].Edge;
                if (Math.Abs(offset) < 0.001f)
                    continue;

                shapeEdges[i].Shape.Translate(isVertical ? 0f : offset, isVertical ? offset : 0f);
            }
        }

        private static void DistributeByCenter(
            IReadOnlyList<DrawObject> shapes,
            SKRect areaBounds,
            Func<SKRect, float> getCenter,
            Func<SKRect, float> getSize,
            bool isVertical,
            bool isCanvasArea)
        {
            var shapeCenters = shapes
                .Select(s => new { Shape = s, BBox = s.GetAABB(), Center = getCenter(s.GetAABB()) })
                .OrderBy(x => x.Center)
                .ToList();

            if (shapeCenters.Count < 2)
                return;

            float firstCenter;
            float lastCenter;
            if (isCanvasArea)
            {
                float areaStart = isVertical ? areaBounds.Top : areaBounds.Left;
                float areaEnd = isVertical ? areaBounds.Bottom : areaBounds.Right;
                float firstHalfSize = getSize(shapeCenters[0].BBox) / 2f;
                float lastHalfSize = getSize(shapeCenters[^1].BBox) / 2f;
                firstCenter = areaStart + firstHalfSize;
                lastCenter = areaEnd - lastHalfSize;
            }
            else
            {
                firstCenter = shapeCenters[0].Center;
                lastCenter = shapeCenters[^1].Center;
            }

            if (Math.Abs(lastCenter - firstCenter) < 0.001f)
                return;

            float step = (lastCenter - firstCenter) / (shapeCenters.Count - 1);
            int startIdx = isCanvasArea ? 0 : 1;
            int endIdx = isCanvasArea ? shapeCenters.Count : shapeCenters.Count - 1;

            for (int i = startIdx; i < endIdx; i++)
            {
                float targetCenter = firstCenter + step * i;
                float offset = targetCenter - shapeCenters[i].Center;
                if (Math.Abs(offset) < 0.001f)
                    continue;

                shapeCenters[i].Shape.Translate(isVertical ? 0f : offset, isVertical ? offset : 0f);
            }
        }

        private static void DistributeEqualSpacing(
            IReadOnlyList<DrawObject> shapes,
            SKRect areaBounds,
            bool isVertical,
            bool isCanvasArea)
        {
            var shapeBounds = shapes
                .Select(s => new { Shape = s, Bounds = s.GetAABB() })
                .ToList();

            shapeBounds = isVertical
                ? shapeBounds.OrderBy(x => x.Bounds.Top).ToList()
                : shapeBounds.OrderBy(x => x.Bounds.Left).ToList();

            if (shapeBounds.Count < 2)
                return;

            float totalShapeSize = 0;
            for (int i = 0; i < shapeBounds.Count; i++)
            {
                var bb = shapeBounds[i].Bounds;
                totalShapeSize += isVertical ? bb.Height : bb.Width;
            }

            float areaStart;
            float areaEnd;
            if (isCanvasArea)
            {
                areaStart = isVertical ? areaBounds.Top : areaBounds.Left;
                areaEnd = isVertical ? areaBounds.Bottom : areaBounds.Right;
            }
            else if (isVertical)
            {
                areaStart = shapeBounds[0].Bounds.Top;
                areaEnd = shapeBounds[^1].Bounds.Bottom;
            }
            else
            {
                areaStart = shapeBounds[0].Bounds.Left;
                areaEnd = shapeBounds[^1].Bounds.Right;
            }

            float areaSize = areaEnd - areaStart;
            if (areaSize < 0.001f)
                return;

            float equalGap = (areaSize - totalShapeSize) / (shapeBounds.Count - 1);
            float currentPos = areaStart;

            for (int i = 0; i < shapeBounds.Count; i++)
            {
                var bb = shapeBounds[i].Bounds;
                float shapeSize = isVertical ? bb.Height : bb.Width;
                float currentEdge = isVertical ? bb.Top : bb.Left;
                float offset = currentPos - currentEdge;

                if (Math.Abs(offset) > 0.001f)
                {
                    shapeBounds[i].Shape.Translate(isVertical ? 0f : offset, isVertical ? offset : 0f);
                }

                currentPos += shapeSize + equalGap;
            }
        }
    }
}
