using System.Numerics;
using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    internal enum CombinationKind
    {
        Normal = 0,
        Extended = 1,
    }

    /// <summary>
    /// 组合对象：将多个图形组合为一个整体，支持控制点拖动缩放
    /// </summary>
    public class DrawCombination : DrawObject, IHatchable, IContainer
    {
        #region State
        private const int FastBoundsChildCountThreshold = 100000;
        // ── IShapeData.ChildShapes：重写基类虚方法，返回子图形集合 ────────────
        protected override IReadOnlyList<IShapeData> GetChildShapeData() =>
            Children.OfType<IShapeData>().ToArray();
        internal CombinationKind Kind { get; set; } = CombinationKind.Normal;
        private SKPoint[] LocalCorners = new SKPoint[4];
        internal bool _suppressChildPropagation;

        /// <summary>
        /// 标记此组合是否由 BatchBasicShapes 自动创建（仅用于将基础图形合并显示）。
        /// 复制图层时，自动创建的组合会被展开为独立图形，而用户手动创建的组合保持原样。
        /// </summary>
        internal bool IsBatchedBasicShapes { get; set; }

        public ChildCollection Children { get; init; } = null!;
        #endregion

        #region Construction And Factory

        public DrawCombination()
        {
            UId = UniqueIdGenerator.NextId();
            Type = ShapeType.Combination;
            Children = new ChildCollection(InvalidateChildCaches, () => _suppressChildPropagation);
        }

        public DrawCombination(List<IShape> children) : this()
        {
            Children = new ChildCollection(children, InvalidateChildCaches, () => _suppressChildPropagation);
            InitializeBoundsFromChildren(children);
        }

        public override IShape Clone()
        {
            var clone = new DrawCombination
            {
                Kind = Kind,
                LocalCorners = LocalCorners,
                _suppressChildPropagation = true
            };
            foreach (var child in Children)
            {
                clone.Children.Add(child.Clone());
            }
            clone._suppressChildPropagation = false;
            return FinalizeClone(clone);
        }

        internal static DrawCombination CreateContainerResult(IEnumerable<IShape> children, DrawObject? styleSource = null)
        {
            var combination = new DrawCombination(children.ToList());

            if (styleSource != null)
            {
                combination.Pen = new SKPaint
                {
                    Color = styleSource.Pen.Color,
                    Style = styleSource.Pen.Style,
                    StrokeWidth = styleSource.Pen.StrokeWidth,
                    IsAntialias = styleSource.Pen.IsAntialias
                };
            }

            return combination;
        }

        internal static DrawCombination CreateBooleanResult(IEnumerable<IShape> children, DrawObject? styleSource = null)
        {
            return CreateContainerResult(children, styleSource);
        }

        internal static DrawCombination CreateCurveResult(IEnumerable<IShape> children, DrawObject source)
        {
            var combination = new DrawCombination(children.ToList())
            {
                Name = $"{source.Name}",
                Kind = CombinationKind.Extended
            };
            combination.Pen = new SKPaint
            {
                Color = source.Pen.Color,
                Style = source.Pen.Style,
                StrokeWidth = source.Pen.StrokeWidth,
                IsAntialias = source.Pen.IsAntialias
            };

            // 从源图形复制变换属性，保持转换后的组合与源图形位置、旋转、缩放一致
            combination._suppressChildPropagation = true;
            try
            {
                combination.Rotation = source.Rotation;
                combination.ScaleX = source.ScaleX;
                combination.ScaleY = source.ScaleY;
                combination.SkewX = source.SkewX;
                combination.SkewY = source.SkewY;
            }
            finally
            {
                combination._suppressChildPropagation = false;
            }

            if (source is IHatchable hatchable)
                combination.HatchParamInfo = hatchable.HatchParamInfo;

            try
            {
                combination.PathNodes = CreateCurvePathNodes(combination, source);
            }
            catch
            {
                combination.PathNodes = new List<SKPoint>();
            }

            return combination;
        }

        /// <summary>
        /// 委托每个子图形调用自身的 CreateCurveChildren，
        /// 仅将返回结果从世界坐标重映射到组合局部坐标空间。
        /// </summary>
        internal override List<IShape> CreateCurveChildren()
        {
            var result = new List<IShape>();

            foreach (var child in Children.OfType<DrawObject>())
            {
                result.AddRange(child.CreateCurveChildren());
            }

            return result;
        }

        internal static List<SKPoint> CreateCurvePathNodes(DrawCombination combination, DrawObject source)
        {
            var worldToCombo = combination.GetInverseMatrix();

            if (source is DrawCircle circleForNodes)
            {
                float rx = circleForNodes.DrawingRadiusX;
                float ry = circleForNodes.DrawingRadiusY;
                var originalToWorld = circleForNodes.GetTransformMatrix();
                var axisPoints = new[]
                {
                    new SKPoint(0,  ry),
                    new SKPoint(rx,  0),
                    new SKPoint(0, -ry),
                    new SKPoint(-rx, 0),
                };
                return axisPoints
                    .Select(p => worldToCombo.MapPoint(originalToWorld.MapPoint(p)))
                    .ToList();
            }

            if (source is DrawArc)
            {
                var rawNodes = new List<SKPoint>();
                foreach (var child in combination.Children.OfType<DrawObject>())
                {
                    if (child.Points == null)
                        continue;

                    foreach (var p in child.Points)
                        rawNodes.Add(worldToCombo.MapPoint(p));
                }

                return DedupePathNodes(rawNodes);
            }

            if (source is DrawRectangle)
            {
                var rawNodes = new List<SKPoint>();
                foreach (var child in combination.Children.OfType<DrawObject>())
                {
                    if (child is DrawArc childArc)
                    {
                        var wpts = childArc.GetWorldPoints();
                        if (wpts?.Count >= 3)
                        {
                            rawNodes.Add(worldToCombo.MapPoint(wpts[0]));
                            rawNodes.Add(worldToCombo.MapPoint(wpts[2]));
                        }
                    }
                    else if (child.Points != null)
                    {
                        foreach (var p in child.Points)
                            rawNodes.Add(worldToCombo.MapPoint(p));
                    }
                }

                return DedupePathNodes(rawNodes);
            }

            using var path = source.GetPath();
            if (path == null || path.IsEmpty)
                return new List<SKPoint>();

            var originalNodes = ExtractPathNodes(path);
            var originalToWorldMatrix = source.GetTransformMatrix();
            var originalToCombo = SKMatrix.Concat(worldToCombo, originalToWorldMatrix);
            return originalNodes
                .Select(p => originalToCombo.MapPoint(p))
                .ToList();
        }

        private static List<SKPoint> ExtractPathNodes(SKPath path)
        {
            var nodes = new List<SKPoint>();
            using var iter = path.CreateRawIterator();
            var curvePoints = new SKPoint[4];
            SKPathVerb verb;

            while ((verb = iter.Next(curvePoints)) != SKPathVerb.Done)
            {
                SKPoint localPos = verb switch
                {
                    SKPathVerb.Move => curvePoints[0],
                    SKPathVerb.Line => curvePoints[1],
                    SKPathVerb.Quad => curvePoints[2],
                    SKPathVerb.Cubic => curvePoints[3],
                    SKPathVerb.Conic => curvePoints[2],
                    _ => SKPoint.Empty
                };

                if (verb == SKPathVerb.Close || localPos.IsEmpty)
                    continue;

                if (nodes.Count > 0)
                {
                    var last = nodes[^1];
                    float dx = localPos.X - last.X;
                    float dy = localPos.Y - last.Y;
                    if (dx * dx + dy * dy < 1e-6f)
                        continue;
                }

                nodes.Add(localPos);
            }

            if (nodes.Count > 2)
            {
                var first = nodes[0];
                var last = nodes[^1];
                float dx = last.X - first.X;
                float dy = last.Y - first.Y;
                if (dx * dx + dy * dy < 1e-6f)
                    nodes.RemoveAt(nodes.Count - 1);
            }

            return nodes;
        }

        private static List<SKPoint> DedupePathNodes(List<SKPoint> rawNodes)
        {
            var deduped = new List<SKPoint>();
            foreach (var pt in rawNodes)
            {
                bool dup = false;
                foreach (var ex in deduped)
                {
                    float ddx = pt.X - ex.X;
                    float ddy = pt.Y - ex.Y;
                    if (ddx * ddx + ddy * ddy < 1e-4f)
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                    deduped.Add(pt);
            }

            return deduped;
        }

        internal List<IShape> CreateSeparatedChildren()
        {
            return Children.ToList();
        }
        #endregion

        #region Geometry And Hit Testing

        public override SKPath GetPath()
        {
            var result = new SKPath();
            foreach (var child in Children.OfType<DrawObject>())
            {
                using var childPath = child.GetPath();
                if (childPath == null || childPath.IsEmpty) continue;

                // 将子图形路径从其局部坐标系变换到 DrawCombination 的局部坐标系
                var childLocalToWorld = child.GetTransformMatrix();
                var comboWorldToLocal = GetInverseMatrix();
                var childToComboLocal = SKMatrix.Concat(comboWorldToLocal, childLocalToWorld);

                var transformed = new SKPath(childPath);
                transformed.Transform(childToComboLocal);
                result.AddPath(transformed);
                transformed.Dispose();
            }
            return result;
        }

        public override bool HitTest(SKPoint point, float tolerance = 6)
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
            // 快速预过滤：如果点远离组合包围盒，直接返回 float.MaxValue
            float halfW = Width / 2;
            float halfH = Height / 2;
            float dx = worldPoint.X - SharpCenter.X;
            float dy = worldPoint.Y - SharpCenter.Y;
            if (halfW > 0 && halfH > 0)
            {
                // 使用2倍半轴的椭圆作为快速剔除区域（比实际包围盒宽松）
                float normDistSq = (dx * dx) / (halfW * halfW) + (dy * dy) / (halfH * halfH);
                if (normDistSq > 4) // 2倍范围外
                    return float.MaxValue;
            }

            // 进一步优化：只检查包围盒与测试点距离较近的子图形
            // 设定一个合理的搜索半径，比如组合尺寸的10%
            float searchRadius = MathF.Max(Width, Height) * 0.1f;
            float maxSearchRadius = 50f; // 最大搜索半径限制
            searchRadius = MathF.Min(searchRadius, maxSearchRadius);

            float minDist = float.MaxValue;
            bool foundNearby = false;

            foreach (var child in Children)
            {
                // 获取子图形的包围盒
                var childBounds = child.GetAABB();

                // 计算点到子图形包围盒的最近距离
                float distToBounds = DistanceToRect(worldPoint, childBounds);

                // 只处理距离在搜索半径内的子图形
                if (distToBounds <= searchRadius)
                {
                    foundNearby = true;
                    float dist = child.GetDistanceToPath(worldPoint);
                    if (dist < minDist) minDist = dist;

                    // 如果已经非常接近（在容差范围内），可以提前返回
                    if (minDist < 1.0f) break;
                }
            }

            // 如果没有找到附近的子图形，回退到完整遍历
            if (!foundNearby)
            {
                foreach (var child in Children)
                {
                    float dist = child.GetDistanceToPath(worldPoint);
                    if (dist < minDist) minDist = dist;
                }
            }

            return minDist;
        }

        /// <summary>
        /// 计算点到矩形的最短距离
        /// </summary>
        private static float DistanceToRect(SKPoint point, SKRect rect)
        {
            // 如果点在矩形内部，距离为0
            if (point.X >= rect.Left && point.X <= rect.Right &&
                point.Y >= rect.Top && point.Y <= rect.Bottom)
                return 0f;

            // 计算点到矩形四条边的最短距离
            float dx = 0, dy = 0;

            if (point.X < rect.Left)
                dx = rect.Left - point.X;
            else if (point.X > rect.Right)
                dx = point.X - rect.Right;

            if (point.Y < rect.Top)
                dy = rect.Top - point.Y;
            else if (point.Y > rect.Bottom)
                dy = point.Y - rect.Bottom;

            return MathF.Sqrt(dx * dx + dy * dy);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            var skRect = new SKRect((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
            foreach (var child in Children)
            {
                if (child.GetAABB().IntersectsWith(skRect))
                    return true;
            }
            return false;
        }
        #endregion

        #region Child Cache And Preview Geometry

        /// <summary>
        /// 使子级缓存失效（由 ChildCollection 回调 + InvalidateBoundingBox 共用）。
        /// </summary>
        private void InvalidateChildCaches()
        {
            _cachedBoundingBox = null;
            _bboxDirty = true;
            NotifyBoundingBoxInvalidated();
        }

        /// <summary>
        /// 手动使包围盒缓存失效，在外部修改子图形几何数据后调用。
        /// </summary>
        public void InvalidateBoundingBox()
        {
            InvalidateChildCaches();
            RefreshLocalCorners();
        }

        /// <summary>
        /// 从当前子图形重新建立组合对象的辅助几何缓存。
        /// 用于加载/映射后恢复 LocalCorners，以及路径编辑后刷新选择框基准。
        /// </summary>
        internal void RebuildFromChildren()
        {
            SyncTransformFromChildren();
            RefreshLocalCorners();
            InvalidateChildCaches();
        }

        private void SyncTransformFromChildren()
        {
            var drawableChildren = GetDrawableChildren().ToList();
            if (drawableChildren.Count == 0)
                return;

            var center = GetChildrenAabbBounds().Center();
            var currentSnapshot = CaptureTransformCommandSnapshot();
            var updatedMatrix = new SKMatrix
            {
                ScaleX = currentSnapshot.Matrix.ScaleX,
                SkewX = currentSnapshot.Matrix.SkewX,
                TransX = center.X,
                SkewY = currentSnapshot.Matrix.SkewY,
                ScaleY = currentSnapshot.Matrix.ScaleY,
                TransY = center.Y,
                Persp0 = currentSnapshot.Matrix.Persp0,
                Persp1 = currentSnapshot.Matrix.Persp1,
                Persp2 = currentSnapshot.Matrix.Persp2
            };

            RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                updatedMatrix,
                currentSnapshot.Rotation,
                currentSnapshot.ScaleX,
                currentSnapshot.ScaleY,
                currentSnapshot.SkewX,
                currentSnapshot.SkewY,
                center,
                currentSnapshot.ScaleAnchorPoint,
                currentSnapshot.RotationCenterLocal));
        }

        /// <summary>
        /// 重算 LocalCorners（GetPreviewOBB 的局部角点基准）。
        /// LocalCorners 只在构造时快照一次，节点拖动/增删改变子图形几何后必须刷新，
        /// 否则红色选择框（GetPreviewOBB）无法包含拖出原范围的节点。
        /// 注意：必须在"局部坐标系"里求子图形内容的 AABB（逐角点逆映射后取 min/max），
        /// 这样正向映射后红框仍是跟随组合旋转的 OBB 轮廓；
        /// 若直接逆映射世界 union AABB 的四角，旋转后红框会退化成世界轴对齐矩形。
        /// </summary>
        private void RefreshLocalCorners()
        {
            if (UseFastBounds)
            {
                LocalCorners = GetChildrenAabbBounds().ToCorners();
                return;
            }

            if (!TotalPreviewMatrix.TryInvert(out var inverse))
                inverse = SKMatrix.Identity;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool hasData = false;

            foreach (var child in GetDrawableChildren())
            {
                var corners = child.GetPreviewOBB().Corners;
                if (corners == null || corners.Length == 0)
                    continue;

                foreach (var worldCorner in corners)
                {
                    var local = inverse.MapPoint(worldCorner);
                    if (local.X < minX) minX = local.X;
                    if (local.X > maxX) maxX = local.X;
                    if (local.Y < minY) minY = local.Y;
                    if (local.Y > maxY) maxY = local.Y;
                    hasData = true;
                }
            }

            if (!hasData)
                return;

            LocalCorners = new SKRect(minX, minY, maxX, maxY).ToCorners();
        }

        public override void UpdateSetProperty(List<SKPoint> points)
        {
            RebuildFromChildren();
        }
        #endregion

        #region Path Editing And Topology

        /// <summary>
        /// 从子图形的 GetPath() 提取端点（verb 关键点），映射到 combo 局部坐标系。
        /// 此方法与 GetPath() 使用相同的坐标变换链，确保节点始终在图形上。
        /// 所有变换（旋转/镜像/缩放/倾斜）自动传播到节点位置。
        /// 返回 (局部坐标点, 所属子图形, 子图形内的点索引) 三元组列表。
        /// </summary>
        /// <summary>
        /// 实时从子图形收集所有节点的世界坐标列表（带元数据）。
        /// 直接使用子图形的 GetTransformMatrix() 得到世界坐标，无需组合图形矩阵。
        /// 结果已全局去重（世界坐标阈值 1e-4f）。
        /// </summary>
        public List<(SKPoint WorldPos, DrawObject Child, int ChildPointIndex)> GetPathNodeLocalPositions()
        {
            var result = new List<(SKPoint, DrawObject, int)>();

            foreach (var child in Children.OfType<DrawObject>())
            {
                using var childPath = child.GetPath();
                if (childPath == null || childPath.IsEmpty) continue;

                // 直接使用子图形的变换矩阵得到世界坐标
                var childLocalToWorld = child.GetTransformMatrix();

                // 从 child.GetPath() 的 verb 序列提取端点
                // 同时维护一个 childPointIndex 计数器，对应子图形 Points 的索引
                using var iter = childPath.CreateRawIterator();
                var pts = new SKPoint[4];
                SKPathVerb verb;
                int childPointIndex = 0; // 跟踪当前端点在子图形 Points 中的索引

                while ((verb = iter.Next(pts)) != SKPathVerb.Done)
                {
                    SKPoint localPos = verb switch
                    {
                        SKPathVerb.Move => pts[0],
                        SKPathVerb.Line => pts[1],
                        SKPathVerb.Quad => pts[2],
                        SKPathVerb.Cubic => pts[3],
                        SKPathVerb.Conic => pts[2],
                        _ => SKPoint.Empty
                    };

                    if (localPos.IsEmpty) continue;

                    // 所有 verb 的 localPos 都是在曲线上的端点，应作为可拖动节点
                    // 唯一例外：DrawBezier 3控点时，Quad 端点即控制点，不在曲线上，需跳过
                    bool isBezierQuadEndpoint = (verb == SKPathVerb.Quad || verb == SKPathVerb.Conic)
                                                && child is DrawBezier && child.Points?.Count == 3;

                    if (!isBezierQuadEndpoint)
                    {
                        // 直接用子图形矩阵转为世界坐标
                        var worldPos = childLocalToWorld.MapPoint(localPos);
                        // 世界坐标去重，避免 combo-local 空间缩放导致的误判
                        AddIfNotDupWorld(result, worldPos, child, childPointIndex);
                    }

                    // Move/Line/Quad/Cubic/Conic 的端点都推进 childPointIndex
                    // Close 不推进（它只是回到 MoveTo 点）
                    if (verb != SKPathVerb.Close)
                        childPointIndex++;
                }
            }
            return result;
        }

        /// <summary>
        /// 实时从子图形收集所有节点的世界坐标列表。
        /// 基于 GetPathNodeLocalPositions() 返回的世界坐标，无需额外变换。
        /// 结果已全局去重（世界坐标阈值 1e-4f）。
        /// </summary>
        public List<SKPoint> GetPathNodeWorldPositions()
        {
            var localPositions = GetPathNodeLocalPositions();
            var result = new List<SKPoint>();
            foreach (var (worldPos, _, _) in localPositions)
            {
                // 已经是世界坐标，直接去重添加
                AddIfNotDup(result, worldPos);
            }
            return result;
        }

        private static void AddIfNotDup(List<SKPoint> list, SKPoint pt)
        {
            // 世界坐标去重，阈值 1e-4f（0.01mm）
            foreach (var ex in list)
            {
                float dx = pt.X - ex.X, dy = pt.Y - ex.Y;
                if (dx * dx + dy * dy < 1e-9f)
                    return;
            }
            list.Add(pt);
        }

        /// <summary>
        /// 世界坐标去重版本（带元数据）：用世界坐标判断是否重复。
        /// </summary>
        private static void AddIfNotDupWorld(
            List<(SKPoint WorldPos, DrawObject Child, int ChildPointIndex)> list,
            SKPoint worldPos, DrawObject child, int childPointIndex)
        {
            // 世界坐标去重，阈值 1e-4f（0.01mm）
            foreach (var ex in list)
            {
                float dx = worldPos.X - ex.WorldPos.X;
                float dy = worldPos.Y - ex.WorldPos.Y;
                if (dx * dx + dy * dy < 1e-9f) 
                    return;
            }
            list.Add((worldPos, child, childPointIndex));
        }

        internal void MoveChildPathNodeToWorldPosition(DrawObject child, int pointIndex, SKPoint newWorldPos)
        {
            if (child.Points == null || pointIndex < 0 || pointIndex >= child.Points.Count)
                return;

            SKPoint oldPos = child.Points[pointIndex];

            if (child is DrawPolyLines polyLines)
            {
                // 直接传 worldPos 和 pointIndex，由子图形内部用逆矩阵计算局部坐标。
                // 不经过 Points 转换，避免重算所有 _localPoints 导致其他节点跳动。
                polyLines.UpdateLocalPointsInPlace(pointIndex, newWorldPos);
            }
            else if (child is DrawCubicPath cubic)
            {
                var newHandles = cubic.ControlHandles != null
                    ? new List<SKPoint>(cubic.ControlHandles)
                    : null;
                if (newHandles != null && newHandles.Count == cubic.Points.Count * 2)
                {
                    var invMatrix = child.GetInverseMatrix();
                    var localNew = invMatrix.MapPoint(newWorldPos);
                    var localOld = invMatrix.MapPoint(oldPos);
                    float dx = localNew.X - localOld.X;
                    float dy = localNew.Y - localOld.Y;
                    int hi = pointIndex * 2;
                    newHandles[hi] = new SKPoint(newHandles[hi].X + dx, newHandles[hi].Y + dy);
                    newHandles[hi + 1] = new SKPoint(newHandles[hi + 1].X + dx, newHandles[hi + 1].Y + dy);
                }
                cubic.UpdateLocalPointsInPlace(new List<SKPoint>(child.Points), newHandles);
            }
            else
            {
                // 其他类型子图形：走 UpdateSetProperty
                child.Points[pointIndex] = newWorldPos;
                child.UpdateSetProperty(new List<SKPoint>(child.Points));
            }

            // 子图形几何变化后，父组合的 SharpCenter/矩阵平移也必须同步，
            // 否则保存时容器 header 仍会落旧位置。
            RebuildFromChildren();
        }

        internal void SeparatePathNode(DrawObject hitChild, int hitPointIndex, float separationDistance)
        {
            if (hitChild.Points == null || hitPointIndex < 0 || hitPointIndex >= hitChild.Points.Count)
                return;

            // 获取命中节点的真实世界坐标（跨类型匹配需要统一到世界坐标）
            SKPoint hitWorldPos;
            if (hitChild is DrawPolyLines)
                hitWorldPos = hitChild.Points[hitPointIndex]; // DrawPolyLines.Points = 真实世界坐标
            else if (hitChild is DrawCubicPath cubicHit)
            {
                var worldAnchors = cubicHit.GetWorldAnchors();
                hitWorldPos = hitPointIndex < worldAnchors.Count
                    ? worldAnchors[hitPointIndex]
                    : hitChild.Points[hitPointIndex];
            }
            else
                hitWorldPos = hitChild.Points[hitPointIndex];

            // 收集每个子图形内「距离最近」的节点候选，与删除逻辑保持一致。
            // 不用固定阈值 + 第一个命中：比较空间密度随缩放变化会导致误匹配。
            var candidates = new List<(DrawObject Child, int PointIndex, float DistSq)>();
            foreach (var child in Children.OfType<DrawObject>().ToList())
            {
                if (child.Points == null || child.Points.Count == 0)
                    continue;

                // 按子图形类型转换到对应的 Points 坐标空间
                var comparePos = WorldToChildPointsSpace(child, hitWorldPos);

                int bestIndex = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < child.Points.Count; i++)
                {
                    float dx = child.Points[i].X - comparePos.X;
                    float dy = child.Points[i].Y - comparePos.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                    candidates.Add((child, bestIndex, bestDistSq));
            }

            if (candidates.Count == 0)
                return;

            // hitWorldPos 是命中节点的精确世界坐标，真实所属节点回投后距离≈0，
            // 必然是全局最近点。仅接受与全局最近点重合的候选（容忍浮点误差）。
            float globalBestDistSq = candidates.Min(c => c.DistSq);
            float acceptDistSq = MathF.Max(globalBestDistSq + 1e-3f, globalBestDistSq * 4f);
            var matched = candidates
                .Where(c => c.DistSq <= acceptDistSq)
                .OrderBy(c => c.DistSq)
                .Select(c => (c.Child, c.PointIndex))
                .ToList();

            if (matched.Count == 0 || matched.Any(x => x.Child is not DrawPolyLines))
                return;

            float halfDist = separationDistance / 2f;
            bool anyChanged = false;

            foreach (var (child, pointIndex) in matched)
            {
                if (child is not DrawPolyLines poly || poly.Points == null)
                    continue;

                var points = poly.Points;
                bool isStart = pointIndex == 0;
                bool isEnd = pointIndex == points.Count - 1;

                // 闭合路径上的任一顶点都应被视为“断开一个环上的节点”，
                // 结果应是带缺口的一条开折线，而不是把闭环误拆成两段并丢失一条边。
                if (poly.IsClosed && points.Count > 2)
                {
                    int count = points.Count;
                    int nextIndex = (pointIndex + 1) % count;
                    int prevIndex = (pointIndex - 1 + count) % count;

                    SKPoint pb = OffsetPointToward(points[pointIndex], points[nextIndex], halfDist);
                    SKPoint pa = OffsetPointToward(points[pointIndex], points[prevIndex], halfDist);

                    var newPoints = new List<SKPoint> { pb };
                    int current = nextIndex;
                    while (current != pointIndex)
                    {
                        newPoints.Add(points[current]);
                        current = (current + 1) % count;
                    }
                    newPoints.Add(pa);

                    poly.Points = newPoints;
                    poly.IsClosed = false;
                    poly.UpdateLocalPointsInPlace(new List<SKPoint>(newPoints));
                    anyChanged = true;
                    continue;
                }

                if (isStart && points.Count > 1)
                {
                    points[0] = OffsetPointToward(points[0], points[1], halfDist);
                    poly.UpdateLocalPointsInPlace(new List<SKPoint>(points));
                    anyChanged = true;
                    continue;
                }

                if (isEnd && points.Count > 1)
                {
                    int lastIndex = points.Count - 1;
                    points[lastIndex] = OffsetPointToward(points[lastIndex], points[lastIndex - 1], halfDist);
                    poly.UpdateLocalPointsInPlace(new List<SKPoint>(points));
                    anyChanged = true;
                    continue;
                }

                if (!isStart && !isEnd)
                {
                    SeparateInternalPolyNode(poly, pointIndex, halfDist);
                    anyChanged = true;
                }
            }

            if (!anyChanged)
                return;

            _suppressChildPropagation = true;
            try
            {
                UpdateSetProperty(new List<SKPoint>());
            }
            finally
            {
                _suppressChildPropagation = false;
            }

            InvalidateBoundingBox();
        }

        internal bool DeletePathNodeAtWorldPosition(SKPoint deletedCurrentWorld)
        {
            // 收集每个子图形内「距离最近」的节点候选。
            // 不能用固定阈值 + 第一个命中：Points 的比较空间密度随图形缩放变化
            // （尤其 DrawCubicPath/DrawBezier 会经逆矩阵把间距压缩 1/scale），
            // 放大图形后固定阈值会框住相邻节点，导致误删其它点。
            var candidates = new List<(DrawObject Child, int PointIndex, float DistSq)>();
            foreach (var child in Children.OfType<DrawObject>().ToList())
            {
                if (child.Points == null || child.Points.Count == 0)
                    continue;

                // Points 坐标空间因子图形类型而异：
                // DrawPolyLines.Points = Matrix.MapPoint(_localPoints[i]) = 真实世界坐标
                // DrawCubicPath/DrawBezier.Points = 旋转前世界坐标
                var comparePos = WorldToChildPointsSpace(child, deletedCurrentWorld);

                int bestIndex = -1;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < child.Points.Count; i++)
                {
                    float dx = child.Points[i].X - comparePos.X;
                    float dy = child.Points[i].Y - comparePos.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIndex = i;
                    }
                }

                if (bestIndex >= 0)
                    candidates.Add((child, bestIndex, bestDistSq));
            }

            if (candidates.Count == 0)
                return false;

            // deletedCurrentWorld 是上游按缩放自适应命中得到的精确节点世界坐标，
            // 其真实所属节点回投后距离≈0，必然是全局最近点，与缩放比例无关。
            float globalBestDistSq = candidates.Min(c => c.DistSq);

            // 仅接受与全局最近点重合的候选：
            //   - 单图形删除：只有一个候选；
            //   - 分离折线合并：两条折线共享同一端点，两者回投距离都≈全局最近值。
            // 采用「绝对 + 相对」双阈值容忍浮点误差，避免误纳入真实相邻节点。
            float acceptDistSq = MathF.Max(globalBestDistSq + 1e-3f, globalBestDistSq * 4f);
            var matched = candidates
                .Where(c => c.DistSq <= acceptDistSq)
                .OrderBy(c => c.DistSq)
                .Select(c => (c.Child, c.PointIndex))
                .ToList();

            if (matched.Count == 0)
                return false;

            if (TryMergeSeparatedPolyLines(matched))
            {
                RefreshAfterPathEdit();
                return true;
            }

            var (singleChild, matchIndex) = matched[0];
            bool changed = false;

            if (singleChild is DrawCubicPath cubic)
            {
                var newAnchors = new List<SKPoint>(cubic.Points);
                var newHandles = new List<SKPoint>(cubic.ControlHandles);
                newAnchors.RemoveAt(matchIndex);
                if (newHandles.Count == cubic.Points.Count * 2)
                {
                    newHandles.RemoveAt(matchIndex * 2 + 1);
                    newHandles.RemoveAt(matchIndex * 2);
                }

                if (newAnchors.Count < 2)
                    Children.Remove(singleChild);
                else
                    cubic.Initialize(newAnchors, newHandles);

                changed = true;
            }
            else if (singleChild is DrawPolyLines polyLines)
            {
                polyLines.Points.RemoveAt(matchIndex);
                if (polyLines.Points.Count < 2)
                    Children.Remove(singleChild);
                else
                    polyLines.UpdateLocalPointsInPlace(polyLines.Points);

                changed = true;
            }
            else if (singleChild is DrawBezier bezier)
            {
                bezier.Points.RemoveAt(matchIndex);
                if (bezier.Points.Count < 2)
                    Children.Remove(singleChild);
                else
                    bezier.UpdateSetProperty(bezier.Points);

                changed = true;
            }

            if (!changed)
                return false;

            RefreshAfterPathEdit();
            return true;
        }

        internal bool InsertPathNodeAtWorldPosition(SKPoint newWorldPos)
        {
            var pathNodeMeta = GetPathNodeLocalPositions();
            if (pathNodeMeta.Count < 2)
                return false;

            var worldNodes = new List<SKPoint>();
            // pathNodeMeta 已经是世界坐标，直接使用
            foreach (var (worldPos, _, _) in pathNodeMeta)
                AddIfNotDup(worldNodes, worldPos);
            if (worldNodes.Count < 2)
                return false;

            int insertIndex = FindWorldNodesInsertIndex(worldNodes, newWorldPos, IsPathClosed());
            if (insertIndex < 0)
                return false;

            // 优先使用「单子图形快捷路径」：
            // 单子图形时 worldNodes 和 pathNodeMeta 1:1 对应（无去重差异），
            // insertIndex 可直接作为 Points 插入位置，
            // 避免多子图形/去重导致的 prevMeta 索引偏移问题。
            var singleChild = Children.OfType<DrawObject>().FirstOrDefault();
            if (Children.OfType<DrawObject>().Count() == 1 && singleChild is DrawPolyLines poly)
            {
                int pointsInsertAt = insertIndex;
                // 对于闭合折线，FindWorldNodesInsertIndex 可能返回 insertIndex == worldNodes.Count
                // 表示插入到闭合段（末尾→首个），此时 Points 插入位置就是 Points.Count
                if (pointsInsertAt < 0 || pointsInsertAt > poly.Points.Count)
                    return false;

                // DrawPolyLines.Points 存储真实世界坐标，直接用 newWorldPos
                poly.Points.Insert(pointsInsertAt, newWorldPos);
                poly.UpdateLocalPointsInPlace(poly.Points);
                RefreshAfterPathEdit();
                return true;
            }

            // 单子图形 DrawBezier/DrawArbitraryCurve：转为 DrawCubicPath 后用 de Casteljau 分割，
            // 确保新增节点后曲线形状不变（直接向 Points 插入原始锚点会改变 Catmull-Rom 切线方向）。
            if (Children.OfType<DrawObject>().Count() == 1
                && (singleChild is DrawBezier || singleChild is DrawArbitraryCurve))
            {
                var cubic = CurveChildToCubicPath(singleChild);
                if (cubic != null)
                {
                    InsertNodeInCubicPath(cubic, newWorldPos, preserveSharpCenter: Rotation != 0);
                    ReplaceChild(singleChild, cubic);
                    RefreshAfterPathEdit();
                    return true;
                }
                // 转换失败则回退到坐标匹配
            }

            // 多子图形或非 DrawPolyLines 场景：
            // 直接遍历每个子图形的实际路径边段，在世界坐标中找到距点击最近的真实边，
            // （不同子图形首尾节点之间的虚假连接），防止插入到错误的子图形。
            if (!TryInsertIntoNearestChildSegment(newWorldPos))
                return false;

            RefreshAfterPathEdit();
            return true;
        }

        /// <summary>
        /// 遍历每个子图形的实际路径边段，在世界坐标中找到距 newWorldPos 最近的真实边，
        /// 将节点插入对应子图形。
        /// </summary>
        private bool TryInsertIntoNearestChildSegment(SKPoint newWorldPos)
        {
            DrawObject? bestChild = null;
            int bestChildInsertIndex = -1;
            SKPoint bestNewOriginal = SKPoint.Empty;
            float bestDistSq = float.MaxValue;

            foreach (var child in Children.OfType<DrawObject>())
            {
                // DrawPolyLines 直接使用 Points 列表（高效且精确）
                // 注意：DrawPolyLines.Points 存储的是世界坐标（SharpCenter 已烘焙到路径坐标中），
                // 不能再通过 GetTransformMatrix() 映射，否则 SharpCenter 被重复平移。
                if (child is DrawPolyLines poly && poly.Points != null && poly.Points.Count >= 2)
                {
                    var worldPts = poly.Points; // 已经是世界坐标

                    int segCount = poly.IsClosed ? worldPts.Count : worldPts.Count - 1;
                    for (int i = 0; i < segCount; i++)
                    {
                        int next = (i + 1) % worldPts.Count;
                        float distSq = DistanceToSegmentSquared(newWorldPos, worldPts[i], worldPts[next]);
                        if (distSq < bestDistSq)
                        {
                            bestDistSq = distSq;
                            bestChild = child;

                            bool isWrap = poly.IsClosed && i == worldPts.Count - 1;
                            bestChildInsertIndex = isWrap ? poly.Points.Count : i + 1;

                            // Points 存储世界坐标，插入时也直接用世界坐标
                            bestNewOriginal = newWorldPos;
                        }
                    }
                    continue;
                }

                // 其他类型（DrawCubicPath / DrawBezier / DrawArc / DrawCircle / DrawRectangle）
                // 通过 GetPath() 的实际 verb 序列获取真实边段
                using var childPath = child.GetPath();
                if (childPath == null || childPath.IsEmpty) continue;

                var childToCombo = child.GetTransformMatrix();

                using var iter = childPath.CreateRawIterator();
                var pts = new SKPoint[4];
                SKPathVerb verb;
                SKPoint currentPt = SKPoint.Empty;
                SKPoint movePt = SKPoint.Empty;
                int verbSegIndex = 0;

                while ((verb = iter.Next(pts)) != SKPathVerb.Done)
                {
                    switch (verb)
                    {
                        case SKPathVerb.Move:
                            currentPt = pts[0];
                            movePt = pts[0];
                            break;

                        case SKPathVerb.Line:
                            {
                                var w0 = childToCombo.MapPoint(currentPt);
                                var w1 = childToCombo.MapPoint(pts[1]);
                                float distSq = DistanceToSegmentSquared(newWorldPos, w0, w1);
                                if (distSq < bestDistSq)
                                {
                                    bestDistSq = distSq;
                                    bestChild = child;
                                    bestChildInsertIndex = verbSegIndex + 1;
                                    bestNewOriginal = WorldToChildPointsSpace(child, newWorldPos);
                                }
                                currentPt = pts[1];
                                verbSegIndex++;
                                break;
                            }

                        case SKPathVerb.Cubic:
                            {
                                var w0 = childToCombo.MapPoint(currentPt);
                                var w3 = childToCombo.MapPoint(pts[3]);
                                float distSq = DistanceToSegmentSquared(newWorldPos, w0, w3);
                                if (distSq < bestDistSq)
                                {
                                    bestDistSq = distSq;
                                    bestChild = child;
                                    bestChildInsertIndex = verbSegIndex + 1;
                                    bestNewOriginal = WorldToChildPointsSpace(child, newWorldPos);
                                }
                                currentPt = pts[3];
                                verbSegIndex++;
                                break;
                            }

                        case SKPathVerb.Quad:
                        case SKPathVerb.Conic:
                            {
                                var endPt = (verb == SKPathVerb.Quad) ? pts[2] : pts[2];
                                var w0 = childToCombo.MapPoint(currentPt);
                                var wEnd = childToCombo.MapPoint(endPt);
                                float distSq = DistanceToSegmentSquared(newWorldPos, w0, wEnd);
                                if (distSq < bestDistSq)
                                {
                                    bestDistSq = distSq;
                                    bestChild = child;
                                    bestChildInsertIndex = verbSegIndex + 1;
                                    bestNewOriginal = WorldToChildPointsSpace(child, newWorldPos);
                                }
                                currentPt = endPt;
                                verbSegIndex++;
                                break;
                            }

                        case SKPathVerb.Close:
                            {
                                var w0 = childToCombo.MapPoint(currentPt);
                                var w1 = childToCombo.MapPoint(movePt);
                                float closeDistSq = (w0.X - w1.X) * (w0.X - w1.X) + (w0.Y - w1.Y) * (w0.Y - w1.Y);
                                if (closeDistSq > 1e-6f)
                                {
                                    float distSq = DistanceToSegmentSquared(newWorldPos, w0, w1);
                                    if (distSq < bestDistSq)
                                    {
                                        bestDistSq = distSq;
                                        bestChild = child;
                                        bestChildInsertIndex = verbSegIndex + 1;
                                        bestNewOriginal = WorldToChildPointsSpace(child, newWorldPos);
                                    }
                                    verbSegIndex++;
                                }
                                currentPt = movePt;
                                break;
                            }
                    }
                }
            }

            if (bestChild == null)
                return false;

            return InsertNodeIntoChildAtPathNode(bestChild, bestChildInsertIndex, newWorldPos, bestNewOriginal);
        }

        /// <summary>
        /// 在指定子图形的指定路径节点索引处插入新节点。
        /// DrawPolyLines 直接插入 Points；曲线类型转 DrawCubicPath 后用 de Casteljau 分割。
        /// </summary>
        private bool InsertNodeIntoChildAtPathNode(
            DrawObject child, int pathNodeInsertIndex, SKPoint newWorldPos, SKPoint newOriginalPos)
        {
            if (child is DrawPolyLines polyLines)
            {
                int insertAt = Math.Min(pathNodeInsertIndex, polyLines.Points.Count);
                polyLines.Points.Insert(insertAt, newOriginalPos);
                polyLines.UpdateLocalPointsInPlace(polyLines.Points);
                return true;
            }

            if (child is DrawBezier or DrawArbitraryCurve)
            {
                var cubic = CurveChildToCubicPath(child);
                if (cubic == null) return false;
                InsertNodeInCubicPath(cubic, newWorldPos, preserveSharpCenter: Rotation != 0);
                ReplaceChild(child, cubic);
                return true;
            }

            if (child is DrawCubicPath existingCubic)
            {
                InsertNodeInCubicPath(existingCubic, newOriginalPos, preserveSharpCenter: Rotation != 0);
                return true;
            }

            if (child is DrawArc arcChild)
            {
                var worldPts = arcChild.Points;
                if (worldPts == null || worldPts.Count < 3) return false;
                var cubic = ArcToCubicPath(worldPts[0], worldPts[1], worldPts[2], child.Pen);
                if (cubic == null) return false;
                InsertNodeInCubicPath(cubic, newWorldPos);
                ReplaceChild(child, cubic);
                return true;
            }

            if (child is DrawCircle circleChild)
            {
                var cubic = CircleToCubicPath(circleChild);
                if (cubic == null) return false;
                InsertNodeInCubicPath(cubic, newWorldPos);
                ReplaceChild(child, cubic);
                return true;
            }

            // DrawText / DrawRectangle 等其他类型：通过 GetPath() 采样后转为 DrawPolyLines。
            // 多轮廓路径（如文字对象的多个字符）按轮廓拆分为独立的 DrawPolyLines，
            // 避免合并为单条折线时产生轮廓之间的「幽灵连接」。
            using var childPath = child.GetPath();
            if (childPath != null && !childPath.IsEmpty)
            {
                var childTransform = child.GetTransformMatrix();
                // GetPath() 返回局部坐标，通过 GetTransformMatrix() 映射到世界坐标后采样
                using var worldPath = new SKPath(childPath);
                worldPath.Transform(childTransform);

                // 按轮廓拆分：每个 Move 开始一个新的轮廓
                var contours = new List<(List<SKPoint> Points, bool IsClosed)>();
                {
                    List<SKPoint>? current = null;
                    SKPoint movePoint = SKPoint.Empty;

                    using var iter = worldPath.CreateRawIterator();
                    var pts = new SKPoint[4];
                    SKPathVerb verb;
                    while ((verb = iter.Next(pts)) != SKPathVerb.Done)
                    {
                        switch (verb)
                        {
                            case SKPathVerb.Move:
                                if (current != null && current.Count >= 2)
                                    contours.Add((current, false));
                                current = new List<SKPoint> { pts[0] };
                                movePoint = pts[0];
                                break;
                            case SKPathVerb.Line:
                                current?.Add(pts[1]);
                                break;
                            case SKPathVerb.Quad:
                                current?.Add(pts[2]);
                                break;
                            case SKPathVerb.Cubic:
                                current?.Add(pts[3]);
                                break;
                            case SKPathVerb.Conic:
                                current?.Add(pts[2]);
                                break;
                            case SKPathVerb.Close:
                                if (current != null && current.Count >= 2)
                                    contours.Add((current, true));
                                current = null;
                                break;
                        }
                    }
                    if (current != null && current.Count >= 2)
                        contours.Add((current, false));
                }

                if (contours.Count == 0)
                    return false;

                // 对每个轮廓分别采样为独立的 DrawPolyLines
                var contourPolys = new List<DrawPolyLines>();
                var pen = new SKPaint
                {
                    Color = child.Pen.Color,
                    Style = child.Pen.Style,
                    StrokeWidth = child.Pen.StrokeWidth,
                    IsAntialias = child.Pen.IsAntialias
                };

                foreach (var (contourPts, isClosed) in contours)
                {
                    // 对轮廓进行采样（如果是稀疏顶点则用原始顶点，密集曲线则按步长采样）
                    var sampled = SampleContourToPoints(contourPts, isClosed);
                    if (sampled.Count < 2) continue;

                    var poly = new DrawPolyLines(sampled)
                    {
                        IsClosed = isClosed,
                        Pen = new SKPaint
                        {
                            Color = pen.Color,
                            Style = pen.Style,
                            StrokeWidth = pen.StrokeWidth,
                            IsAntialias = pen.IsAntialias
                        },
                        Name = child.Name
                    };
                    contourPolys.Add(poly);
                }

                if (contourPolys.Count == 0)
                    return false;

                // 找到距 newWorldPos 最近的轮廓，在该轮廓的折线中插入新节点
                int nearestIdx = 0;
                float nearestDist = float.MaxValue;
                for (int ci = 0; ci < contourPolys.Count; ci++)
                {
                    var cp = contourPolys[ci];
                    int segCount = cp.IsClosed ? cp.Points.Count : cp.Points.Count - 1;
                    for (int si = 0; si < segCount; si++)
                    {
                        int next = (si + 1) % cp.Points.Count;
                        float d = DistanceToSegmentSquared(newWorldPos, cp.Points[si], cp.Points[next]);
                        if (d < nearestDist)
                        {
                            nearestDist = d;
                            nearestIdx = ci;
                        }
                    }
                }

                // 在最近的轮廓折线中插入新节点
                var targetPoly = contourPolys[nearestIdx];
                int insertAt = FindNearestInsertIndex(targetPoly.Points, newWorldPos, targetPoly.IsClosed);
                targetPoly.Points.Insert(insertAt, newWorldPos);
                targetPoly.UpdateLocalPointsInPlace(targetPoly.Points);

                // 用所有轮廓折线替换原始子图形
                ReplaceChildWithMultiple(child, contourPolys.Cast<IShape>().ToList());
                return true;
            }

            return false;
        }

        internal bool IsPathEndpoint(DrawObject child, int pointIndex)
        {
            if (child?.Points == null || child.Points.Count == 0)
            {
                return false;
            }

            bool isClosed = IsChildPathClosed(child);

            if (isClosed)
            {
                return false;
            }

            bool isStartNode = pointIndex == 0;
            bool isEndNode = pointIndex == child.Points.Count - 1;
            bool isEndpoint = isStartNode || isEndNode;
            return isEndpoint;
        }

        internal bool TryConnectPathNodes(
            DrawObject firstChild,
            int firstPointIndex,
            SKPoint firstWorldPos,
            DrawObject secondChild,
            int secondPointIndex,
            SKPoint secondWorldPos)
        {
            // 先走“端点语义”：
            // 同一路径首尾点 -> 闭合路径；
            // 两条折线端点 -> 直接合并成一条路径。
            // 只有端点规则不成立时，才退回到“补一条 connector 线段”的普通连接。
            bool connectedByEndpointRule = TryConnectPathEndpoints(
                firstChild,
                firstPointIndex,
                secondChild,
                secondPointIndex);
            if (connectedByEndpointRule)
            {
                return true;
            }

            bool isSameNode =
                ReferenceEquals(firstChild, secondChild)
                && firstPointIndex == secondPointIndex;
            if (isSameNode)
            {
                return false;
            }

            float deltaX = firstWorldPos.X - secondWorldPos.X;
            float deltaY = firstWorldPos.Y - secondWorldPos.Y;
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;
            bool isSameWorldPosition = distanceSquared < 1e-4f;
            if (isSameWorldPosition)
            {
                return false;
            }

            bool alreadyConnected = HasDirectConnectionBetweenNodes(firstWorldPos, secondWorldPos);
            if (alreadyConnected)
            {
                return false;
            }

            var connectorPoints = new List<SKPoint>
            {
                firstWorldPos,
                secondWorldPos
            };
            var connector = new DrawPolyLines(connectorPoints)
            {
                Pen = firstChild.Pen?.Clone() ?? new SKPaint(),
                Name = firstChild.Name,
                IsVisible = firstChild.IsVisible,
                IsPathEditing = firstChild.IsPathEditing,
            };

            Children.Add(connector);
            RefreshAfterPathEdit();
            return true;
        }

        internal bool CanConnectPathNodes(
            DrawObject firstChild,
            int firstPointIndex,
            SKPoint firstWorldPos,
            DrawObject secondChild,
            int secondPointIndex,
            SKPoint secondWorldPos)
        {
            // 可连接性判断必须与 TryConnectPathNodes 保持同一语义顺序，
            // 否则会出现“按钮亮了但执行失败”。
            bool canConnectByEndpointRule = CanConnectPathEndpoints(
                firstChild,
                firstPointIndex,
                secondChild,
                secondPointIndex);
            if (canConnectByEndpointRule)
            {
                return true;
            }

            bool isSameNode =
                ReferenceEquals(firstChild, secondChild)
                && firstPointIndex == secondPointIndex;
            if (isSameNode)
            {
                return false;
            }

            float deltaX = firstWorldPos.X - secondWorldPos.X;
            float deltaY = firstWorldPos.Y - secondWorldPos.Y;
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;
            bool isSameWorldPosition = distanceSquared < 1e-4f;
            if (isSameWorldPosition)
            {
                return false;
            }

            bool alreadyConnected = HasDirectConnectionBetweenNodes(firstWorldPos, secondWorldPos);
            if (alreadyConnected)
            {
                return false;
            }

            return true;
        }

        internal bool TryExtendContinuousPathNodes(
            DrawObject child,
            int startPointIndex,
            int endPointIndex,
            SKPoint delta,
            out DrawObject extendedChild,
            out List<int> movedPointIndices)
        {
            extendedChild = null!;
            movedPointIndices = new List<int>();

            // 当前延伸语义不是“移动原节点”，而是：
            // 保留原连续段的首尾点，在中间插入一段整体偏移后的复制段。
            // 因此这里要求至少 2 个连续点，且后续必须返回新复制段的索引给上层恢复选区。
            bool hasValidChild = child?.Points != null && child.Points.Count >= 2;
            if (!hasValidChild)
            {
                return false;
            }

            bool hasValidRange =
                startPointIndex >= 0
                && endPointIndex >= 0
                && startPointIndex < endPointIndex
                && endPointIndex < child.Points.Count;
            if (!hasValidRange)
            {
                return false;
            }

            var targetChild = child;
            bool needsCurveConversion = child is DrawBezier || child is DrawArbitraryCurve;
            if (needsCurveConversion)
            {
                // 非 cubic 曲线统一转为 DrawCubicPath 后再做结构编辑，
                // 这样“复制锚点 + 重建句柄”的规则只需要维护一套。
                var cubicReplacement = CurveChildToCubicPath(child);
                if (cubicReplacement == null)
                {
                    return false;
                }

                ReplaceChild(child, cubicReplacement);
                targetChild = cubicReplacement;
            }

            if (targetChild is DrawPolyLines polyLines)
            {
                var newPoints = BuildExtendedPolylinePoints(
                    polyLines.Points,
                    startPointIndex,
                    endPointIndex,
                    delta);
                polyLines.UpdateLocalPointsInPlace(newPoints);

                movedPointIndices = BuildExtendedMovedPointIndices(startPointIndex, endPointIndex);
                extendedChild = polyLines;
                RefreshAfterPathEdit();
                return true;
            }

            if (targetChild is DrawCubicPath cubicPath)
            {
                var newAnchors = BuildExtendedCubicAnchors(
                    cubicPath.Points,
                    startPointIndex,
                    endPointIndex,
                    delta);
                var newHandles = BuildExtendedCubicHandles(
                    cubicPath.Points,
                    cubicPath.ControlHandles,
                    startPointIndex,
                    endPointIndex,
                    delta);

                cubicPath.InitializePreserveSharpCenter(newAnchors, newHandles);

                movedPointIndices = BuildExtendedMovedPointIndices(startPointIndex, endPointIndex);
                extendedChild = cubicPath;
                RefreshAfterPathEdit();
                return true;
            }

            return false;
        }



        internal bool TryConnectPathEndpoints(
            DrawObject firstChild,
            int firstPointIndex,
            DrawObject secondChild,
            int secondPointIndex)
        {
            // 端点连接是“改原路径拓扑”，不是“额外补线”：
            // 1. 同一 child 的首尾点 -> 闭合；
            // 2. 两条折线的端点 -> 合并为一条新折线。
            bool firstIsEndpoint = IsPathEndpoint(firstChild, firstPointIndex);
            bool secondIsEndpoint = IsPathEndpoint(secondChild, secondPointIndex);
            if (!firstIsEndpoint || !secondIsEndpoint)
            {
                return false;
            }

            if (ReferenceEquals(firstChild, secondChild))
            {
                bool isSameEndpoint = firstPointIndex == secondPointIndex;
                if (isSameEndpoint)
                {
                    return false;
                }

                bool closesWholePath =
                    (firstPointIndex == 0 && secondPointIndex == firstChild.Points.Count - 1)
                    || (secondPointIndex == 0 && firstPointIndex == firstChild.Points.Count - 1);
                if (!closesWholePath)
                {
                    return false;
                }

                firstChild.ApplyClosePath();
                RefreshAfterPathEdit();
                return true;
            }

            if (firstChild is not DrawPolyLines firstPolyLine || secondChild is not DrawPolyLines secondPolyLine)
            {
                return false;
            }

            var firstPoints = new List<SKPoint>(firstPolyLine.Points);
            var secondPoints = new List<SKPoint>(secondPolyLine.Points);

            bool firstNeedsReverse = firstPointIndex == 0;
            if (firstNeedsReverse)
            {
                firstPoints.Reverse();
            }

            bool secondNeedsReverse = secondPointIndex == secondPolyLine.Points.Count - 1;
            if (secondNeedsReverse)
            {
                secondPoints.Reverse();
            }

            var mergedPoints = new List<SKPoint>(firstPoints.Count + secondPoints.Count);
            mergedPoints.AddRange(firstPoints);

            bool hasOverlapPoint = mergedPoints.Count > 0 && secondPoints.Count > 0;
            if (hasOverlapPoint)
            {
                var lastPoint = mergedPoints[mergedPoints.Count - 1];
                var firstPoint = secondPoints[0];
                float deltaX = lastPoint.X - firstPoint.X;
                float deltaY = lastPoint.Y - firstPoint.Y;
                float distanceSquared = deltaX * deltaX + deltaY * deltaY;
                hasOverlapPoint = distanceSquared < 1e-4f;
            }

            int secondStartIndex = hasOverlapPoint ? 1 : 0;
            for (int i = secondStartIndex; i < secondPoints.Count; i++)
            {
                mergedPoints.Add(secondPoints[i]);
            }

            var merged = new DrawPolyLines(mergedPoints)
            {
                Pen = firstPolyLine.Pen?.Clone() ?? new SKPaint(),
                Name = firstPolyLine.Name,
                IsVisible = firstPolyLine.IsVisible,
                IsPathEditing = firstPolyLine.IsPathEditing,
            };

            int firstIndex = Children.IndexOf(firstChild);
            int secondIndex = Children.IndexOf(secondChild);
            int insertIndex = Math.Min(firstIndex, secondIndex);

            Children.Remove(firstChild);
            Children.Remove(secondChild);

            if (insertIndex < 0 || insertIndex > Children.Count)
            {
                insertIndex = Children.Count;
            }

            Children.Insert(insertIndex, merged);
            RefreshAfterPathEdit();
            return true;
        }

        internal bool CanConnectPathEndpoints(
            DrawObject firstChild,
            int firstPointIndex,
            DrawObject secondChild,
            int secondPointIndex)
        {
            bool firstIsEndpoint = IsPathEndpoint(firstChild, firstPointIndex);
            bool secondIsEndpoint = IsPathEndpoint(secondChild, secondPointIndex);
            if (!firstIsEndpoint || !secondIsEndpoint)
            {
                return false;
            }

            if (ReferenceEquals(firstChild, secondChild))
            {
                bool isSameEndpoint = firstPointIndex == secondPointIndex;
                if (isSameEndpoint)
                {
                    return false;
                }

                bool closesWholePath =
                    (firstPointIndex == 0 && secondPointIndex == firstChild.Points.Count - 1)
                    || (secondPointIndex == 0 && firstPointIndex == firstChild.Points.Count - 1);
                return closesWholePath;
            }

            bool areBothPolyLines =
                firstChild is DrawPolyLines
                && secondChild is DrawPolyLines;
            return areBothPolyLines;
        }

        private bool HasDirectConnectionBetweenNodes(SKPoint firstWorldPos, SKPoint secondWorldPos)
        {
            // 普通连接前必须排除“这两个点已经是现有路径的一条边”。
            // 判断方式不是看 child/索引，而是按世界坐标扫描所有 child 的相邻节点边，
            // 这样闭合路径、合并路径、connector 子路径都能统一覆盖。
            var childShapes = Children.OfType<DrawObject>().ToList();
            foreach (var childShape in childShapes)
            {
                var childNodeWorldPositions = GetChildPathNodeWorldPositions(childShape);
                if (childNodeWorldPositions.Count < 2)
                {
                    continue;
                }

                bool childIsClosed = IsChildPathClosed(childShape);
                int lastSequentialIndex = childNodeWorldPositions.Count - 1;
                for (int i = 0; i < lastSequentialIndex; i++)
                {
                    bool matchesSegment = IsSameUndirectedSegment(
                        childNodeWorldPositions[i],
                        childNodeWorldPositions[i + 1],
                        firstWorldPos,
                        secondWorldPos);
                    if (matchesSegment)
                    {
                        return true;
                    }
                }

                if (!childIsClosed)
                {
                    continue;
                }

                var closingStart = childNodeWorldPositions[childNodeWorldPositions.Count - 1];
                var closingEnd = childNodeWorldPositions[0];
                bool matchesClosingSegment = IsSameUndirectedSegment(
                    closingStart,
                    closingEnd,
                    firstWorldPos,
                    secondWorldPos);
                if (matchesClosingSegment)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsChildPathClosed(DrawObject child)
        {
            bool isClosed = false;
            if (child is DrawPolyLines polyLines)
            {
                isClosed = polyLines.IsClosed;
            }
            else if (child is DrawCubicPath cubicPath)
            {
                isClosed = cubicPath.IsClosed;
            }
            else if (child is DrawBezier bezier)
            {
                isClosed = bezier.IsClosed;
            }
            else if (child is DrawArbitraryCurve arbitraryCurve)
            {
                isClosed = arbitraryCurve.IsClosed;
            }
            else if (child is DrawRectangle or DrawCircle or DrawArc)
            {
                isClosed = true;
            }

            return isClosed;
        }

        private static bool IsSameUndirectedSegment(
            SKPoint firstSegmentStart,
            SKPoint firstSegmentEnd,
            SKPoint secondSegmentStart,
            SKPoint secondSegmentEnd)
        {
            bool sameDirection =
                IsSamePoint(firstSegmentStart, secondSegmentStart)
                && IsSamePoint(firstSegmentEnd, secondSegmentEnd);
            if (sameDirection)
            {
                return true;
            }

            bool oppositeDirection =
                IsSamePoint(firstSegmentStart, secondSegmentEnd)
                && IsSamePoint(firstSegmentEnd, secondSegmentStart);
            return oppositeDirection;
        }

        private static bool IsSamePoint(SKPoint firstPoint, SKPoint secondPoint)
        {
            float deltaX = firstPoint.X - secondPoint.X;
            float deltaY = firstPoint.Y - secondPoint.Y;
            float distanceSquared = deltaX * deltaX + deltaY * deltaY;
            bool isSame = distanceSquared < 1e-4f;
            return isSame;
        }

        private static List<SKPoint> GetChildPathNodeWorldPositions(DrawObject child)
        {
            var childNodeWorldPositions = new List<SKPoint>();

            using var childPath = child.GetPath();
            if (childPath == null || childPath.IsEmpty)
            {
                return childNodeWorldPositions;
            }

            var childTransform = child.GetTransformMatrix();
            using var iter = childPath.CreateRawIterator();
            var pts = new SKPoint[4];
            SKPathVerb verb;

            while ((verb = iter.Next(pts)) != SKPathVerb.Done)
            {
                SKPoint localPos = verb switch
                {
                    SKPathVerb.Move => pts[0],
                    SKPathVerb.Line => pts[1],
                    SKPathVerb.Quad => pts[2],
                    SKPathVerb.Cubic => pts[3],
                    SKPathVerb.Conic => pts[2],
                    _ => SKPoint.Empty
                };

                if (localPos.IsEmpty)
                {
                    continue;
                }

                bool isBezierQuadEndpoint =
                    (verb == SKPathVerb.Quad || verb == SKPathVerb.Conic)
                    && child is DrawBezier
                    && child.Points?.Count == 3;
                if (isBezierQuadEndpoint)
                {
                    continue;
                }

                var worldPos = childTransform.MapPoint(localPos);
                AddIfNotDup(childNodeWorldPositions, worldPos);
            }

            return childNodeWorldPositions;
        }

        private void SeparateInternalPolyNode(DrawPolyLines poly, int pointIndex, float halfDist)
        {
            var points = poly.Points;
            if (points == null || pointIndex <= 0 || pointIndex >= points.Count - 1)
                return;

            SKPoint pa = OffsetPointToward(points[pointIndex], points[pointIndex - 1], halfDist);
            SKPoint pb = OffsetPointToward(points[pointIndex], points[pointIndex + 1], halfDist);

            var seg1Points = new List<Point2D>(pointIndex + 1);
            for (int i = 0; i < pointIndex; i++)
                seg1Points.Add(new Point2D(points[i].X, points[i].Y));
            seg1Points.Add(new Point2D(pa.X, pa.Y));

            var seg2Points = new List<Point2D>(points.Count - pointIndex);
            seg2Points.Add(new Point2D(pb.X, pb.Y));
            for (int i = pointIndex + 1; i < points.Count; i++)
                seg2Points.Add(new Point2D(points[i].X, points[i].Y));

            var seg1 = new DrawPolyLines(seg1Points)
            {
                Pen = poly.Pen?.Clone() ?? new SKPaint(),
                Name = poly.Name,
                IsVisible = poly.IsVisible,
                IsPathEditing = poly.IsPathEditing,
            };
            var seg2 = new DrawPolyLines(seg2Points)
            {
                Pen = poly.Pen?.Clone() ?? new SKPaint(),
                Name = poly.Name,
                IsVisible = poly.IsVisible,
                IsPathEditing = poly.IsPathEditing,
            };

            int insertIndex = Children.IndexOf(poly);
            Children.Remove(poly);
            if (insertIndex < 0 || insertIndex > Children.Count)
                insertIndex = Children.Count;

            Children.Insert(insertIndex, seg1);
            Children.Insert(insertIndex + 1, seg2);
        }

        private bool IsPathClosed()
        {
            foreach (var child in Children.OfType<DrawObject>())
            {
                if (child is DrawPolyLines polyLines && polyLines.IsClosed)
                    return true;
                if (child is DrawCubicPath cubicPath && cubicPath.IsClosed)
                    return true;
                // 矩形、圆、弧的路径天然闭合
                if (child is DrawRectangle or DrawCircle or DrawArc)
                    return true;
            }

            var worldNodes = GetPathNodeWorldPositions();
            if (worldNodes.Count < 2)
                return false;

            var first = worldNodes[0];
            var last = worldNodes[^1];
            float dx = last.X - first.X;
            float dy = last.Y - first.Y;
            return dx * dx + dy * dy < 1e-4f;
        }

        private static int FindWorldNodesInsertIndex(List<SKPoint> worldNodes, SKPoint newWorldPos, bool isClosedPath)
        {
            if (worldNodes.Count < 2)
                return -1;

            int bestIndex = -1;
            float bestDistSq = float.MaxValue;
            int segmentCount = isClosedPath ? worldNodes.Count : worldNodes.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % worldNodes.Count;
                float distSq = DistanceToSegmentSquared(newWorldPos, worldNodes[i], worldNodes[next]);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestIndex = i;
                }
            }

            return bestIndex >= 0 ? bestIndex + 1 : -1;
        }

        private void ReplaceChild(DrawObject oldChild, DrawObject newChild)
        {
            int replaceIndex = Children.IndexOf(oldChild);
            if (replaceIndex < 0)
                return;

            Children.RemoveAt(replaceIndex);
            Children.Insert(replaceIndex, newChild);
        }

        /// <summary>
        /// 将一个子图形替换为多个子图形（保持插入位置）。
        /// </summary>
        private void ReplaceChildWithMultiple(DrawObject oldChild, List<IShape> newChildren)
        {
            int replaceIndex = Children.IndexOf(oldChild);
            if (replaceIndex < 0)
                return;

            Children.RemoveAt(replaceIndex);
            for (int i = 0; i < newChildren.Count; i++)
                Children.Insert(replaceIndex + i, newChildren[i]);
        }

        /// <summary>
        /// 对轮廓顶点进行采样：顶点稀疏（≤合理阈值）时直接使用原始顶点，
        /// 密集曲线时按 CurveConversionStepMm 步长采样。
        /// </summary>
        private static List<SKPoint> SampleContourToPoints(List<SKPoint> rawPoints, bool isClosed)
        {
            if (rawPoints.Count < 2) return new List<SKPoint>();

            // 计算轮廓总长度
            float totalLength = 0;
            int segCount = isClosed ? rawPoints.Count : rawPoints.Count - 1;
            for (int i = 0; i < segCount; i++)
            {
                int next = (i + 1) % rawPoints.Count;
                float dx = rawPoints[next].X - rawPoints[i].X;
                float dy = rawPoints[next].Y - rawPoints[i].Y;
                totalLength += MathF.Sqrt(dx * dx + dy * dy);
            }

            // 如果顶点数量合理且轮廓不太长，直接使用原始顶点（保留矩形等简单形状的精确角点）
            const int maxRawVertices = 50;
            if (rawPoints.Count <= maxRawVertices && totalLength < 100f)
                return new List<SKPoint>(rawPoints);

            // 密集曲线：按步长采样
            float step = CurveConversionStepMm;
            var result = new List<SKPoint>();
            float accumulated = 0;

            for (int i = 0; i < segCount; i++)
            {
                int next = (i + 1) % rawPoints.Count;
                float dx = rawPoints[next].X - rawPoints[i].X;
                float dy = rawPoints[next].Y - rawPoints[i].Y;
                float segLen = MathF.Sqrt(dx * dx + dy * dy);
                if (segLen < 1e-6f) continue;

                // 添加段起点
                if (i == 0) result.Add(rawPoints[i]);

                // 在段内按步长插入采样点
                float dist = step - (accumulated % step);
                if (dist < 1e-6f) dist = step;
                while (dist < segLen)
                {
                    float t = dist / segLen;
                    result.Add(new SKPoint(
                        rawPoints[i].X + dx * t,
                        rawPoints[i].Y + dy * t));
                    dist += step;
                }
                accumulated += segLen;
            }

            // 闭合轮廓不需要重复添加终点（首尾自然相连）
            // 开放轮廓确保添加终点
            if (!isClosed && rawPoints.Count > 0)
            {
                var last = rawPoints[rawPoints.Count - 1];
                if (result.Count == 0 ||
                    Math.Abs(last.X - result[result.Count - 1].X) > 1e-4f ||
                    Math.Abs(last.Y - result[result.Count - 1].Y) > 1e-4f)
                {
                    result.Add(last);
                }
            }

            return result.Count >= 2 ? result : new List<SKPoint>(rawPoints);
        }

        /// <summary>
        /// 在折线中找到距 point 最近的线段，返回插入索引（线段终点位置）。
        /// </summary>
        private static int FindNearestInsertIndex(List<SKPoint> points, SKPoint point, bool isClosed)
        {
            int segCount = isClosed ? points.Count : points.Count - 1;
            float bestDist = float.MaxValue;
            int bestIdx = 1;

            for (int i = 0; i < segCount; i++)
            {
                int next = (i + 1) % points.Count;
                float d = DistanceToSegmentSquared(point, points[i], points[next]);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIdx = next;
                }
            }

            // 闭合轮廓的最后一段（末尾→首个），插入位置为 Points.Count
            if (isClosed && bestIdx == 0)
                return points.Count;

            return bestIdx;
        }

        private static List<int> BuildExtendedMovedPointIndices(int startPointIndex, int endPointIndex)
        {
            // 新复制段会插在“原 start 节点之后”，
            // 因此上层需要的不是原索引，而是复制段在新路径中的索引范围。
            int movedPointCount = endPointIndex - startPointIndex + 1;
            int movedPointStartIndex = startPointIndex + 1;

            var movedPointIndices = Enumerable
                .Range(movedPointStartIndex, movedPointCount)
                .ToList();
            return movedPointIndices;
        }

        private static List<SKPoint> BuildExtendedPolylinePoints(
            List<SKPoint> originalPoints,
            int startPointIndex,
            int endPointIndex,
            SKPoint delta)
        {
            // 折线延伸的最终结构是：
            // prefix(含原 start) + movedCopy(selected range) + suffix(从原 end 开始)。
            // 这样可以同时保留原首尾点，并在中间插入一段偏移后的复制段。
            var prefixPoints = originalPoints
                .Take(startPointIndex + 1)
                .ToList();
            var selectedPoints = originalPoints
                .Skip(startPointIndex)
                .Take(endPointIndex - startPointIndex + 1)
                .ToList();
            var movedPoints = selectedPoints
                .Select(point => point + delta)
                .ToList();
            var suffixPoints = originalPoints
                .Skip(endPointIndex)
                .ToList();

            var extendedPoints = new List<SKPoint>();
            extendedPoints.AddRange(prefixPoints);
            extendedPoints.AddRange(movedPoints);
            extendedPoints.AddRange(suffixPoints);
            return extendedPoints;
        }

        private static List<SKPoint> BuildExtendedCubicAnchors(
            List<SKPoint> originalAnchors,
            int startPointIndex,
            int endPointIndex,
            SKPoint delta)
        {
            // cubic 锚点的结构与折线一致：保留原首尾锚点，中间插入偏移后的复制锚点。
            // 真正让连接处稳定的是 handles 重建，不在 anchors 这一步处理。
            var newAnchors = new List<SKPoint>();

            var prefixAnchors = originalAnchors
                .Take(startPointIndex + 1)
                .ToList();
            var selectedAnchors = originalAnchors
                .Skip(startPointIndex)
                .Take(endPointIndex - startPointIndex + 1)
                .ToList();
            var movedAnchors = selectedAnchors
                .Select(anchor => anchor + delta)
                .ToList();
            var suffixAnchors = originalAnchors
                .Skip(endPointIndex)
                .ToList();

            newAnchors.AddRange(prefixAnchors);
            newAnchors.AddRange(movedAnchors);
            newAnchors.AddRange(suffixAnchors);
            return newAnchors;
        }

        private static List<SKPoint> BuildExtendedCubicHandles(
            List<SKPoint> originalAnchors,
            List<SKPoint> originalHandles,
            int startPointIndex,
            int endPointIndex,
            SKPoint delta)
        {
            // handles 的重建重点不是“整体平移一份”这么简单，
            // 还要把新复制段与原路径的两个接缝压成明确的连接点：
            // - 新复制段首锚点的 in-handle 压回锚点
            // - 新复制段尾锚点的 out-handle 压回锚点
            // - 原首尾连接侧也同步压回锚点
            // 否则旧句柄会把原连续曲线关系带进新接缝，出现回勾或异常拉扯。
            var newHandles = new List<SKPoint>();

            for (int index = 0; index < startPointIndex; index++)
            {
                var anchor = originalAnchors[index];
                var outHandle = GetOutHandle(originalHandles, index);
                var inHandle = GetInHandle(originalHandles, index);

                AddAnchorHandles(newHandles, anchor, outHandle, inHandle);
            }

            var startAnchor = originalAnchors[startPointIndex];
            var startOutHandle = startAnchor;
            var startInHandle = GetInHandle(originalHandles, startPointIndex);
            AddAnchorHandles(newHandles, startAnchor, startOutHandle, startInHandle);

            for (int index = startPointIndex; index <= endPointIndex; index++)
            {
                var movedAnchor = originalAnchors[index] + delta;
                var movedOutHandle = GetOutHandle(originalHandles, index) + delta;
                var movedInHandle = GetInHandle(originalHandles, index) + delta;

                bool isFirstMovedAnchor = index == startPointIndex;
                if (isFirstMovedAnchor)
                {
                    movedInHandle = movedAnchor;
                }

                bool isLastMovedAnchor = index == endPointIndex;
                if (isLastMovedAnchor)
                {
                    movedOutHandle = movedAnchor;
                }

                AddAnchorHandles(newHandles, movedAnchor, movedOutHandle, movedInHandle);
            }

            var endAnchor = originalAnchors[endPointIndex];
            var endOutHandle = GetOutHandle(originalHandles, endPointIndex);
            var endInHandle = endAnchor;
            AddAnchorHandles(newHandles, endAnchor, endOutHandle, endInHandle);

            for (int index = endPointIndex + 1; index < originalAnchors.Count; index++)
            {
                var anchor = originalAnchors[index];
                var outHandle = GetOutHandle(originalHandles, index);
                var inHandle = GetInHandle(originalHandles, index);

                AddAnchorHandles(newHandles, anchor, outHandle, inHandle);
            }

            return newHandles;
        }

        private static void AddAnchorHandles(
            List<SKPoint> handles,
            SKPoint anchor,
            SKPoint outHandle,
            SKPoint inHandle)
        {
            handles.Add(outHandle);
            handles.Add(inHandle);
        }

        private static SKPoint GetOutHandle(List<SKPoint> handles, int anchorIndex)
        {
            int handleIndex = anchorIndex * 2;
            var outHandle = handles[handleIndex];
            return outHandle;
        }

        private static SKPoint GetInHandle(List<SKPoint> handles, int anchorIndex)
        {
            int handleIndex = anchorIndex * 2 + 1;
            var inHandle = handles[handleIndex];
            return inHandle;
        }

        private bool TryMergeSeparatedPolyLines(List<(DrawObject Child, int PointIndex)> matched)
        {
            if (matched.Count != 2
                || matched[0].Child is not DrawPolyLines first
                || matched[1].Child is not DrawPolyLines second)
            {
                return false;
            }

            SKPoint keepA = matched[0].PointIndex == 0 ? first.Points[first.Points.Count - 1] : first.Points[0];
            SKPoint keepB = matched[1].PointIndex == 0 ? second.Points[second.Points.Count - 1] : second.Points[0];

            var merged = new DrawPolyLines(new List<Point2D>
            {
                new(keepA.X, keepA.Y),
                new(keepB.X, keepB.Y),
            })
            {
                Pen = first.Pen?.Clone() ?? new SKPaint(),
                Name = first.Name,
                IsVisible = first.IsVisible,
                IsPathEditing = first.IsPathEditing,
            };

            int insertIndex = Children.IndexOf(first);
            Children.Remove(first);
            Children.Remove(second);
            if (insertIndex < 0 || insertIndex > Children.Count)
                insertIndex = Children.Count;

            Children.Insert(insertIndex, merged);
            return true;
        }

        /// <summary>
        /// 将世界坐标转换为子图形 Points 空间坐标。
        /// DrawPolyLines.Points 存储真实世界坐标（Matrix.MapPoint(_localPoints[i])），直接使用；
        /// DrawCubicPath/DrawBezier 等的 Points 存储旋转前世界坐标（scaledAnchors + SharpCenter），
        /// 需要通过逆矩阵撤销旋转/缩放。
        /// </summary>
        private static SKPoint WorldToChildPointsSpace(DrawObject child, SKPoint worldPos)
        {
            if (child is DrawPolyLines)
                return worldPos;
            var local = child.GetInverseMatrix().MapPoint(worldPos);
            return new SKPoint(local.X + child.SharpCenter.X, local.Y + child.SharpCenter.Y);
        }

        private void RefreshAfterPathEdit()
        {
            _suppressChildPropagation = true;
            try
            {
                UpdateSetProperty(new List<SKPoint>());
            }
            finally
            {
                _suppressChildPropagation = false;
            }

            InvalidateBoundingBox();
        }

        private static SKPoint OffsetPointToward(SKPoint from, SKPoint to, float distance)
        {
            SKPoint direction = to - from;
            float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length <= 1e-6f)
                return from;

            return new SKPoint(
                from.X + direction.X / length * distance,
                from.Y + direction.Y / length * distance);
        }

        private static float DistanceToSegmentSquared(SKPoint point, SKPoint start, SKPoint end)
        {
            float abx = end.X - start.X;
            float aby = end.Y - start.Y;
            float apx = point.X - start.X;
            float apy = point.Y - start.Y;
            float abLengthSq = abx * abx + aby * aby;

            if (abLengthSq < 1e-10f)
                return apx * apx + apy * apy;

            float t = (apx * abx + apy * aby) / abLengthSq;
            if (t < 0f)
                t = 0f;
            else if (t > 1f)
                t = 1f;

            float projX = start.X + t * abx;
            float projY = start.Y + t * aby;
            float dx = point.X - projX;
            float dy = point.Y - projY;
            return dx * dx + dy * dy;
        }

        private static DrawCubicPath? ArcToCubicPath(SKPoint p1, SKPoint p2, SKPoint p3, SKPaint pen)
        {
            var circle = ArcMath.Circumcircle(p1, p2, p3);
            if (circle == null)
                return null;

            var (center, radius) = circle.Value;
            if (radius < 0.001f)
                return null;

            float a1 = MathF.Atan2(p1.Y - center.Y, p1.X - center.X);
            float am = MathF.Atan2(p2.Y - center.Y, p2.X - center.X);
            float a3 = MathF.Atan2(p3.Y - center.Y, p3.X - center.X);

            static float Normalize(float angle) => ((angle % (2 * MathF.PI)) + 2 * MathF.PI) % (2 * MathF.PI);
            a1 = Normalize(a1);
            am = Normalize(am);
            a3 = Normalize(a3);

            float CounterClockwiseDistance(float from, float to) => Normalize(to - from);
            bool sweepCounterClockwise = CounterClockwiseDistance(a1, am) < CounterClockwiseDistance(a1, a3);
            float totalSweep = sweepCounterClockwise
                ? CounterClockwiseDistance(a1, a3)
                : -(2 * MathF.PI - CounterClockwiseDistance(a1, a3));

            float absSweep = MathF.Abs(totalSweep);
            int segmentCount = Math.Max(1, (int)MathF.Ceiling(absSweep / (MathF.PI / 2 + 0.001f)));
            float segmentSweep = totalSweep / segmentCount;

            var anchors = new List<SKPoint>();
            var handles = new List<SKPoint>();
            for (int i = 0; i < segmentCount; i++)
            {
                float alpha = a1 + i * segmentSweep;
                float beta = alpha + segmentSweep;

                SKPoint start = new(
                    center.X + radius * MathF.Cos(alpha),
                    center.Y + radius * MathF.Sin(alpha));
                SKPoint end = new(
                    center.X + radius * MathF.Cos(beta),
                    center.Y + radius * MathF.Sin(beta));

                float handleLength = (4f / 3f) * MathF.Tan(MathF.Abs(segmentSweep) / 4f) * radius;
                float sign = segmentSweep >= 0 ? 1f : -1f;

                SKPoint outHandle = new(
                    start.X + sign * handleLength * (-MathF.Sin(alpha)),
                    start.Y + sign * handleLength * MathF.Cos(alpha));
                SKPoint inHandle = new(
                    end.X - sign * handleLength * (-MathF.Sin(beta)),
                    end.Y - sign * handleLength * MathF.Cos(beta));

                if (i == 0)
                {
                    anchors.Add(start);
                    handles.Add(outHandle);
                    handles.Add(SKPoint.Empty);
                }
                else
                {
                    handles[handles.Count - 2] = outHandle;
                }

                anchors.Add(end);
                handles.Add(SKPoint.Empty);
                handles.Add(inHandle);
            }

            var cubic = new DrawCubicPath
            {
                IsClosed = false,
                Pen = pen?.Clone() ?? new SKPaint(),
                Name = "_弧曲线"
            };
            cubic.Initialize(anchors, handles);
            return cubic;
        }

        private static DrawCubicPath? CircleToCubicPath(DrawCircle circle)
        {
            float rx = circle.DrawingRadiusX;
            float ry = circle.DrawingRadiusY;
            if (rx < 0.001f || ry < 0.001f)
                return null;

            var matrix = circle.GetTransformMatrix();
            const float k = 0.5522847498f;

            var localAnchors = new[]
            {
                new SKPoint(0, ry),
                new SKPoint(rx, 0),
                new SKPoint(0, -ry),
                new SKPoint(-rx, 0),
            };
            var localOutHandles = new[]
            {
                new SKPoint(k * rx, ry),
                new SKPoint(rx, -k * ry),
                new SKPoint(-k * rx, -ry),
                new SKPoint(-rx, k * ry),
            };
            var localInHandles = new[]
            {
                new SKPoint(-k * rx, ry),
                new SKPoint(rx, k * ry),
                new SKPoint(k * rx, -ry),
                new SKPoint(-rx, -k * ry),
            };

            var worldAnchors = localAnchors.Select(matrix.MapPoint).ToList();
            var worldHandles = new List<SKPoint>();
            for (int i = 0; i < 4; i++)
            {
                worldHandles.Add(matrix.MapPoint(localOutHandles[i]));
                worldHandles.Add(matrix.MapPoint(localInHandles[i]));
            }

            var cubic = new DrawCubicPath
            {
                IsClosed = true,
                Pen = circle.Pen?.Clone() ?? new SKPaint(),
                Name = $"{circle.Name}_曲线"
            };
            cubic.Initialize(worldAnchors, worldHandles);
            return cubic;
        }

        /// <summary>
        /// 将 DrawBezier/DrawArbitraryCurve（Catmull-Rom 样条）转为 DrawCubicPath，
        /// 以便用 de Casteljau 分割插入节点时保持曲线形状不变。
        /// </summary>
        private static DrawCubicPath? CurveChildToCubicPath(DrawObject curveChild)
        {
            // 这一步的核心目的不是“换个类型存一下”，而是把原先只隐含切线关系的曲线，
            // 显式展开成“锚点 + 每个锚点一对控制句柄”的 cubic 表达。
            // 后续凡是结构性编辑（插点、延伸连续段、重建接缝）都统一基于 DrawCubicPath 处理，
            // 避免分别维护 DrawBezier / DrawArbitraryCurve 的多套节点编辑规则。
            List<SKPoint> worldAnchors;
            bool isClosed;

            if (curveChild is DrawBezier bz)
            {
                worldAnchors = bz.GetWorldAnchorPoints();
                isClosed = bz.IsClosed;
            }
            else if (curveChild is DrawArbitraryCurve ac)
            {
                worldAnchors = ac.GetWorldAnchorPoints();
                isClosed = ac.IsClosed;
            }
            else
            {
                return null;
            }

            if (worldAnchors == null || worldAnchors.Count < 2)
                return null;

            // 2 个锚点时，本质上只是直线段。
            // 这里仍返回 DrawCubicPath，是为了让上层“结构编辑后统一替换为 cubic”这条链不断裂，
            // 不需要在调用方再分一次“2 点走 polyline，3 点以上走 cubic”。
            if (worldAnchors.Count == 2)
            {
                var lineAnchors = worldAnchors;
                var lineHandles = new List<SKPoint>
                {
                    lineAnchors[0], // out-handle of first
                    lineAnchors[0], // in-handle of first
                    lineAnchors[1], // out-handle of second
                    lineAnchors[0], // in-handle of second (direction)
                };
                var lineCubic = new DrawCubicPath
                {
                    IsClosed = false,
                    Pen = curveChild.Pen?.Clone() ?? new SKPaint(),
                    Name = $"{curveChild.Name}_曲线"
                };
                lineCubic.Initialize(lineAnchors, lineHandles);
                return lineCubic;
            }

            int N = worldAnchors.Count;
            var handles = new List<SKPoint>(N * 2);

            for (int i = 0; i < N; i++)
            {
                // Catmull-Rom 本身只有锚点，没有显式控制句柄。
                // 这里先为锚点 i 找到“前一个/后一个参考点”，
                // 再用这两个点推出该锚点的切线方向，最后折算成 cubic 的 out / in handles。
                int prevIdx, nextIdx;
                if (isClosed)
                {
                    prevIdx = (i - 1 + N) % N;
                    nextIdx = (i + 1) % N;
                }
                else
                {
                    prevIdx = i == 0 ? -1 : i - 1;
                    nextIdx = i == N - 1 ? -1 : i + 1;
                }

                // 反射端点（与 FillCatmullRomPath 中 ReflectPoint 一致）
                var prev = prevIdx < 0
                    ? new SKPoint(2 * worldAnchors[i].X - worldAnchors[nextIdx!].X,
                                  2 * worldAnchors[i].Y - worldAnchors[nextIdx!].Y)
                    : worldAnchors[prevIdx];
                var next = nextIdx < 0
                    ? new SKPoint(2 * worldAnchors[i].X - worldAnchors[prevIdx!].X,
                                  2 * worldAnchors[i].Y - worldAnchors[prevIdx!].Y)
                    : worldAnchors[nextIdx];

                // Catmull-Rom → 三次贝塞尔控制句柄公式（与 AddCatmullRomSegment 一致）：
                //   outHandle = anchor + (nextAnchor - prevAnchor) / 6
                //   inHandle  = anchor - (nextAnchor - prevAnchor) / 6
                // 这里的 1/6 是标准 Catmull-Rom 到 cubic 的缩放系数，
                // 表示“用邻点差向量来近似锚点切线，再把切线长度折算为贝塞尔句柄长度”。
                float tx = next.X - prev.X;
                float ty = next.Y - prev.Y;
                handles.Add(new SKPoint(worldAnchors[i].X + tx / 6f, worldAnchors[i].Y + ty / 6f));
                handles.Add(new SKPoint(worldAnchors[i].X - tx / 6f, worldAnchors[i].Y - ty / 6f));
            }

            var cubicPath = new DrawCubicPath
            {
                IsClosed = isClosed,
                Pen = curveChild.Pen?.Clone() ?? new SKPaint(),
                Name = $"{curveChild.Name}_曲线"
            };
            cubicPath.Initialize(worldAnchors, handles);
            return cubicPath;
        }

        private static void InsertNodeInCubicPath(DrawCubicPath cubic, SKPoint newWorldPos, bool preserveSharpCenter = false)
        {
            int anchorCount = cubic.Points.Count;
            if (anchorCount < 2 || cubic.ControlHandles == null || cubic.ControlHandles.Count != anchorCount * 2)
                return;

            int segmentCount = cubic.IsClosed ? anchorCount : anchorCount - 1;
            int bestSegment = 0;
            float bestT = 0.5f;
            float bestDistSq = float.MaxValue;

            // ── 第一遍：粗搜索，每段固定 coarseN 个采样点，快速定位最近段 ──
            // coarseN 取足够大（640），确保放大到最大时也能找到正确的段。
            const int coarseN = 640;
            for (int segment = 0; segment < segmentCount; segment++)
            {
                int startIndex = segment;
                int endIndex = (segment + 1) % anchorCount;
                var p0 = cubic.Points[startIndex];
                var p3 = cubic.Points[endIndex];
                var cp1 = cubic.ControlHandles[startIndex * 2];
                var cp2 = cubic.ControlHandles[endIndex * 2 + 1];

                for (int s = 0; s <= coarseN; s++)
                {
                    float t = s / (float)coarseN;
                    float u = 1 - t;
                    var point = new SKPoint(
                        u * u * u * p0.X + 3 * u * u * t * cp1.X + 3 * u * t * t * cp2.X + t * t * t * p3.X,
                        u * u * u * p0.Y + 3 * u * u * t * cp1.Y + 3 * u * t * t * cp2.Y + t * t * t * p3.Y);
                    float dx = newWorldPos.X - point.X, dy = newWorldPos.Y - point.Y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestSegment = segment;
                        bestT = t;
                    }
                }
            }

            // ── 第二遍：牛顿迭代精化 ──
            // 在粗搜索找到的最近段上，用牛顿法最小化 d(t) = |C(t) - P|²，
            // 迭代公式：t_new = t - f(t)/f'(t)，其中
            //   f(t)  = (C(t)-P)·C'(t)      （距离平方的一阶导数）
            //   f'(t) = |C'(t)|² + (C(t)-P)·C''(t)  （二阶导数）
            // 收敛后精度达到浮点极限，不受采样密度限制，彻底解决放大到最大时节点偏移问题。
            {
                int startIndex = bestSegment;
                int endIndex = (bestSegment + 1) % anchorCount;
                var p0 = cubic.Points[startIndex];
                var p3 = cubic.Points[endIndex];
                var cp1 = cubic.ControlHandles[startIndex * 2];
                var cp2 = cubic.ControlHandles[endIndex * 2 + 1];

                float t = bestT;
                for (int iter = 0; iter < 20; iter++)
                {
                    float u = 1 - t;
                    // C(t)
                    float cx = u * u * u * p0.X + 3 * u * u * t * cp1.X + 3 * u * t * t * cp2.X + t * t * t * p3.X;
                    float cy = u * u * u * p0.Y + 3 * u * u * t * cp1.Y + 3 * u * t * t * cp2.Y + t * t * t * p3.Y;
                    // C'(t) = 3*[(-3u²)*p0 + (3u²-6ut)*cp1 + (6ut-3t²)*cp2 + 3t²*p3]
                    float d1x = 3 * ((-3 * u * u) * p0.X + (3 * u * u - 6 * u * t) * cp1.X + (6 * u * t - 3 * t * t) * cp2.X + (3 * t * t) * p3.X);
                    float d1y = 3 * ((-3 * u * u) * p0.Y + (3 * u * u - 6 * u * t) * cp1.Y + (6 * u * t - 3 * t * t) * cp2.Y + (3 * t * t) * p3.Y);
                    // C''(t) = 6*[(3u-1)*p0 + (-6u+3t+1)*cp1 ... 简化为对 C'(t) 再求导]
                    // C''(t) = 6*[(1-t)*(-p0+2*cp1-cp2+...) ...]  —— 直接展开：
                    float d2x = 6 * ((p0.X - 2 * cp1.X + cp2.X) * u + (-cp1.X + 2 * cp2.X - p3.X) * t);
                    float d2y = 6 * ((p0.Y - 2 * cp1.Y + cp2.Y) * u + (-cp1.Y + 2 * cp2.Y - p3.Y) * t);

                    float ex = cx - newWorldPos.X, ey = cy - newWorldPos.Y;
                    float fVal = ex * d1x + ey * d1y;                          // f(t)
                    float fDer = d1x * d1x + d1y * d1y + ex * d2x + ey * d2y; // f'(t)

                    if (Math.Abs(fDer) < 1e-12f) break;
                    float step = fVal / fDer;
                    t -= step;
                    t = Math.Clamp(t, 0f, 1f);
                    if (Math.Abs(step) < 1e-7f) break; // 已收敛
                }

                // 验证迭代结果是否优于粗搜索结果
                {
                    float u = 1 - t;
                    float cx = u * u * u * p0.X + 3 * u * u * t * cp1.X + 3 * u * t * t * cp2.X + t * t * t * p3.X;
                    float cy = u * u * u * p0.Y + 3 * u * u * t * cp1.Y + 3 * u * t * t * cp2.Y + t * t * t * p3.Y;
                    float dx = newWorldPos.X - cx, dy = newWorldPos.Y - cy;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestT = t;
                    }
                }
            }

            int previousAnchorIndex = bestSegment;
            int nextAnchorIndex = (bestSegment + 1) % anchorCount;
            var a = cubic.Points[previousAnchorIndex];
            var d = cubic.Points[nextAnchorIndex];
            var b = cubic.ControlHandles[previousAnchorIndex * 2];
            var c = cubic.ControlHandles[nextAnchorIndex * 2 + 1];

            float invT = 1 - bestT;
            var e = new SKPoint(invT * a.X + bestT * b.X, invT * a.Y + bestT * b.Y);
            var f = new SKPoint(invT * b.X + bestT * c.X, invT * b.Y + bestT * c.Y);
            var g = new SKPoint(invT * c.X + bestT * d.X, invT * c.Y + bestT * d.Y);
            var h = new SKPoint(invT * e.X + bestT * f.X, invT * e.Y + bestT * f.Y);
            var j = new SKPoint(invT * f.X + bestT * g.X, invT * f.Y + bestT * g.Y);
            var k = new SKPoint(invT * h.X + bestT * j.X, invT * h.Y + bestT * j.Y);

            int insertIndex = nextAnchorIndex == 0 ? anchorCount : nextAnchorIndex;
            var newAnchors = new List<SKPoint>(cubic.Points);
            newAnchors.Insert(insertIndex, k);

            int newAnchorCount = newAnchors.Count;
            var newHandles = new SKPoint[newAnchorCount * 2];
            for (int i = 0; i < insertIndex; i++)
            {
                newHandles[i * 2] = cubic.ControlHandles[i * 2];
                newHandles[i * 2 + 1] = cubic.ControlHandles[i * 2 + 1];
            }
            for (int i = insertIndex + 1; i < newAnchorCount; i++)
            {
                newHandles[i * 2] = cubic.ControlHandles[(i - 1) * 2];
                newHandles[i * 2 + 1] = cubic.ControlHandles[(i - 1) * 2 + 1];
            }

            int prevAnchor = insertIndex - 1;
            int nextAnchor = (insertIndex + 1) % newAnchorCount;
            newHandles[prevAnchor * 2] = e;
            newHandles[insertIndex * 2] = j;
            newHandles[insertIndex * 2 + 1] = h;
            newHandles[nextAnchor * 2 + 1] = g;

            if (preserveSharpCenter)
                cubic.InitializePreserveSharpCenter(newAnchors, newHandles.ToList());
            else
                cubic.Initialize(newAnchors, newHandles.ToList());
        }
        #endregion

        #region Preview Commit And Dimension

        public override IEnumerable<IShape> Flatten()
        {
            return Children.SelectMany(c => c.Flatten()).ToList();
            //return [this];
        }

        internal override void CommitPreviewBounds()
        {
            CommitScaledBounds(
                Width,
                Height,
                SharpCenter,
                PreviewWidth,
                PreviewHeight,
                PreviewSharpCenter);
        }

        internal void CommitScaledBounds(
            float oldWidth,
            float oldHeight,
            SKPoint oldCenter,
            float newWidth,
            float newHeight,
            SKPoint newCenter)
        {
            float scaleX = oldWidth > 0.001f ? newWidth / oldWidth : 1f;
            float scaleY = oldHeight > 0.001f ? newHeight / oldHeight : 1f;

            _suppressChildPropagation = true;
            try
            {
                var ownerTransform = GetTransformMatrix();

                foreach (var child in Children.OfType<DrawObject>())
                {
                    BatchTransformHelper.CommitChildResize(child, oldCenter, newCenter, scaleX, scaleY, ownerTransform);
                }
            }
            finally
            {
                _suppressChildPropagation = false;
            }

            // 提交后按子图形真实几何重新回算组合边界，避免多选缩放结束后
            // 组合仍停留在预览框尺寸，导致后续合并外框偶发包不住全部子图形。
            UpdateSetProperty(new List<SKPoint>());
        }

        /// <summary>
        /// 旋转容器设置尺寸时，必须在容器局部坐标系内缩放子图形，
        /// 而非沿世界轴缩放（基类 ApplyScaling 是世界轴缩放，旋转后会导致选择框偏移）。
        /// </summary>
        internal override bool TryApplyDimension(float targetWidth, float targetHeight)
        {
            GetDrawableChildren().ToList().ForEach(child =>
            {
                child.TryApplyDimension(targetWidth, targetHeight);
            });

            return true;
        }

        #region Bounds

        private IEnumerable<DrawObject> GetDrawableChildren()
        {
            return Children?.OfType<DrawObject>() ?? Enumerable.Empty<DrawObject>();
        }

        private SKRect GetChildrenAabbBounds()
        {
            if (_cachedBoundingBox.HasValue && !_bboxDirty)
            {
                return _cachedBoundingBox.Value;
            }

            var bounds = ComputeChildrenAabbBounds();
            _cachedBoundingBox = bounds;
            _bboxDirty = false;
            return bounds;
        }
        private (SKPoint[] Corners, SKPoint Center) GetChildrenObbBounds()
        {
            if (Children.Count == 1 && Children[0] is DrawObject child)
            {
                return child.GetOBB();
            }

            return GetChildrenAabbBounds().CreateBoundsGeometry();
        }
        private SKRect GetChildrenPreviewAabbBounds()
        {
            return GetDrawableChildren().GetUnionPreviewAABB();
        }
        private (SKPoint[] Corners, SKPoint Center) GetChildrenPreviewObbBounds()
        {
            return GetDrawableChildren().GetUnionPreviewOBB();
        }
        
        public override (SKPoint[] Corners, SKPoint Center) GetPreviewAABB()
        {
            if (UseFastBounds)
            {
                return GetChildrenAabbBounds().CreateBoundsGeometry();
            }

            return GetChildrenPreviewAabbBounds().CreateBoundsGeometry();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewOBB()
        {
            if (UseFastBounds)
            {
                return GetPreviewAABB();
            }

            SKPoint center;
            var corners = TotalPreviewMatrix.MapPoints(LocalCorners);

            center = new SKPoint(
                (corners[0].X + corners[2].X) / 2,
                (corners[0].Y + corners[2].Y) / 2
            );

            return (corners, center);
        }

        public override SKRect GetAABB()
        {
            return GetChildrenAabbBounds();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetAABB2()
        {
            return GetChildrenAabbBounds().CreateBoundsGeometry();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetOBB()
        {
            if (UseFastBounds)
            {
                return GetChildrenAabbBounds().CreateBoundsGeometry();
            }

            return GetChildrenObbBounds();
        }

        private void InitializeBoundsFromChildren(IReadOnlyList<IShape> children)
        {
            var bounds = ComputeChildrenAabbBounds(children, out bool hasPendingPreviewDelta);
            _cachedBoundingBox = bounds;
            _bboxDirty = false;
            base.SetRotationCenter(bounds.Center());
            LocalCorners = ShouldUseFastBounds(children.Count)
                ? bounds.ToCorners()
                : hasPendingPreviewDelta
                ? GetChildrenPreviewObbBounds().Corners
                : bounds.ToCorners();
        }

        private SKRect ComputeChildrenAabbBounds()
        {
            return ComputeChildrenAabbBounds(Children, out _);
        }

        private static SKRect ComputeChildrenAabbBounds(
            IReadOnlyList<IShape> children,
            out bool hasPendingPreviewDelta)
        {
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            bool hasData = false;
            hasPendingPreviewDelta = false;

            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child == null)
                {
                    continue;
                }

                if (child is DrawObject drawChild)
                {
                    hasPendingPreviewDelta |= !IsIdentityMatrix(drawChild.DeltaMatrix);
                    var bounds = drawChild.GetAABB();
                    if (bounds.IsEmpty)
                    {
                        continue;
                    }

                    if (bounds.Left < minX) minX = bounds.Left;
                    if (bounds.Top < minY) minY = bounds.Top;
                    if (bounds.Right > maxX) maxX = bounds.Right;
                    if (bounds.Bottom > maxY) maxY = bounds.Bottom;
                    hasData = true;
                    continue;
                }

                var corners = child.GetAABB2().Corners;
                if (corners == null || corners.Length == 0)
                {
                    continue;
                }

                for (int cornerIndex = 0; cornerIndex < corners.Length; cornerIndex++)
                {
                    var point = corners[cornerIndex];
                    if (point.X < minX) minX = point.X;
                    if (point.Y < minY) minY = point.Y;
                    if (point.X > maxX) maxX = point.X;
                    if (point.Y > maxY) maxY = point.Y;
                }

                hasData = true;
            }

            return hasData ? new SKRect(minX, minY, maxX, maxY) : SKRect.Empty;
        }

        private static bool IsIdentityMatrix(SKMatrix matrix)
        {
            return matrix.ScaleX == 1f &&
                   matrix.ScaleY == 1f &&
                   matrix.SkewX == 0f &&
                   matrix.SkewY == 0f &&
                   matrix.TransX == 0f &&
                   matrix.TransY == 0f &&
                   matrix.Persp0 == 0f &&
                   matrix.Persp1 == 0f &&
                   matrix.Persp2 == 1f;
        }

        private bool UseFastBounds => ShouldUseFastBounds(Children.Count);

        private static bool ShouldUseFastBounds(int childCount) =>
            childCount >= FastBoundsChildCountThreshold;
        #endregion

        #region Transforms
        public override void ApplyMirror(bool isHorizontal, SKPoint anchor, bool commit = true)
        {
            var scaleX = isHorizontal ? -1 : 1;
            var scaleY = isHorizontal ? 1 : -1;

            foreach (var child in GetDrawableChildren())
            {
                child.Scale(scaleX, scaleY, anchor, commit: commit);
            }
            base.Scale(scaleX, scaleY, anchor, commit: commit);
        }

        protected override void OnCommittedMatrixChanged()
        {
            RefreshLocalCorners();
            InvalidateChildCaches();
            base.OnCommittedMatrixChanged();
        }

        public override void Translate(float dx, float dy, bool commit = true)
        {
            foreach (var child in GetDrawableChildren())
            {
                child.Translate(dx, dy, commit);
            }

            base.Translate(dx, dy, commit);
        }

        public override void Scale(float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = false)
        {
            // 统一世界坐标：组合与所有子图形应用同一个世界 delta（同 anchor、同 directionRad），
            // 拉右控制点时子图形整体沿组合 OBB 的 X 方向拉伸，而不是各自在宽度方向拉伸。
            foreach (var child in GetDrawableChildren())
            {
                child.Scale(scaleX, scaleY, anchor, directionRad, commit);
            }

            base.Scale(scaleX, scaleY, anchor, directionRad, commit);
        }

        public override void Rotate(float deltaAngle, SKPoint center, bool commit = false)
        {
            foreach (var child in GetDrawableChildren())
            {
                child?.Rotate(deltaAngle, center, commit);
            }

            base.Rotate(deltaAngle, center, commit);
        }

        public override void Skew(float tanSkewX, float tanSkewY, SKPoint anchor, bool commit = false)
        {
            foreach (var child in GetDrawableChildren())
            {
                child?.Skew(tanSkewX, tanSkewY, anchor, commit);
            }

            base.Skew(tanSkewX, tanSkewY, anchor, commit);
        }

        protected override SKRect ComputeCommittedAabbBounds()
        {
            return GetDrawableChildren().GetUnionAABB();
        }
        #endregion

        /// <summary>
        /// 递归子级总数，O(1) 懒缓存。
        /// </summary>
        public override int FlattenCount => Children.FlattenCount;
        #endregion

        #region Hatch Fill
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

        public HatchPatternObjects CreateHatchPattern()
        {
            if (HatchParamInfo == null) return new HatchPatternObjects();

            // 1. 获取基础数据（Extension / ReverseFillLine 已在 GetFillLines 内部处理）
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
        /// 组合图形通过提取所有子图形的路径轮廓来生成填充数据。
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo.LineSpacing <= 0)
                return result;

            // 组合图形没有自身的 _localPoints，通过子图形路径获取轮廓
            if (Children == null || Children.Count == 0)
                return result;

            return hatchInfo.FillTypeIndex switch
            {
                0 => GetScanlineFillLines(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                1 => GetScanlineFillLines(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                2 => GetConcentricFillLines(hatchInfo),   // 回字形
                3 => GetSpiralFillLines(hatchInfo),
                _ => new List<(SKPoint, SKPoint)>(),      // 其他
            };
        }
        /// <summary>
        /// 获取填充线段。将子图形路径离散为多轮廓多边形后，使用多轮廓扫描线算法生成填充线。
        /// 返回的线段在**局部坐标系**中（相对于 SharpCenter）。
        /// 对于不相交图形：各子图形独立形成闭合轮廓，odd-even 规则自然独立填充；
        /// 对于相交图形：所有轮廓边统一参与扫描线交点计算，重叠区域按 odd-even 规则处理。
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetScanlineFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();

            // 提取所有子图形在组合局部坐标系中的多轮廓多边形
            //var contours = GetChildContoursInLocalCoords();
            var contours = GetChildContoursInWorldCoords();
            if (contours.Count == 0)
                return result;

            // 使用多轮廓扫描线填充算法（odd-even 规则）
            // 不相交图形：各轮廓独立填充
            // 相交图形：重叠区域按 odd-even 规则自动处理
            result.AddRange(GenerateMultiContourScanlineFill(contours, hatchInfo));
            return result;
        }

        private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }

        private List<(SKPoint Start, SKPoint End)> GetSpiralFillLines(HatchParamDto info)
        {
            return new List<(SKPoint Start, SKPoint End)>();
        }
        /// <summary>
        /// 从子图形路径中提取所有闭合轮廓的离散点，坐标在世界坐标系中。
        /// 每个轮廓是一个闭合的 SKPoint 数组，用于扫描线填充。
        /// </summary>
        private List<SKPoint[]> GetChildContoursInWorldCoords()
        {
            var contours = new List<SKPoint[]>();

            foreach (var child in Children.OfType<DrawObject>())
            {
                using var childPath = child.GetPath();
                if (childPath == null || childPath.IsEmpty) continue;

                // 获取子图形的变换矩阵（局部→世界）
                var childLocalToWorld = child.GetTransformMatrix();

                using var transformed = new SKPath(childPath);
                transformed.Transform(childLocalToWorld);

                // 将变换后的路径按轮廓离散为多边形点集
                var childContours = FlattenSKPathToContours(transformed);
                contours.AddRange(childContours);
            }

            return contours;
        }

        /// <summary>
        /// 将 SKPath 按子路径（contour）离散为多边形点集列表。
        /// 每个 contour 对应一个闭合或非闭合子路径，使用 SKPathMeasure 按分辨率采样。
        /// </summary>
        private static List<SKPoint[]> FlattenSKPathToContours(SKPath path)
        {
            var contours = new List<SKPoint[]>();
            float stepMm = (float)DrSoft.Drawing.Utility.GlobalVariableManagement.Resolution;
            if (stepMm <= 0) stepMm = 0.01f;

            using var measure = new SKPathMeasure(path, forceClosed: false);
            do
            {
                float length = measure.Length;
                if (length < stepMm * 2) continue; // 退化路径跳过

                var points = new List<SKPoint>();
                for (float distance = 0; distance < length; distance += stepMm)
                {
                    if (measure.GetPosition(distance, out var point))
                    {
                        points.Add(point);
                    }
                }
                // 确保终点被加入
                if (measure.GetPosition(length, out var lastPoint))
                {
                    points.Add(lastPoint);
                }

                if (points.Count >= 3)
                {
                    contours.Add(points.ToArray());
                }
            } while (measure.NextContour());

            return contours;
        }

        /// <summary>
        /// 多轮廓扫描线填充算法（使用 odd-even 规则）。
        /// 将所有轮廓的边统一参与扫描线交点计算，使用 odd-even 配对得到填充段。
        /// 对于不相交图形，各轮廓独立产生填充段；
        /// 对于相交图形，重叠区域按 odd-even 规则自动镲空。
        /// 同时对每条扫描线执行"到边距离≥margin"的约束裁剪。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GenerateMultiContourScanlineFill(
            List<SKPoint[]> contours, HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo.LineSpacing <= 0 || contours.Count == 0)
                return result;

            float margin = (float)hatchInfo.Margin;
            float extension = (float)hatchInfo.Extension;
            bool reverseAll = hatchInfo.ReverseFillLine;
            bool relativeToAngle = hatchInfo.RelativeToAngle;

            // 旋转所有轮廓使填充方向水平
            //double rad = -(relativeToAngle ? hatchInfo.StartAngle : hatchInfo.StartAngle + Rotation) * Math.PI / 180.0;
            double rad = -(relativeToAngle ? hatchInfo.StartAngle + 2 * Rotation : hatchInfo.StartAngle) * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);

            // 旋转所有轮廓点并收集全局 Y 范围
            var rotatedContours = new List<SKPoint[]>(contours.Count);
            int totalEdges = 0;
            float globalMinY = float.MaxValue, globalMaxY = float.MinValue;

            for (int c = 0; c < contours.Count; c++)
            {
                var polygon = contours[c];
                int n = polygon.Length;
                var rotated = new SKPoint[n];
                for (int i = 0; i < n; i++)
                {
                    float rx = (float)(polygon[i].X * cos - polygon[i].Y * sin);
                    float ry = (float)(polygon[i].X * sin + polygon[i].Y * cos);
                    rotated[i] = new SKPoint(rx, ry);
                    if (ry < globalMinY) globalMinY = ry;
                    if (ry > globalMaxY) globalMaxY = ry;
                }
                rotatedContours.Add(rotated);
                totalEdges += n;
            }

            if (globalMaxY <= globalMinY)
                return result;

            // AverageDistribute：将 LineSpacing 作为目标值，重算间距使扫描线均等分布
            float spacing = (float)hatchInfo.LineSpacing;
            float startOffset = spacing / 2f;
            float yLimit = globalMaxY;
            if (hatchInfo.AverageDistribute && globalMaxY > globalMinY)
            {
                float span = globalMaxY - globalMinY;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startOffset = spacing;
                yLimit = globalMaxY - spacing * 0.5f;
            }

            double cosBack = Math.Cos(-rad), sinBack = Math.Sin(-rad);
            var xs = new List<float>(16);
            var forbidden = new List<(float Start, float End)>(totalEdges);

            for (float y = globalMinY + startOffset; y < yLimit; y += spacing)
            {
                // 1) 遍历所有轮廓的边，求扫描线与边的交点（odd-even 配对得到填充段）
                xs.Clear();
                for (int c = 0; c < rotatedContours.Count; c++)
                {
                    var rotated = rotatedContours[c];
                    int n = rotated.Length;
                    for (int i = 0; i < n; i++)
                    {
                        var p1 = rotated[i];
                        var p2 = rotated[(i + 1) % n];
                        if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                        {
                            float t = (y - p1.Y) / (p2.Y - p1.Y);
                            xs.Add(p1.X + t * (p2.X - p1.X));
                        }
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                // 2) 求扫描线与所有轮廓边 margin-胶囊的 x 区间并集（禁区）
                forbidden.Clear();
                if (margin > 0)
                {
                    for (int c = 0; c < rotatedContours.Count; c++)
                    {
                        var rotated = rotatedContours[c];
                        int n = rotated.Length;
                        for (int i = 0; i < n; i++)
                        {
                            var p1 = rotated[i];
                            var p2 = rotated[(i + 1) % n];
                            if (TrySegmentCapsuleXRange(p1.X, p1.Y, p2.X, p2.Y, y, margin, out float fMin, out float fMax))
                            {
                                forbidden.Add((fMin, fMax));
                            }
                        }
                    }
                    // 按起点排序并合并重叠区间
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

                // 3) 从每个实心填充段中减去禁区，输出剩余的子区间
                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float segStart = xs[i];
                    float segEnd = xs[i + 1];
                    if (segEnd <= segStart) continue;

                    float cur = segStart;
                    for (int k = 0; k < forbidden.Count; k++)
                    {
                        var (fs, fe) = forbidden[k];
                        if (fe <= cur) continue;
                        if (fs >= segEnd) break;
                        if (fs > cur)
                        {
                            AddRotatedLine(result, cur, fs, y, cosBack, sinBack, extension, reverseAll);
                        }
                        if (fe > cur) cur = fe;
                        if (cur >= segEnd) break;
                    }
                    if (cur < segEnd)
                    {
                        AddRotatedLine(result, cur, segEnd, y, cosBack, sinBack, extension, reverseAll);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 将旋转坐标系下的线段 (x1..x2, y) 反旋转回局部坐标系并追加到结果中。
        /// 同时在旋转系内应用 Extension 延伸（沿 x 轴两端各延长 extension，负值收缩，<=0 丢弃）与 ReverseFillLine 全局反向。
        /// </summary>
        private void AddRotatedLine(List<(SKPoint Start, SKPoint End)> result,
                                    float x1, float x2, float y,
                                    double cosBack, double sinBack,
                                    float extension, bool reverseLine)
        {
            if (x2 <= x1) return;
            if (extension != 0f)
            {
                x1 -= extension;
                x2 += extension;
                if (x2 <= x1) return;
            }
            var s = new SKPoint(
                (float)(x1 * cosBack - y * sinBack),
                (float)(x1 * sinBack + y * cosBack));
            var e = new SKPoint(
                (float)(x2 * cosBack - y * sinBack),
                (float)(x2 * sinBack + y * cosBack));
            if (reverseLine)
                result.Add((e, s));
            else
                result.Add((s, e));
        }

        /// <summary>
        /// 计算水平扫描线 y = y 与线段 P1P2 的 margin-胶囊（capsule）的交集 x 区间。
        /// 胶囊即线段与半径为 margin 的圆盘的 Minkowski 和，是凸集，因此与任意直线的
        /// 交集必为单个区间。返回 true 表示有交，并输出 [xMin, xMax]；返回 false 表示不相交。
        /// </summary>
        private bool TrySegmentCapsuleXRange(float p1x, float p1y, float p2x, float p2y,
                                             float y, float margin,
                                             out float xMin, out float xMax)
        {
            xMin = float.MaxValue;
            xMax = float.MinValue;
            bool any = false;

            float dx = p2x - p1x;
            float dy = p2y - p1y;
            float L2 = dx * dx + dy * dy;

            // 退化：线段就是单点
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

            // 端点 P1 处的半圆帽与扫描线交集
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

            // 端点 P2 处的半圆帽与扫描线交集
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

            // 线段中间部分的垂直条带（沿边法线的 margin 侧向威延，并受限于垂直投影参数 t∈[0,1]）
            // 单位法线 (nx, ny) = (-dy/L, dx/L)
            // 垂直有符号距离约束：|(x-p1x)*nx + (y-p1y)*ny| ≤ margin
            // 投影参数约束：t = ((x-p1x)*dx + (y-p1y)*dy) / L² ∈ [0, 1]
            //                 ⇔ (x-p1x)*dx ∈ [-(y-p1y)*dy, L² - (y-p1y)*dy]
            {
                float nx = -dy / L;
                float B = (y - p1y) * (dx / L);

                float stripMin, stripMax;
                bool stripActive = true;
                if (Math.Abs(nx) < 1e-9f)
                {
                    // 近水平边：条带条件简化为 |y-p1y| ≤ margin
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
                        // 近竖直边：t∈[0,1] 对 x 无约束，但需 y 在 [p1y, p2y] 之间
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
        #endregion

        #region Snapshot

        /// <summary>
        /// 捕获组合及其所有子图形的完整快照，支持撤销/重做路径节点编辑等操作。
        /// 基类 DrawObjectMemento 仅捕获 combo 自身的 Points + 变换属性，
        /// 不捕获子图形状态。此覆盖额外捕获每个子图形的 memento 和子图形列表，
        /// 确保 Undo 时能完整恢复 combo 及其子图形的原始状态。
        /// </summary>
        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawCombinationMemento(this);
        }

        protected class DrawCombinationMemento : DrawObjectMemento
        {
            private readonly List<(DrawObject Child, IShapeMemento Memento)> _childSnapshots;

            public DrawCombinationMemento(DrawCombination combo) : base(combo)
            {
                _childSnapshots = new List<(DrawObject, IShapeMemento)>(combo.Children.Count);
                foreach (var child in combo.Children)
                {
                    if (child is DrawObject drawChild)
                        _childSnapshots.Add((drawChild, drawChild.CaptureSnapshot()));
                }
            }

            /// <summary>
            /// 重写几何恢复：直接赋值 Points，不走 UpdateSetProperty，
            /// 避免从子图形边界框重算 combo 的 SharpCenter。
            /// </summary>
            protected override void RestoreGeometry()
            {
                if (Shape is DrawCombination combo)
                {
                    combo._suppressChildPropagation = true;
                    try
                    {
                        combo.Children.Clear();
                        foreach (var (child, _) in _childSnapshots)
                            combo.Children.Add(child);

                        foreach (var (_, memento) in _childSnapshots)
                            memento.Restore();
                    }
                    finally
                    {
                        combo._suppressChildPropagation = false;
                    }
                }

                if (_points != null && _points.Count > 0)
                    Shape.Points = new List<SKPoint>(_points);
                else
                    Shape.Points = _points != null ? new List<SKPoint>() : null;
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawCombination combo)
                {
                    combo._cachedBoundingBox = null;
                    combo.NotifyBoundingBoxInvalidated();
                }
            }
        }
        #endregion
    }
}
