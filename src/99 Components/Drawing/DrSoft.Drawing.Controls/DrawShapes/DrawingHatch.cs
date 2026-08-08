using System.Runtime.CompilerServices;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 填满对象 - 用于将多个图形打包在一起
    /// </summary>
    public class DrawingHatch : DrawObject, IContainer
    {
        internal sealed record BreakFillPreparation(
            List<DrawingHatch> Hatches,
            DrawingGroup Group);

        // ── IShapeData.ChildShapes：重写基类虚方法，返回子图形集合 ────────────
        protected override IReadOnlyList<IShapeData> GetChildShapeData() =>
            Children.OfType<IShapeData>().ToArray();
        // 子级边界框缓存：填充对象常被用于选中/拖拽，避免高频重复汇总所有填充线的边界。
        private SKRect? _cachedChildrenBounds;
        private bool _childrenBoundsDirty = true;

        public DrawingHatch()
        {
            UId = UniqueIdGenerator.NextId();
            Type = ShapeType.Hatch;
            Children = new ChildCollection(InvalidateChildCaches, () => _suppressChildPropagation);
        }

        public DrawingHatch(IEnumerable<IShape> children) : this()
        {
            Children = new ChildCollection(children, InvalidateChildCaches, () => _suppressChildPropagation);
        }

        private bool _suppressChildPropagation;

        /// <summary>供命令层（CommandTransform）在 Restore 时抑制 TranslateContents 副作用。</summary>
        internal bool SuppressChildPropagation
        {
            get => _suppressChildPropagation;
            set => _suppressChildPropagation = value;
        }

        public override bool CanTransform => !IsLocked/* && Boundaries.All(x => x.CanTransform)*/;

        /// <summary>
        /// 目标对象
        /// </summary>
        public List<IShape> Boundaries { get; set; } = new List<IShape>();

        /// <summary>
        /// 填充参数
        /// </summary>
        public HatchParamDto? HatchParamInfo { get; set; }

        /// <summary>填满包含的图形集合</summary>
        public ChildCollection Children { get; init; } = null!;

        public override IShape Clone()
        {
            var clone = new DrawingHatch
            {
                // Boundaries在复制的过程中处理
                //clone.Boundaries.AddRange(Boundaries.Select(x => x.Clone()));
                HatchParamInfo = HatchParamInfo
            };
            clone.Children.AddRange(Children.Select(x => x.Clone()));
            return FinalizeClone(clone);
        }

        public override bool HitTest(SKPoint point, float tolerance = 6.0f)
        {
            foreach (var child in Children)
            {
                if (child.HitTest(point, tolerance))
                    return true;
            }
            return false;
        }

        public override float GetDistanceToPath(SKPoint worldPoint)
        {
            var childrenBounds = GetChildrenBounds();
            if (!childrenBounds.IsEmpty)
            {
                childrenBounds.Inflate(6f, 6f);
                if (!childrenBounds.Contains(worldPoint.X, worldPoint.Y))
                    return float.MaxValue;
            }

            float minDist = float.MaxValue;
            foreach (var child in Children)
            {
                float dist = child.GetDistanceToPath(worldPoint);
                if (dist < minDist) minDist = dist;
            }
            return minDist;
        }

        public override bool IntersectsWith(SKRect rect)
        {
            //var skRect = new SKRect((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
            //foreach (var child in Children)
            //{
            //    if (child.GetBoundingBox().IntersectsWith(skRect))
            //        return true;
            //}
            return true;
        }

        public override SKPath GetPath()
        {
            return new SKPath();
        }
        public override IEnumerable<IShape> Flatten()
        {
            return new IShape[] { this };
        }

        /// <summary>
        /// 使子级缓存失效（由 ChildCollection 回调）。
        /// </summary>
        private void InvalidateChildCaches()
        {
            _childrenBoundsDirty = true;
            _bboxDirty = true;
            NotifyBoundingBoxInvalidated();
        }

        public List<DrawObject> ExpandHatchObject()
        {
            if (Children == null) throw new ArgumentNullException("填充物件为null！");
            if (!Children.Any(x => x is DrawPolyLines)) throw new Exception("填充物件类型不为直线！");
            var fillObjects = Children.Cast<DrawPolyLines>();
            if (fillObjects == null || fillObjects.Count() == 0) return new List<DrawObject>();
            if (fillObjects.Select(x => x.LineStyle).Distinct().Count() > 1) throw new Exception("填充物件类型不一致！");

            LineStyle FillTypeIndex = fillObjects.FirstOrDefault()!.LineStyle;
            SKColor color = fillObjects.FirstOrDefault()!.Pen.Color;
            List<(SKPoint Start, SKPoint End)> hatchLineObjects = new List<(SKPoint Start, SKPoint End)>();
            //同一种类型的填充线
            foreach (var item in fillObjects)
            {
                hatchLineObjects.Add((item.Points[0], item.Points[1]));
            }

            List<DrawObject> result = new List<DrawObject>();
            switch (FillTypeIndex)
            {
                case LineStyle.Solid:
                    //throw new Exception("实线无需解析！");
                    result.AddRange(Children.OfType<DrawPolyLines>());
                    break;

                case LineStyle.Dashed:
                    result.AddRange(HatchRenderHelper.ExpandToDashGeometry(DashRenderType.Dash, hatchLineObjects,
                        HatchRenderHelper.GetDashParameters((int)FillTypeIndex), color, Name));
                    break;

                case LineStyle.Dotted:
                    result.AddRange(HatchRenderHelper.ExpandToDashGeometry(DashRenderType.Dot, hatchLineObjects,
                   HatchRenderHelper.GetDashParameters((int)FillTypeIndex), color, Name));
                    break;
            }

            return result;
        }

        internal void RebuildChildrenFromTargets(HatchParamDto hatchParam)
        {
            if (Boundaries.Count == 0)
                return;

            _suppressChildPropagation = true;
            try
            {
                Children.Clear();

                foreach (var fillObj in Boundaries.OfType<IHatchable>())
                {
                    fillObj.SetHatchParam(hatchParam);
                    fillObj.InvalidateHatch(rebuildImmediately: true);
                    foreach (var hatchObject in fillObj.HatchPattern?.HatchObjects ?? new List<DrawObject>())
                    {
                        Children.Add(hatchObject);
                        if (hatchObject is DrawObject hatchChild)
                        {
                            hatchChild.OwningLayer = OwningLayer;
                            hatchChild.OnShapeSelectedAction = OnShapeSelectedAction;
                            hatchChild.OnShapeDeselectedAction = OnShapeDeselectedAction;
                        }
                    }
                }

                UpdateSetProperty(new List<SKPoint>());
            }
            finally
            {
                _suppressChildPropagation = false;
                InvalidateChildCaches();
            }
        }

        internal void RefillFromTargets(HatchParamDto hatchParam)
        {
            HatchParamInfo = hatchParam;
            foreach (var hatchable in Boundaries.OfType<IHatchable>())
            {
                hatchable.SetHatchParam(hatchParam);
            }

            RebuildChildrenFromTargets(hatchParam);
        }

        internal static List<int> RefillSelectedHatches(
            IReadOnlyList<DrawingHatch> hatches,
            HatchParamDto hatchParam)
        {
            var ids = new List<int>();
            if (hatches == null || hatches.Count == 0)
                return ids;

            foreach (var hatch in hatches)
            {
                hatch.RefillFromTargets(hatchParam);
                ids.Add(hatch.UId);
            }

            return ids;
        }

        internal static DrawingHatch CreateFromTargets(IEnumerable<IHatchable> hatchables, HatchParamDto hatchParam)
        {
            var hatchableList = hatchables.ToList();
            var targetShapes = hatchableList.Cast<IShape>().ToList();
            var hatchObjects = hatchableList
                .SelectMany(shape =>
                {
                    shape.SetHatchParam(hatchParam);
                    return shape.HatchPattern?.HatchObjects ?? new List<DrawObject>();
                })
                .Cast<IShape>()
                .ToList();

            return new DrawingHatch(hatchObjects)
            {
                Boundaries = targetShapes,
                HatchParamInfo = hatchParam
            };
        }

        internal bool IsAffectedBy(ISet<int> targetIds)
        {
            if (targetIds.Contains(UId))
                return true;

            for (int i = 0; i < Boundaries.Count; i++)
            {
                if (targetIds.Contains(Boundaries[i].UId))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 解除与目标对象的关联，并清除填充参数信息。
        /// </summary>
        internal static List<DrawingHatch> CollectSelectedHatches(IEnumerable<IShape> shapes)
        {
            return shapes.OfType<DrawingHatch>().ToList();
        }

        internal static GraphicResult<BreakFillPreparation> PrepareBreakFillResult(
            IEnumerable<IShape>? selectedShapes = null,
            IEnumerable<DrawingHatch>? hatches = null)
        {
            var hatchList = hatches?.ToList() ?? (selectedShapes == null ? null : CollectSelectedHatches(selectedShapes));
            if (hatchList == null || hatchList.Count == 0)
                return GraphicResult<BreakFillPreparation>.Fail(GraphicErrorCode.NothingSelected, string.Empty);

            return GraphicResult<BreakFillPreparation>.Ok(new BreakFillPreparation(
                hatchList,
                CreateBreakFillGroup(hatchList)));
        }

        internal static List<IHatchable> CollectFillTargets(IEnumerable<IShape> shapes)
        {
            //return shapes
            //    .Where(shape => shape is not DrawingHatch)
            //    .SelectMany(shape => shape.Flatten())
            //    .OfType<IHatchable>()
            //    .ToList();

            return shapes
          .Where(shape => shape is not DrawingHatch)
          .SelectMany(shape => ((shape is DrawCombination) ? new[] { shape } : shape.Flatten()))
          .OfType<IHatchable>()
          .ToList();
        }

        internal static GraphicResult<DrawingHatch> PrepareFillCreation(
            HatchParamDto hatchParam,
            IEnumerable<IShape>? selectedShapes = null,
            IEnumerable<IHatchable>? hatchables = null)
        {
            List<IHatchable>? targetList = hatchables?.ToList();
            if (targetList == null)
            {
                if (selectedShapes == null)
                    return GraphicResult<DrawingHatch>.Fail(GraphicErrorCode.NothingSelected, "");

                targetList = CollectFillTargets(selectedShapes);
            }

            if (targetList.Count == 0)
                return GraphicResult<DrawingHatch>.Fail(GraphicErrorCode.ShapeTypeMismatch, "选中图形不支持填充");

            if (!SupportsFillTargets(targetList.Cast<IShape>(), hatchParam, out var shapeInfo))
                return GraphicResult<DrawingHatch>.Fail(
                    GraphicErrorCode.NotImplemented,
                    $"选中图形{shapeInfo}填充暂未实现");

            return GraphicResult<DrawingHatch>.Ok(CreateFromTargets(targetList, hatchParam));
        }

        internal static bool SupportsFillTargets(
            IEnumerable<IShape> shapes,
            HatchParamDto hatchParam,
            out string shapeInfo)
        {
            shapeInfo = string.Empty;
            if (hatchParam.FillTypeIndex == 0 || hatchParam.FillTypeIndex == 1)
                return true;

            bool result = true;
            string typeInfo = hatchParam.FillTypeIndex switch
            {
                0 => "Z字型单向",
                1 => "弓字型双向",
                2 => "回字型",
                3 => "螺旋型",
                _ => string.Empty
            };

            foreach (var shape in shapes)
            {
                if (shape is DrawPolygon)
                {
                    shapeInfo += "多边形，";
                    result = false;
                }
                else if (shape is DrawBezier)
                {
                    shapeInfo += "贝塞尔曲线，";
                    result = false;
                }
                else if (shape is DrawText)
                {
                    shapeInfo += "文本，";
                    result = false;
                }
            }

            if (!result && !string.IsNullOrEmpty(shapeInfo) && shapeInfo.Length > 1)
            {
                shapeInfo = shapeInfo.Remove(shapeInfo.Length - 1, 1);
                shapeInfo += typeInfo;
            }

            return result;
        }

        internal static bool RequiresRegeneration(IEnumerable<IShape> shapes)
        {
            foreach (var shape in shapes)
            {
                if (shape is DrawObject drawObject && drawObject.CanTransform)
                    return true;

                if (shape is IHatchable hatchable && hatchable.HatchParamInfo != null)
                    return true;

                foreach (var leaf in shape.Flatten())
                {
                    if (leaf is IHatchable flattenedHatchable && flattenedHatchable.HatchParamInfo != null)
                        return true;
                }
            }

            return false;
        }

        internal static HashSet<int> CollectRegenerationTargetIds(IEnumerable<IShape> shapes)
        {
            var targetIds = new HashSet<int>();
            foreach (var shape in shapes)
            {
                targetIds.Add(shape.UId);
                foreach (var leaf in shape.Flatten())
                {
                    targetIds.Add(leaf.UId);
                }
            }

            return targetIds;
        }

        internal static List<DrawingHatch> CollectAffectedHatches(IEnumerable<DrawingLayer> layers, ISet<int> targetIds)
        {
            var result = new List<DrawingHatch>();
            foreach (var layer in layers)
            {
                foreach (var shape in layer.AllShapesInternal)
                {
                    if (shape is not DrawingHatch hatch)
                        continue;

                    if (hatch.IsAffectedBy(targetIds))
                        result.Add(hatch);
                }
            }

            return result;
        }

        internal static bool RebuildAffectedHatches(
            IReadOnlyList<DrawingHatch> hatches,
            Action<SKRect>? markDirty = null)
        {
            if (hatches == null || hatches.Count == 0)
                return false;

            bool anyUpdated = false;
            foreach (var hatch in hatches)
            {
                markDirty?.Invoke(hatch.GetAABB());
                hatch.RebuildChildrenFromTargets(hatch.HatchParamInfo);
                markDirty?.Invoke(hatch.GetAABB());
                anyUpdated = true;
            }

            return anyUpdated;
        }

        internal static DrawingGroup CreateBreakFillGroup(IEnumerable<DrawingHatch> hatchs)
        {
            var hatchList = hatchs.ToList();

            var shapes = new List<IShape>();
            foreach (var hatch in hatchList)
            {
                shapes.AddRange(hatch.Children);
            }
            var group = new DrawingGroup(shapes);
            group.UpdateSetProperty(new List<SKPoint>());
            return group;
        }

        internal override List<IShape> CreatePartitionShapes(
            float pw,
            float ph,
            float stepX,
            float stepY,
            Func<List<List<SKPoint>>, SKRect, List<List<SKPoint>>>? clipContours = null)
        {
            var results = new List<IShape>();
            var bbox = GetAABB();
            if (bbox.IsEmpty)
                return results;

            clipContours ??= ClipPartitionContours;
            var contourSources = BuildPartitionContourSources();
            if (contourSources.Count == 0)
                return results;

            int partIndex = 0;
            for (float cx = bbox.Left; cx < bbox.Right; cx += stepX)
            {
                for (float cy = bbox.Top; cy < bbox.Bottom; cy += stepY)
                {
                    float left = cx;
                    float top = cy;
                    float right = Math.Min(cx + pw, bbox.Right);
                    float bottom = Math.Min(cy + ph, bbox.Bottom);
                    if (right - left < 0.01f || bottom - top < 0.01f)
                        continue;

                    var clippedChildren = ClipPartitionChildren(
                        contourSources,
                        new SKRect(left, top, right, bottom),
                        clipContours);
                    var partitionShape = CreatePartitionShape(clippedChildren, ++partIndex);
                    if (partitionShape == null)
                    {
                        partIndex--;
                        continue;
                    }

                    results.Add(partitionShape);
                }
            }

            return results;
        }

        private List<(DrawObject Source, List<List<SKPoint>> Contours)> BuildPartitionContourSources()
        {
            var sources = new List<(DrawObject Source, List<List<SKPoint>> Contours)>();
            foreach (var child in ExpandHatchObject())
            {
                if (child is not DrawObject drawObject)
                    continue;

                if (drawObject is DrawDot dot)
                {
                    sources.Add((dot, new List<List<SKPoint>> { dot.Points }));
                    continue;
                }

                var contourData = drawObject.BuildPartitionContourData();
                if (contourData != null && contourData.Value.Contours.Count > 0)
                    sources.Add((drawObject, contourData.Value.Contours));
            }

            return sources;
        }

        private List<DrawObject> ClipPartitionChildren(
            List<(DrawObject Source, List<List<SKPoint>> Contours)> contourSources,
            SKRect rect,
            Func<List<List<SKPoint>>, SKRect, List<List<SKPoint>>> clipContours)
        {
            var result = new List<DrawObject>();
            int idx = 0;

            foreach (var (source, contours) in contourSources)
            {
                if (source is DrawDot dot)
                {
                    var point = dot.Points[0];
                    if (rect.Contains(point.X, point.Y))
                        result.Add(dot);
                    continue;
                }

                var clippedChains = clipContours(contours, rect);
                foreach (var chain in clippedChains)
                {
                    if (chain.Count < 2)
                        continue;

                    idx++;
                    result.Add(new DrawPolyLines(chain)
                    {
                        Pen = new SKPaint
                        {
                            Color = source.Pen.Color,
                            Style = source.Pen.Style,
                            StrokeWidth = source.Pen.StrokeWidth,
                            IsAntialias = source.Pen.IsAntialias
                        },
                        Name = $"{source.Name}_{idx}",
                        IsClockwise = source.IsClockwise,
                        LayerId = LayerId
                    });
                }
            }

            return result;
        }

        internal override IShape? CreatePartitionShape(IReadOnlyList<DrawObject> clippedChildren, int partIndex)
        {
            if (clippedChildren.Count == 0)
                return null;

            return new DrawCombination(clippedChildren.Cast<IShape>().ToList())
            {
                Pen = new SKPaint
                {
                    Color = Pen.Color,
                    Style = Pen.Style,
                    StrokeWidth = Pen.StrokeWidth,
                    IsAntialias = Pen.IsAntialias
                },
                Name = $"{Name}_{partIndex}",
                IsClockwise = IsClockwise,
                LayerId = LayerId
            };
        }


        #region BoundingBox Overrides

        private SKRect GetChildrenBounds()
        {
            if (_cachedChildrenBounds.HasValue && !_childrenBoundsDirty)
                return _cachedChildrenBounds.Value;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasBounds = false;

            foreach (var child in Children)
            {
                if (child.Points is { Count: > 0 } points)
                {
                    foreach (var point in points)
                    {
                        if (point.X < minX) minX = point.X;
                        if (point.Y < minY) minY = point.Y;
                        if (point.X > maxX) maxX = point.X;
                        if (point.Y > maxY) maxY = point.Y;
                        hasBounds = true;
                    }

                    continue;
                }

                var fallbackBounds = child.GetAABB();
                if (fallbackBounds.IsEmpty)
                    continue;

                if (fallbackBounds.Left < minX) minX = fallbackBounds.Left;
                if (fallbackBounds.Top < minY) minY = fallbackBounds.Top;
                if (fallbackBounds.Right > maxX) maxX = fallbackBounds.Right;
                if (fallbackBounds.Bottom > maxY) maxY = fallbackBounds.Bottom;
                hasBounds = true;
            }

            var result = hasBounds ? new SKRect(minX, minY, maxX, maxY) : SKRect.Empty;
            _cachedChildrenBounds = result;
            _childrenBoundsDirty = false;
            return result;
        }

        private SKRect GetChildrenPreviewBounds()
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasBounds = false;

            foreach (var child in Children.OfType<DrawObject>())
            {
                var preview = child.GetPreviewAABB();
                if (preview.Corners == null || preview.Corners.Length == 0)
                    continue;

                for (int i = 0; i < preview.Corners.Length; i++)
                {
                    var point = preview.Corners[i];
                    if (point.X < minX) minX = point.X;
                    if (point.Y < minY) minY = point.Y;
                    if (point.X > maxX) maxX = point.X;
                    if (point.Y > maxY) maxY = point.Y;
                    hasBounds = true;
                }
            }

            return hasBounds ? new SKRect(minX, minY, maxX, maxY) : SKRect.Empty;
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewAABB()
        {
            return GetChildrenPreviewBounds().CreateBoundsGeometry();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewOBB()
        {
            return GetChildrenPreviewBounds().CreateBoundsGeometry();
        }

        public override SKRect GetAABB()
        {
            return GetChildrenBounds();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetAABB2()
        {
            return GetChildrenBounds().CreateBoundsGeometry();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetOBB()
        {
            return GetChildrenBounds().CreateBoundsGeometry();
        }

        protected override SKRect ComputeCommittedAabbBounds()
        {
            return GetChildrenBounds();
        }
        #endregion


        #region Transform Overrides

        public override void Translate(float dx, float dy, bool commit = true)
        {
            foreach (var child in Children)
            {
                child.Translate(dx, dy, commit);
            }

            base.Translate(dx, dy, commit);
        }

        public override void Scale(float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = false)
        {
            foreach (var child in Children)
            {
                child.Scale(scaleX, scaleY, anchor, directionRad, commit);
            }

            base.Scale(scaleX, scaleY, anchor, directionRad, commit);
        }

        public override void Rotate(float deltaAngle, SKPoint center, bool commit = false)
        {
            foreach (var child in Children)
            {
                child?.Rotate(deltaAngle, center, commit);
            }

            base.Rotate(deltaAngle, center, commit);
        }

        public override void Skew(float tanSkewX, float tanSkewY, SKPoint anchor, bool commit = false)
        {
            foreach (var child in Children)
            {
                child?.Skew(tanSkewX, tanSkewY, anchor, commit);
            }
            base.Skew(tanSkewX, tanSkewY, anchor, commit);
        }

        protected override void OnCommittedMatrixChanged()
        {
            InvalidateChildCaches();
            base.OnCommittedMatrixChanged();
        }
        #endregion


        // ── IShapeMemento 快照 ──────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawingHatchMemento(this);
        }

        /// <summary>
        /// DrawingHatch 专属快照：在基类基础上额外捕获/恢复 HatchParamInfo 和 TargetShapes。
        /// </summary>
        protected class DrawingHatchMemento : DrawObjectMemento
        {
            private readonly HatchParamDto? _hatchParamInfo;
            private readonly List<IShape> _targetShapes;

            public DrawingHatchMemento(DrawingHatch hatch) : base(hatch)
            {
                _hatchParamInfo = hatch.HatchParamInfo;
                _targetShapes = hatch.Boundaries.ToList();
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawingHatch hatch)
                {
                    hatch.HatchParamInfo = _hatchParamInfo;
                    // 恢复 TargetShapes（支持 Group 操作中解除关联的撤销）
                    hatch.Boundaries.Clear();
                    hatch.Boundaries.AddRange(_targetShapes);
                }
            }
        }

    }
}
