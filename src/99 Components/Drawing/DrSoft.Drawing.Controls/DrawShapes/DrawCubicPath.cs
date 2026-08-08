using System;
using System.Collections.Generic;
using System.Linq;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 三次贝塞尔路径图形。
    /// 存储结构：每段由 (锚点, 出控制点, 入控制点, 锚点) 描述，使用 SKPath.CubicTo 绘制。
    /// Points 存储各段的锚点（节点拖动操作对象），ControlHandles 存储对应的控制点对。
    /// </summary>
    public class DrawCubicPath : DrawObject
    {
        private float _baseWidth = 0;
        private float _baseHeight = 0;

        // 局部坐标锚点（相对于 SharpCenter）
        private List<SKPoint> _localAnchors = null;

        // 局部坐标控制点句柄（每个锚点对应 2 个句柄：[i*2]=出句柄, [i*2+1]=入句柄）
        // 入/出 均相对于 SharpCenter
        private List<SKPoint> _localHandles = null;

        /// <summary>
        /// 控制点句柄（世界坐标，与 Points 中的锚点一一对应，每个锚点 2 个）
        /// [i*2]   = 第 i 个锚点的"出方向"控制点
        /// [i*2+1] = 第 i 个锚点的"入方向"控制点
        /// </summary>
        public List<SKPoint> ControlHandles { get; set; } = new();

        /// <summary>
        /// 是否为闭合路径
        /// </summary>
        public bool IsClosed { get; set; } = true;

        public DrawCubicPath()
        {
            UId = UniqueIdGenerator.NextId();
            Type = ShapeType.CubicPath;
        }

        public override List<Point2D> OutlinePoints
        {
            get => Points.Select(p => new Point2D(p.X, p.Y)).ToList();
            set => Points = value.Select(p => new SKPoint(p.X, p.Y)).ToList();
        }

        public override IShape Clone()
        {
            var clone = new DrawCubicPath
            {
                IsClosed = this.IsClosed,
                Points = new List<SKPoint>(Points),
                ControlHandles = new List<SKPoint>(ControlHandles)
            };
            if (_localAnchors != null)
                clone._localAnchors = new List<SKPoint>(_localAnchors);
            if (_localHandles != null)
                clone._localHandles = new List<SKPoint>(_localHandles);
            clone._baseWidth = _baseWidth;
            clone._baseHeight = _baseHeight;
            return FinalizeClone(clone);
        }

        /// <summary>
        /// 通过锚点和控制句柄初始化，计算 SharpCenter / Width / Height 及局部坐标。
        /// anchors: 锚点世界坐标列表（n 个）
        /// handles: 控制句柄世界坐标列表（n*2 个，[i*2]=出, [i*2+1]=入）
        /// </summary>
        public void Initialize(List<SKPoint> anchors, List<SKPoint> handles)
        {
            if (anchors == null || anchors.Count == 0) return;
            if (handles == null || handles.Count != anchors.Count * 2) return;

            Points = new List<SKPoint>(anchors);
            ControlHandles = new List<SKPoint>(handles);
            Type = ShapeType.CubicPath;

            // 计算所有点（锚点+控制点）的包围盒
            var allPts = anchors.Concat(handles);
            float minX = allPts.Min(p => p.X);
            float maxX = allPts.Max(p => p.X);
            float minY = allPts.Min(p => p.Y);
            float maxY = allPts.Max(p => p.Y);

            // 用路径的 TightBounds 更精确
            using var path = BuildWorldPath(anchors, handles, IsClosed);
            var bounds = path.TightBounds;
            if (bounds.Width > 0.001f || bounds.Height > 0.001f)
            {
                minX = bounds.Left; maxX = bounds.Right;
                minY = bounds.Bottom; maxY = bounds.Top; // Y 轴向上：Bottom < Top
                // SkiaSharp SKRect: Top < Bottom (屏幕坐标)，但我们是 Y 向上
                // 直接用 MidX / MidY
            }

            _baseWidth = Width;
            _baseHeight = Height;

            // 转换为局部坐标
            _localAnchors = anchors.Select(p => new SKPoint(p.X - SharpCenter.X, p.Y - SharpCenter.Y)).ToList();
            _localHandles = handles.Select(p => new SKPoint(p.X - SharpCenter.X, p.Y - SharpCenter.Y)).ToList();
        }

        /// <summary>
        /// 就地更新锚点和控制句柄的局部坐标，不重算 SharpCenter。
        /// 用于旋转 DrawCombination 内节点拖动，保持旋转中心稳定。
        /// worldAnchors: 新锚点世界坐标（必须与当前 Points.Count 相同）
        /// worldHandles: 新控制句柄世界坐标（必须与当前 ControlHandles.Count 相同），为 null 则保持不变
        /// </summary>
        public void UpdateLocalPointsInPlace(List<SKPoint> worldAnchors, List<SKPoint> worldHandles = null)
        {
            if (worldAnchors == null || worldAnchors.Count == 0) return;

            // 保持 SharpCenter 不变，只重算局部坐标
            float cx = SharpCenter.X;
            float cy = SharpCenter.Y;

            // 更新 Points（世界坐标）
            Points = new List<SKPoint>(worldAnchors);

            // 更新局部锚点坐标
            _localAnchors = worldAnchors.Select(p => new SKPoint(p.X - cx, p.Y - cy)).ToList();

            // 更新句柄
            if (worldHandles != null && worldHandles.Count == worldAnchors.Count * 2)
            {
                ControlHandles = new List<SKPoint>(worldHandles);
                _localHandles = worldHandles.Select(p => new SKPoint(p.X - cx, p.Y - cy)).ToList();
            }

            // 更新 Width/Height（基于局部锚点+句柄的包围盒，不移动 SharpCenter）
            var allLocal = _localAnchors.AsEnumerable();
            if (_localHandles != null) allLocal = allLocal.Concat(_localHandles);
            var pts = allLocal.ToList();
            if (pts.Count > 0)
            {
                float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                _baseWidth = Width;
                _baseHeight = Height;
            }
        }

        /// <summary>
        /// 通过锚点和控制句柄初始化，保持 SharpCenter 不变（即保持旋转中心稳定）。
        /// 用于在已旋转的 DrawCombination 内插入节点时，避免 SharpCenter 重算导致图形漂移。
        /// </summary>
        public void InitializePreserveSharpCenter(List<SKPoint> anchors, List<SKPoint> handles)
        {
            if (anchors == null || anchors.Count == 0) return;
            if (handles == null || handles.Count != anchors.Count * 2) return;

            // 保存旧 SharpCenter
            var savedCenter = SharpCenter;

            Initialize(anchors, handles);

            // 恢复 SharpCenter，重算局部坐标
            float cx = savedCenter.X;
            float cy = savedCenter.Y;
            _localAnchors = anchors.Select(p => new SKPoint(p.X - cx, p.Y - cy)).ToList();
            _localHandles = handles.Select(p => new SKPoint(p.X - cx, p.Y - cy)).ToList();

            // 更新 Width/Height（基于局部坐标范围）
            var allLocal = _localAnchors.AsEnumerable().Concat(_localHandles);
            var pts = allLocal.ToList();
            if (pts.Count > 0)
            {
                float minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
                float minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
                _baseWidth = Width;
                _baseHeight = Height;
            }
        }

        /// <summary>
        /// 兼容基类的 UpdateSetProperty，仅锚点列表（不含控制句柄）。
        /// 控制句柄由调用方通过 Initialize 设置，或在节点拖动时保持比例更新。
        /// </summary>
        public override void UpdateSetProperty(List<SKPoint> points)
        {
            if (points == null || points.Count == 0) return;

            // 如果控制句柄已存在且数量匹配，保持句柄相对于锚点的偏移量
            if (ControlHandles != null && ControlHandles.Count == points.Count * 2
                && Points != null && Points.Count == points.Count)
            {
                // 计算每个锚点的位移，同步移动句柄
                var newHandles = new List<SKPoint>(ControlHandles.Count);
                for (int i = 0; i < points.Count; i++)
                {
                    float dx = points[i].X - Points[i].X;
                    float dy = points[i].Y - Points[i].Y;
                    newHandles.Add(new SKPoint(ControlHandles[i * 2].X + dx, ControlHandles[i * 2].Y + dy));
                    newHandles.Add(new SKPoint(ControlHandles[i * 2 + 1].X + dx, ControlHandles[i * 2 + 1].Y + dy));
                }
                Initialize(points, newHandles);
            }
            else
            {
                // 无句柄信息，仅更新锚点（句柄保持不变或清空）
                Points = new List<SKPoint>(points);
                using var path = BuildWorldPath(points, ControlHandles, IsClosed);
                var bounds = path.TightBounds;
                _baseWidth = Width;
                _baseHeight = Height;
                _localAnchors = points.Select(p => new SKPoint(p.X - SharpCenter.X, p.Y - SharpCenter.Y)).ToList();
                if (ControlHandles != null)
                    _localHandles = ControlHandles.Select(p => new SKPoint(p.X - SharpCenter.X, p.Y - SharpCenter.Y)).ToList();
            }
        }

        public override SKPath GetPath()
        {
            EnsureLocalCoords();

            if (_localAnchors == null || _localAnchors.Count == 0)
                return new SKPath();

            float scaleX = _baseWidth > 0.001f ? Width / _baseWidth : 1f;
            float scaleY = _baseHeight > 0.001f ? Height / _baseHeight : 1f;

            var scaledAnchors = _localAnchors.Select(p => new SKPoint(p.X * scaleX, p.Y * scaleY)).ToList();
            var scaledHandles = _localHandles?.Select(p => new SKPoint(p.X * scaleX, p.Y * scaleY)).ToList();

            var path = BuildWorldPath(scaledAnchors, scaledHandles, IsClosed);

            // 同步 Points（世界坐标 = 局部坐标 + SharpCenter）
            Points = scaledAnchors.Select(p => new SKPoint(p.X + SharpCenter.X, p.Y + SharpCenter.Y)).ToList();
            if (scaledHandles != null)
                ControlHandles = scaledHandles.Select(p => new SKPoint(p.X + SharpCenter.X, p.Y + SharpCenter.Y)).ToList();

            return path;
        }

        /// <summary>
        /// 获取锚点的真实世界坐标（包含旋转/缩放变换）
        /// </summary>
        public List<SKPoint> GetWorldAnchors()
        {
            EnsureLocalCoords();
            if (_localAnchors == null) return new List<SKPoint>();
            var matrix = GetTransformMatrix();
            return _localAnchors.Select(p => matrix.MapPoint(p)).ToList();
        }

        private void EnsureLocalCoords()
        {
            if (_localAnchors == null || _localAnchors.Count == 0)
            {
                if (Points != null && Points.Count > 0 && ControlHandles != null && ControlHandles.Count == Points.Count * 2)
                    Initialize(Points, ControlHandles);
            }
        }

        /// <summary>
        /// 使用锚点+控制句柄列表构建 SKPath（局部或世界坐标均可）。
        /// 要求 handles.Count == anchors.Count * 2。
        /// </summary>
        public static SKPath BuildWorldPath(IList<SKPoint> anchors, IList<SKPoint> handles, bool closed)
        {
            var path = new SKPath();
            int n = anchors.Count;
            if (n < 2 || handles == null || handles.Count != n * 2)
            {
                // 退化为折线
                if (n > 0)
                {
                    path.MoveTo(anchors[0]);
                    for (int i = 1; i < n; i++) path.LineTo(anchors[i]);
                    if (closed) path.Close();
                }
                return path;
            }

            // 标准三次贝塞尔段：
            // 从 anchors[i] 到 anchors[i+1]
            // 使用 handles[i*2]（anchors[i] 的出句柄）和 handles[(i+1)*2+1]（anchors[i+1] 的入句柄）
            path.MoveTo(anchors[0]);
            for (int i = 0; i < n - 1; i++)
            {
                var cp1 = handles[i * 2];          // 当前点的出句柄
                var cp2 = handles[(i + 1) * 2 + 1]; // 下一点的入句柄
                path.CubicTo(cp1, cp2, anchors[i + 1]);
            }

            if (closed && n >= 2)
            {
                // 最后一段：从 anchors[n-1] 回到 anchors[0]
                var cp1 = handles[(n - 1) * 2];    // 最后点的出句柄
                var cp2 = handles[0 * 2 + 1];       // 第一点的入句柄
                path.CubicTo(cp1, cp2, anchors[0]);
                path.Close();
            }

            return path;
        }

        /// <summary>
        /// 返回局部坐标系中的真实包围盒。
        /// 使用路径 TightBounds 而非锚点+控制句柄的包围盒，
        /// 因为控制句柄可能落在真实曲线外部，导致选择框比实际图形偏大。
        /// </summary>
        public override SKRect GetLocalBounds()
        {
            EnsureLocalCoords();
            if (_localAnchors == null || _localAnchors.Count == 0)
                return base.GetLocalBounds();

            // 用路径的 TightBounds 获取真实曲线范围（不含控制句柄外扩）
            float scaleX = _baseWidth > 0.001f ? Width / _baseWidth : 1f;
            float scaleY = _baseHeight > 0.001f ? Height / _baseHeight : 1f;
            var scaledAnchors = _localAnchors.Select(p => new SKPoint(p.X * scaleX, p.Y * scaleY)).ToList();
            var scaledHandles = _localHandles?.Select(p => new SKPoint(p.X * scaleX, p.Y * scaleY)).ToList();

            using var path = BuildWorldPath(scaledAnchors, scaledHandles, IsClosed);
            path.Transform(GetTransformMatrix());
            var tightBounds = path.TightBounds;

            if (tightBounds.Width < 0.001f && tightBounds.Height < 0.001f)
                return base.GetLocalBounds();

            // 世界坐标 TightBounds 转换回局部坐标（减去 SharpCenter 偏移）
            return new SKRect(
                tightBounds.Left - SharpCenter.X,
                tightBounds.Top - SharpCenter.Y,
                tightBounds.Right - SharpCenter.X,
                tightBounds.Bottom - SharpCenter.Y);
        }

        /// <summary>
        /// 贝塞尔曲线的控制句柄可能落在真实曲线外部。
        /// 选择框和组合边界应贴合路径本身，而不是句柄包围盒。
        /// </summary>
        //public override SKRect GetAABB()
        //{
        //    if (!_bboxDirty && _cachedBoundingBox.HasValue)
        //        return _cachedBoundingBox.Value;

        //    using var localPath = GetPath();
        //    if (localPath.IsEmpty)
        //        return SKRect.Empty;

        //    localPath.Transform(GetTransformMatrix());
        //    var result = localPath.TightBounds;
        //    _cachedBoundingBox = result;
        //    _bboxDirty = false;
        //    return result;
        //}


        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points == null || Points.Count < 2) return false;
            return base.HitTest(p, tol);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points == null || Points.Count < 2) return false;
            return base.IntersectsWith(rect);
        }

        private SKPath BuildScaledLocalPath(float width, float height)
        {
            float scaleX = _baseWidth > 0.001f ? width / _baseWidth : 1f;
            float scaleY = _baseHeight > 0.001f ? height / _baseHeight : 1f;

            var scaledAnchors = _localAnchors.Select(p => new SKPoint(p.X * scaleX, p.Y * scaleY)).ToList();
            var scaledHandles = _localHandles?.Select(p => new SKPoint(p.X * scaleX, p.Y * scaleY)).ToList();
            return BuildWorldPath(scaledAnchors, scaledHandles, IsClosed);
        }

        // ── ISnapshotable ──────────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawCubicPathMemento(this);
        }

        protected class DrawCubicPathMemento : DrawObjectMemento
        {
            private readonly bool _isClosed;
            private readonly List<SKPoint> _controlHandles;
            private readonly List<SKPoint> _localAnchors;
            private readonly List<SKPoint> _localHandles;
            private readonly float _baseWidth;
            private readonly float _baseHeight;

            public DrawCubicPathMemento(DrawCubicPath cubicPath) : base(cubicPath)
            {
                _isClosed = cubicPath.IsClosed;
                _controlHandles = cubicPath.ControlHandles != null
                    ? new List<SKPoint>(cubicPath.ControlHandles) : null;
                _localAnchors = cubicPath._localAnchors != null
                    ? new List<SKPoint>(cubicPath._localAnchors) : null;
                _localHandles = cubicPath._localHandles != null
                    ? new List<SKPoint>(cubicPath._localHandles) : null;
                _baseWidth = cubicPath._baseWidth;
                _baseHeight = cubicPath._baseHeight;
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawCubicPath cubicPath)
                {
                    cubicPath.IsClosed = _isClosed;
                    cubicPath.ControlHandles = _controlHandles != null
                        ? new List<SKPoint>(_controlHandles) : new List<SKPoint>();
                    cubicPath._localAnchors = _localAnchors != null
                        ? new List<SKPoint>(_localAnchors) : null;
                    cubicPath._localHandles = _localHandles != null
                        ? new List<SKPoint>(_localHandles) : null;
                    cubicPath._baseWidth = _baseWidth;
                    cubicPath._baseHeight = _baseHeight;
                }
            }
        }
    }
}
