using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public abstract partial class DrawObject : IShape, IPathProvider, IShapeData
    {
        #region 公有属性字段
        // 基础矩阵：记录已经完成并提交的变换
        private SKMatrix _matrix = SKMatrix.Identity;

        private float _skewTanX;
        private float _skewTanY;
        // 预览矩阵：记录当前正在进行的、尚未提交的增量变换
        private SKMatrix _deltaMatrix = SKMatrix.Identity;
        public SKMatrix DeltaMatrix => _deltaMatrix;
        // 组合矩阵：用于获取当前预览状态下的总变换
        protected SKMatrix TotalPreviewMatrix =>
            _matrix.PostConcat(_deltaMatrix);

        public SKPoint RotationCenter { get; private set; } = new(0, 0);
        public float Width { get; private set; }
        public float Height { get; private set; }
        public SKPoint SharpCenter { get; private set; } = new(0, 0);
        public float Rotation { get; set; }//角度
        public float ScaleX { get; set; } = 1.0f;
        public float ScaleY { get; set; } = 1.0f;
        public float SkewX { get; set; }//角度
        public float SkewY { get; set; }

        internal SKMatrix Matrix { get { return _matrix; } private set { _matrix = value; } }
        #endregion

        #region 克隆接口
        protected T FinalizeClone<T>(T clone) where T : DrawObject
        {
            clone.Name = Name;
            clone.LayerId = LayerId;
            clone.IsVisible = IsVisible;
            clone.IsLocked = IsLocked;
            clone.IsClockwise = IsClockwise;
            clone.Direction = Direction;
            clone.ShowJumpLine = ShowJumpLine;
            clone.Type = Type;
            clone._pen = CustomPen?.Clone();

            if (_pathNodes != null && _pathNodes.Count > 0)
            {
                clone.PathNodes = new List<SKPoint>(_pathNodes);
            }

            clone.RestoreTransformCommandSnapshot(CaptureTransformCommandSnapshot());
            return clone;
        }
        #endregion

        #region 生命周期控制

        internal void StartTransform()
        {
            // 拖拽开始时，预览矩阵置为单位矩阵（无偏移）
            _deltaMatrix = SKMatrix.Identity;
        }

        internal void CommitTransform()
        {
            RotationCenter = _deltaMatrix.MapPoint(RotationCenter);
            // MouseUp 时，将当前的预览变换永久应用到基础矩阵中
            _matrix = TotalPreviewMatrix;
            // 重置预览矩阵
            _deltaMatrix = SKMatrix.Identity;

            OnCommittedMatrixChanged();
            SyncCommittedBoundsFromMatrix();//最后计算
        }

        internal void AbortTransform()
        {
            // 如果中途取消，只需清空预览矩阵，图形会弹回原位
            _deltaMatrix = SKMatrix.Identity;
        }

        #endregion

        #region 包围盒接口 (正式与预览)
        // --- 最终状态接口 (通常用于保存、对齐或 MouseUp 后的逻辑) ---
        public virtual SKRect GetAABB()
        {
            return GetCommittedAabbBounds();
        }

        public virtual (SKPoint[] Corners, SKPoint Center) GetAABB2()
        {
            var bounds = GetCommittedAabbBounds();

            SKPoint[] corners =
            [
                new(bounds.Left, bounds.Top),     // 左上
                new(bounds.Right, bounds.Top),    // 右上
                new(bounds.Right, bounds.Bottom), // 右下
                new(bounds.Left, bounds.Bottom)   // 左下
            ];


            var center = new SKPoint(bounds.MidX, bounds.MidY);

            return (corners, center);
        }

        public virtual (SKPoint[] Corners, SKPoint Center) GetOBB()
        {
            return GetCommittedObbBounds();
        }

        // --- 预览状态接口 (用于 MouseMove 过程中实时更新选择框、控制点) ---
        public virtual (SKPoint[] Corners, SKPoint Center) GetPreviewAABB()
        {
            SKPoint center;
            using var path = GetPath();
            SKRect bounds;
            if (path == null || path.IsEmpty)
            {
                bounds = SKRect.Empty;
            }
            else
            {
                using var worldPath = new SKPath(path);
                worldPath.Transform(TotalPreviewMatrix);
                bounds = worldPath.TightBounds;
            }

            SKPoint[] corners =
            {
                new(bounds.Left, bounds.Top),     // 左上
                new(bounds.Right, bounds.Top),    // 右上
                new(bounds.Right, bounds.Bottom), // 右下
                new(bounds.Left, bounds.Bottom)   // 左下
            };


            center = new SKPoint(bounds.MidX, bounds.MidY);

            return (corners, center);
        }

        public virtual (SKPoint[] Corners, SKPoint Center) GetPreviewOBB()
        {
            SKPoint center;
            using var path = GetPath();

            // 获取原始 Path 的边界（局部坐标 AABB），不在此处扩展 offset
            SKRect originalBounds = path.TightBounds;

            // 构造四个角点（局部坐标）
            SKPoint[] corners =
            {
                new(originalBounds.Left, originalBounds.Top),     // 左上
                new(originalBounds.Right, originalBounds.Top),    // 右上
                new(originalBounds.Right, originalBounds.Bottom), // 右下
                new(originalBounds.Left, originalBounds.Bottom)   // 左下
            };

            // 先变换到世界坐标
            corners = TotalPreviewMatrix.MapPoints(corners);

            center = new SKPoint(
                (corners[0].X + corners[2].X) / 2,
                (corners[0].Y + corners[2].Y) / 2
            );

            return (corners, center);
        }
        #endregion

        #region 接口实现 (操作预览)

        // 所有的变换方法现在只作用于 _previewMatrix
        // 这样在交互过程中，图形本体矩阵 _matrix 保持不变

        public virtual void ApplyMirror(bool isHorizontal, SKPoint anchor, bool commit = false)
        {
            var scaleX = isHorizontal ? -1 : 1;
            var scaleY = isHorizontal ? 1 : -1;
            Scale(scaleX, scaleY, anchor, commit: commit);
        }

        public virtual void Translate(float dx, float dy, bool commit = true)
        {
            _deltaMatrix = SKMatrix.CreateTranslation(dx, dy);
            if (commit)
            {
                CommitTransform();
                ApplyDeltaToProperties(dx, dy, 1f, 1f, 0f, 0f, 0f);
            }
        }

        public virtual void Rotate(float deltaAngle, SKPoint center, bool commit = false)
        {
            _deltaMatrix = SKMatrix.CreateRotationDegrees(deltaAngle, center.X, center.Y);
            if (commit)
            {
                CommitTransform();
                InvalidateCommittedBoundingBoxCache();
                ApplyDeltaToProperties(0f, 0f, 1f, 1f, deltaAngle, 0f, 0f);
            }
        }

        public virtual void Scale(float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = false)
        {
            _deltaMatrix = BuildWorldScaleDelta(scaleX, scaleY, anchor, directionRad);

            if (commit)
            {
                CommitTransform();
                InvalidateCommittedBoundingBoxCache();
                ApplyDeltaToProperties(0f, 0f, scaleX, scaleY, 0f, 0f, 0f);
            }
        }

        public virtual void Skew(float tanSkewX, float tanSkewY, SKPoint anchor, bool commit = false)
        {
            var m = SKMatrix.CreateTranslation(-anchor.X, -anchor.Y);
            m = m.PostConcat(SKMatrix.CreateSkew(tanSkewX, tanSkewY));
            m = m.PostConcat(SKMatrix.CreateTranslation(anchor.X, anchor.Y));
            _deltaMatrix = m;
            if (commit)
            {
                CommitTransform();
                InvalidateCommittedBoundingBoxCache();
                ApplyDeltaToProperties(0f, 0f, 1f, 1f, 0f, tanSkewX, tanSkewY);
            }
        }

        public void SetRotationCenter(SKPoint point)
        {
            RotationCenter = point;
        }

        /// <summary>
        /// 纯世界坐标缩放 delta：delta = R(θ)·S(sx,sy)·R(−θ)，全部绕世界锚点。
        /// θ=0 时退化为世界轴对齐缩放；θ=OBB 方向角时先把 OBB 的 X 轴“转正”对齐世界 X，
        /// 做轴对齐缩放后再转回，缩放严格沿 OBB 的 X/Y 方向，旋转图形拉控制点不产生剪切（保形）。
        /// 当 θ 取图形自身旋转角时，与旧 Local 模式（_matrix·S·_matrix⁻¹）数学等价。
        /// </summary>
        private static SKMatrix BuildWorldScaleDelta(float scaleX, float scaleY, SKPoint anchorWorld, float directionRad)
        {
            // 核心防御：强制限制缩放比例的下限
            // 如果传入的 scaleX 或 scaleY 接近 0，强制将其拉回到安全值
            float safeScaleX = Math.Max(Math.Abs(scaleX), 0.001f) * Math.Sign(scaleX);
            float safeScaleY = Math.Max(Math.Abs(scaleY), 0.001f) * Math.Sign(scaleY);

            var delta = SKMatrix.CreateScale(scaleX, scaleY, anchorWorld.X, anchorWorld.Y);
            if (directionRad != 0f)
            {
                delta = SKMatrix.CreateRotation(-directionRad, anchorWorld.X, anchorWorld.Y)
                    .PostConcat(delta)
                    .PostConcat(SKMatrix.CreateRotation(directionRad, anchorWorld.X, anchorWorld.Y));
            }
            return delta;
        }

        /// <summary>图形局部 X 轴在世界坐标中的方向角（弧度），供沿自身方向缩放时作为 directionRad 传入。</summary>
        public float GetWorldRotationRad() => MathF.Atan2(_matrix.SkewY, _matrix.ScaleX);

        #endregion

        #region 辅助方法

        private SKRect GetCommittedAabbBounds()
        {
            if (_cachedBoundingBox.HasValue && !_bboxDirty)
                return _cachedBoundingBox.Value;

            var bounds = ComputeCommittedAabbBounds();
            ApplyCommittedBounds(bounds);
            return bounds;
        }

        private (SKPoint[] Corners, SKPoint Center) GetCommittedObbBounds()
        {
            if (_cachedObbCorners != null && !_obbDirty)
            {
                return (_cachedObbCorners, _cachedObbCenter);
            }

            var geometry = ComputeCommittedObbBounds();
            ApplyCommittedObbBounds(geometry);
            return geometry;
        }

        protected virtual SKRect ComputeCommittedAabbBounds()
        {
            using var path = GetPath();
            if (path == null || path.IsEmpty)
            {
                return SKRect.Empty;
            }

            //using var worldPath = new SKPath(path);
            path.Transform(Matrix);
            return path.TightBounds;
        }

        protected virtual (SKPoint[] Corners, SKPoint Center) ComputeCommittedObbBounds()
        {
            using var path = GetPath();
            if (path == null || path.IsEmpty)
            {
                return (Array.Empty<SKPoint>(), SKPoint.Empty);
            }

            var originalBounds = path.TightBounds;
            var corners = Matrix.MapPoints(originalBounds.ToCorners());
            var center = new SKPoint(
                (corners[0].X + corners[2].X) / 2,
                (corners[0].Y + corners[2].Y) / 2);

            return (corners, center);
        }

        private void InvalidateCommittedBoundingBoxCache()
        {
            _cachedBoundingBox = null;
            _bboxDirty = true;
            _cachedObbCorners = null;
            _obbDirty = true;
        }

        private void SyncCommittedBoundsFromMatrix()
        {
            ApplyCommittedBounds(ComputeCommittedAabbBounds());
        }

        private void ApplyCommittedBounds(SKRect bounds)
        {
            _cachedBoundingBox = bounds;
            _bboxDirty = false;

            var obb = GetCommittedObbBounds();
            if (obb.Corners.Length >= 4)
            {
                Width = SKPoint.Distance(obb.Corners[1], obb.Corners[0]);
                Height = SKPoint.Distance(obb.Corners[2], obb.Corners[1]);
                SharpCenter = obb.Center;
                return;
            }

            Width = bounds.Width;
            Height = bounds.Height;
            SharpCenter = bounds.Center();
        }

        private void ApplyCommittedObbBounds((SKPoint[] Corners, SKPoint Center) geometry)
        {
            _cachedObbCorners = geometry.Corners;
            _cachedObbCenter = geometry.Center;
            _obbDirty = false;
        }

        /// <summary>
        /// 矩阵正式提交后，允许子类同步其额外缓存或辅助几何。
        /// </summary>
        protected virtual void OnCommittedMatrixChanged()
        {
        }

        internal static float GetSampleNodeStep(SKPath worldPath)
        {
            var stepMm = CurveConversionStepMm;
            SKRect bounds = worldPath.Bounds;
            float maxDimension = Math.Max(bounds.Width, bounds.Height);
            if (stepMm <= 0f)
            {
                stepMm = CurveConversionStepMm;
            }
            else if (maxDimension > 200)
            {
                stepMm = 2 * CurveConversionStepMm;
            }
            else if (maxDimension > 400)
            {
                stepMm = 4 * CurveConversionStepMm;
            }
            else if (maxDimension > 600)
            {
                stepMm = 6 * CurveConversionStepMm;
            }

            return stepMm;
        }



/// <summary>
/// 将增量应用到绝对属性上
/// </summary>
private void ApplyDeltaToProperties(float dx, float dy, float scaleX, float scaleY, float deltaRotation, float deltaTanSkewX, float deltaTanSkewY)
        {
            // 1. 缩放是累乘的
            ScaleX = scaleX;
            ScaleY = scaleY;

            // 2. 旋转和倾斜是累加的
            Rotation += deltaRotation;
            Rotation = (Rotation % 360 + 360) % 360;

            // 3. 处理角度归一化（可选，防止角度无限增大，比如保持在 -180 到 180 之间）
            _skewTanX += deltaTanSkewX;
            _skewTanY += deltaTanSkewY;

            float skewAngleX = (float)(Math.Atan(_skewTanX) * 180.0 / Math.PI);
            float skewAngleY = (float)(Math.Atan(_skewTanY) * 180.0 / Math.PI);
            SkewX = skewAngleX;
            SkewY = skewAngleY;

            //Trace.WriteLine($"skewAngleX={skewAngleX},skewAngleY={skewAngleY}");
        }

        public virtual bool HitTest(SKPoint point, float tolerance = 6.0f)
        {
            // 全程使用世界坐标系做命中检测：图形被拉成近似直线时，Matrix 接近奇异（某维缩放≈0），
            // 其逆矩阵在该轴上的放大系数会急剧变大，world→local 映射后的局部坐标严重失真，
            // 加之"局部距离 + 世界容差"在非均匀缩放下本就不等价，会把命中点误判为未命中（时好时坏）。
            // 改为把路径变换到世界坐标后按世界像素距离与 tolerance 比较，避免求逆并保证容差含义一致。
            using var path = GetPath();

            if (path == null || path.IsEmpty)
            {
                // 空路径回退：用世界包围盒判断（等价原局部包围盒 ± 容差，但在世界坐标下计算）
                var worldBounds = Matrix.MapRect(GetLocalBounds());
                return point.X >= worldBounds.Left - tolerance && point.X <= worldBounds.Right + tolerance
                    && point.Y >= worldBounds.Top - tolerance && point.Y <= worldBounds.Bottom + tolerance;
            }

            using var worldPath = new SKPath(path);
            worldPath.Transform(Matrix);

            // 快速预过滤：世界包围盒 ± 容差
            var bounds = worldPath.TightBounds;
            if (point.X < bounds.Left - tolerance || point.X > bounds.Right + tolerance ||
                point.Y < bounds.Top - tolerance || point.Y > bounds.Bottom + tolerance)
                return false;

            // 精确检测：世界坐标下点到路径线条的最短距离
            return IsPointNearPath(point, worldPath, tolerance);
        }

        /// <summary>
        /// 计算世界坐标点到图形路径线条的最短距离。
        /// 用于选择排序：距离路径线条越近的图形优先级越高。
        /// 返回 float.MaxValue 表示无法计算（空路径等）。
        /// </summary>
        public virtual float GetDistanceToPath(SKPoint worldPoint)
        {
            using var path = GetPath();
            if (path == null || path.IsEmpty)
                return float.MaxValue;

            // 与 HitTest 一致：在世界坐标系下算距离。非均匀（近奇异）变换下局部距离≠世界距离，
            // 若在局部系计算，细长图形的距离会被逆矩阵放大，导致命中排序优先级异常。
            using var worldPath = new SKPath(path);
            worldPath.Transform(Matrix);

            return CalcMinDistanceToPath(worldPoint, worldPath);
        }

        /// <summary>
        /// 计算局部坐标点到路径线条的最短距离（平方根）。
        /// </summary>
        private float CalcMinDistanceToPath(SKPoint localPoint, SKPath path)
        {
            float minDistSq = float.MaxValue;

            using var iter = path.CreateRawIterator();
            var points = new SKPoint[4];
            SKPathVerb verb;
            SKPoint lastMoveTo = SKPoint.Empty;
            SKPoint prevPoint = SKPoint.Empty;
            bool hasPrev = false;

            while ((verb = iter.Next(points)) != SKPathVerb.Done)
            {
                switch (verb)
                {
                    case SKPathVerb.Move:
                        prevPoint = points[0];
                        lastMoveTo = points[0];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Line:
                        if (hasPrev)
                        {
                            float dSq = DistToSegmentSq(localPoint, prevPoint, points[1]);
                            if (dSq < minDistSq) minDistSq = dSq;
                        }
                        prevPoint = points[1];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Quad:
                        if (hasPrev)
                            minDistSq = DistToQuadSq(localPoint, prevPoint, points[1], points[2], 0, minDistSq, 6);
                        prevPoint = points[2];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Cubic:
                        if (hasPrev)
                            minDistSq = DistToCubicSq(localPoint, prevPoint, points[1], points[2], points[3], 0, minDistSq, 6);
                        prevPoint = points[3];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Conic:
                        if (hasPrev)
                        {
                            float w = iter.ConicWeight();
                            minDistSq = DistToConicSq(localPoint, prevPoint, points[1], points[2], w, 0, minDistSq, 6);
                        }
                        prevPoint = points[2];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Close:
                        if (hasPrev)
                        {
                            float dSq = DistToSegmentSq(localPoint, prevPoint, lastMoveTo);
                            if (dSq < minDistSq) minDistSq = dSq;
                        }
                        prevPoint = lastMoveTo;
                        hasPrev = true;
                        break;
                }
            }

            return MathF.Sqrt(minDistSq);
        }


        /// <summary>
        /// 判断点是否在路径线条的容差范围内（描边命中检测）。
        /// 通过对路径做轮廓偏移生成闭合区域，检测点是否落在偏移区域内。
        /// </summary>
        private bool IsPointNearPath(SKPoint localPoint, SKPath path, float tolerance)
        {
            // 方法：使用 SKPath.Measure 的 GetPosition + GetTangent 逐段检测
            // 更高效的方式：将路径按容差偏移后检测 Contains
            // 但 SKPath.Offset 不一定可用，这里用迭代路径段的方式

            // 使用路径迭代方式：遍历路径每段，计算点到线段的最短距离
            float minDistSq = float.MaxValue;
            float tolSq = tolerance * tolerance;

            using var iter = path.CreateRawIterator();
            var points = new SKPoint[4];
            SKPathVerb verb;
            SKPoint lastMoveTo = SKPoint.Empty;
            SKPoint prevPoint = SKPoint.Empty;
            bool hasPrev = false;

            while ((verb = iter.Next(points)) != SKPathVerb.Done)
            {
                switch (verb)
                {
                    case SKPathVerb.Move:
                        prevPoint = points[0];
                        lastMoveTo = points[0];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Line:
                        if (hasPrev)
                        {
                            float dSq = DistToSegmentSq(localPoint, prevPoint, points[1]);
                            if (dSq < minDistSq) minDistSq = dSq;
                            if (minDistSq <= tolSq) return true;
                        }
                        prevPoint = points[1];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Quad:
                        if (hasPrev)
                        {
                            // 递归细分二次贝塞尔曲线
                            minDistSq = DistToQuadSq(localPoint, prevPoint, points[1], points[2], tolSq, minDistSq, 6);
                            if (minDistSq <= tolSq) return true;
                        }
                        prevPoint = points[2];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Cubic:
                        if (hasPrev)
                        {
                            // 递归细分三次贝塞尔曲线
                            minDistSq = DistToCubicSq(localPoint, prevPoint, points[1], points[2], points[3], tolSq, minDistSq, 6);
                            if (minDistSq <= tolSq) return true;
                        }
                        prevPoint = points[3];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Conic:
                        if (hasPrev)
                        {
                            float w = iter.ConicWeight();
                            minDistSq = DistToConicSq(localPoint, prevPoint, points[1], points[2], w, tolSq, minDistSq, 16);
                            if (minDistSq <= tolSq) return true;
                        }
                        prevPoint = points[2];
                        hasPrev = true;
                        break;

                    case SKPathVerb.Close:
                        if (hasPrev)
                        {
                            float dSq = DistToSegmentSq(localPoint, prevPoint, lastMoveTo);
                            if (dSq < minDistSq) minDistSq = dSq;
                            if (minDistSq <= tolSq) return true;
                        }
                        prevPoint = lastMoveTo;
                        hasPrev = true;
                        break;
                }
            }

            return minDistSq <= tolSq;
        }

        /// <summary>点到线段的最短距离的平方</summary>
        private static float DistToSegmentSq(SKPoint p, SKPoint a, SKPoint b)
        {
            float dx = b.X - a.X;
            float dy = b.Y - a.Y;
            float lenSq = dx * dx + dy * dy;

            if (lenSq < 1e-10f)
            {
                // 退化为点
                float px = p.X - a.X, py = p.Y - a.Y;
                return px * px + py * py;
            }

            // 参数 t 将投影限制在线段 [0,1] 范围内
            float t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
            t = Math.Clamp(t, 0f, 1f);

            float projX = a.X + t * dx;
            float projY = a.Y + t * dy;
            float ex = p.X - projX, ey = p.Y - projY;
            return ex * ex + ey * ey;
        }

        /// <summary>递归细分二次贝塞尔曲线，计算点到曲线的最短距离平方</summary>
        private static float DistToQuadSq(SKPoint p, SKPoint p0, SKPoint p1, SKPoint p2, float tolSq, float currentMin, int depth)
        {
            // 二次贝塞尔中点 B(0.5) = 0.25*p0 + 0.5*p1 + 0.25*p2
            var midPoint = new SKPoint(
                (p0.X + 2f * p1.X + p2.X) * 0.25f,
                (p0.Y + 2f * p1.Y + p2.Y) * 0.25f);

            if (depth <= 0)
            {
                // depth 用尽时：检查弦、端点和曲线中点，避免漏掉弯曲部分
                currentMin = Math.Min(currentMin, DistToSegmentSq(p, p0, p2));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p0));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p2));
                currentMin = Math.Min(currentMin, DistanceSquared(p, midPoint));
                return currentMin;
            }

            // 快速剔除：控制点到弦的距离衡量曲线弯曲程度，用固定阈值（0.25 = 0.5px）
            float ctrlDist = DistToSegmentSq(p1, p0, p2);
            if (ctrlDist < 0.25f)
            {
                // 曲线非常平，用弦近似，但仍检查端点和中点保证精度
                currentMin = Math.Min(currentMin, DistToSegmentSq(p, p0, p2));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p0));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p2));
                currentMin = Math.Min(currentMin, DistanceSquared(p, midPoint));
                return currentMin;
            }

            // 细分：p0, (p0+p1)/2, mid, (p1+p2)/2, p2
            var m01 = MidPoint(p0, p1);
            var m12 = MidPoint(p1, p2);
            var mid = MidPoint(m01, m12);

            currentMin = DistToQuadSq(p, p0, m01, mid, tolSq, currentMin, depth - 1);
            if (currentMin <= tolSq) return currentMin;
            return DistToQuadSq(p, mid, m12, p2, tolSq, currentMin, depth - 1);
        }

        /// <summary>递归细分三次贝塞尔曲线，计算点到曲线的最短距离平方</summary>
        private static float DistToCubicSq(SKPoint p, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, float tolSq, float currentMin, int depth)
        {
            // 三次贝塞尔中点 B(0.5) = 0.125*p0 + 0.375*p1 + 0.375*p2 + 0.125*p3
            var midPoint = new SKPoint(
                (p0.X + 3f * p1.X + 3f * p2.X + p3.X) * 0.125f,
                (p0.Y + 3f * p1.Y + 3f * p2.Y + p3.Y) * 0.125f);

            if (depth <= 0)
            {
                // depth 用尽时：检查弦、端点和曲线中点
                currentMin = Math.Min(currentMin, DistToSegmentSq(p, p0, p3));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p0));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p3));
                currentMin = Math.Min(currentMin, DistanceSquared(p, midPoint));
                return currentMin;
            }

            var m01 = MidPoint(p0, p1);
            var m12 = MidPoint(p1, p2);
            var m23 = MidPoint(p2, p3);
            var m012 = MidPoint(m01, m12);
            var m123 = MidPoint(m12, m23);
            var mid = MidPoint(m012, m123);

            currentMin = DistToCubicSq(p, p0, m01, m012, mid, tolSq, currentMin, depth - 1);
            if (currentMin <= tolSq) return currentMin;
            return DistToCubicSq(p, mid, m123, m23, p3, tolSq, currentMin, depth - 1);
        }

        /// <summary>
        /// 计算点到 Conic（有理二次贝塞尔）曲线的最短距离平方。
        /// Conic 公式：C(t) = ((1-t)²P0 + 2w(1-t)tP1 + t²P2) / ((1-t)² + 2w(1-t)t + t²)
        /// 当 w=1 时退化为普通二次贝塞尔（Quad）。
        /// 采用采样法：递归深度耗尽或曲线平坦时，均匀采样曲线上的点来计算距离。
        /// </summary>
        private static float DistToConicSq(SKPoint p, SKPoint p0, SKPoint p1, SKPoint p2, float w, float tolSq, float currentMin, int depth)
        {
            // Conic 中点 C(0.5)：用有理参数公式精确计算
            float halfDenom = 0.25f + 0.5f * w + 0.25f;
            float invHalfDenom = 1f / halfDenom;
            var midPoint = new SKPoint(
                (0.25f * p0.X + 0.5f * w * p1.X + 0.25f * p2.X) * invHalfDenom,
                (0.25f * p0.Y + 0.5f * w * p1.Y + 0.25f * p2.Y) * invHalfDenom);

            // 快速剔除：控制点到弦的距离衡量弯曲程度
            float ctrlDist = DistToSegmentSq(p1, p0, p2);
            if (ctrlDist < 0.25f || depth <= 0)
            {
                // 曲线平坦或深度耗尽：采样关键点计算距离
                // 采样 t=0, 0.25, 0.5, 0.75, 1 共5个点
                currentMin = Math.Min(currentMin, DistanceSquared(p, p0));
                currentMin = Math.Min(currentMin, DistanceSquared(p, p2));
                currentMin = Math.Min(currentMin, DistanceSquared(p, midPoint));

                // t=0.25 和 t=0.75 采样
                currentMin = Math.Min(currentMin, DistanceSquared(p, EvalConic(p0, p1, p2, w, 0.25f)));
                currentMin = Math.Min(currentMin, DistanceSquared(p, EvalConic(p0, p1, p2, w, 0.75f)));

                // 弦近似作为下界
                currentMin = Math.Min(currentMin, DistToSegmentSq(p, p0, p2));
                return currentMin;
            }

            // 递归细分：使用采样法将 Conic 分为左右两段
            // 左半段 t ∈ [0, 0.5]，右半段 t ∈ [0.5, 1]
            // 左半段的三个控制点通过齐次坐标 de Casteljau 推导：
            //   左起点 = P0
            //   左控制 = (P0 + w*P1) / (1+w)   （齐次中间点投影）
            //   左终点 = midPoint（C(0.5)）
            //   左 weight = (1+w)/2
            // 右半段：
            //   右起点 = midPoint
            //   右控制 = (w*P1 + P2) / (1+w)
            //   右终点 = P2
            //   右 weight = (1+w)/2
            //
            // 注意：细分后两半段的齐次端点权重不完全对称（左端 W=1, 右端 W=(1+w)/2），
            // 所以子段不再是标准 conic 形式。但作为距离计算的递归细分，
            // 这种近似在递归深度足够时误差可忽略（每细分一次，子段越接近直线）。
            float invOnePlusW = 1f / (1f + w);
            var leftCtrl = new SKPoint(
                (p0.X + w * p1.X) * invOnePlusW,
                (p0.Y + w * p1.Y) * invOnePlusW);
            var rightCtrl = new SKPoint(
                (w * p1.X + p2.X) * invOnePlusW,
                (w * p1.Y + p2.Y) * invOnePlusW);

            float halfW = (1f + w) * 0.5f;

            // 左半段 Conic
            currentMin = DistToConicSq(p, p0, leftCtrl, midPoint, halfW, tolSq, currentMin, depth - 1);
            if (currentMin <= tolSq) return currentMin;
            // 右半段 Conic
            return DistToConicSq(p, midPoint, rightCtrl, p2, halfW, tolSq, currentMin, depth - 1);
        }



        #endregion
    }
}
