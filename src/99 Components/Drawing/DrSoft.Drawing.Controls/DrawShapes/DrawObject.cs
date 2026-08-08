using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using DrSoft.Drawing.Utility;
using Rougamo.Context;
using SkiaSharp;
using System.Diagnostics;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    internal readonly record struct PartitionContourData(SKRect Bounds, List<List<SKPoint>> Contours);
    internal readonly record struct WorldPathInfo(SKPath Path, bool IsClosed);
    internal readonly record struct BooleanPathEntry(SKPath WorldPath, bool IsClosed, DrawObject Source);
    internal readonly record struct MatrixCopyPreparation(int ColumnCount, int RowCount, float HorizontalSpacing, float VerticalSpacing)
    {
        public bool RequiresCloneGeneration => ColumnCount > 1 || RowCount > 1;
    }
    internal sealed record BooleanPathPreparation(List<DrawObject> OrderedShapes, List<BooleanPathEntry> Entries);
    internal readonly record struct CopyContainerPreparation(ILayerViewModel TargetLayer, DrawCombination Combination);
    internal readonly record struct SelectionContainerPreparation(ILayerViewModel TargetLayer, IShape ContainerShape);
    internal readonly record struct ContainerReleasePreparation(
        ILayerViewModel TargetLayer,
        IShape SourceShape,
        IReadOnlyList<IShape> ReleasedChildren,
        IShape? ParentContainer = null,
        int InsertIndex = -1);
    internal readonly record struct ActiveLayerResultPreparation(
        ILayerViewModel TargetLayer,
        IReadOnlyList<IShape> ResultShapes);

    /// <summary>
    /// 所有可绘制图形的抽象基类。
    /// <para>实现 <see cref="IShapeData"/> 只读数据契约，打标卡组件可通过该接口直接读取图形数据，无需 DTO 转换。</para>
    /// <para>渲染行为（SKCanvas、HitTest 等）仅在 Drawing.Controls 层可见，MarkCard 层不可访问。</para>
    /// </summary>
    public abstract partial class DrawObject : IShape, IPathProvider, IShapeData
    {
        public int UId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Direction { get; set; } = false;// 是否有方向（如箭头线）        

        // ── IShapeData：CenterX / CenterY（代理到 SharpCenter，避免冗余存储）──────────
        float IShapeData.CenterX => SharpCenter.X;
        float IShapeData.CenterY => SharpCenter.Y;
        // 显式接口委托到 protected virtual 方法，容器类（Group/Combination/Hatch）重写即可
        IReadOnlyList<IShapeData> IShapeData.ChildShapes => GetChildShapeData();
        /// <summary>子图形数据（容器图形重写此方法）；叶子图形默认返回空列表。</summary>
        protected virtual IReadOnlyList<IShapeData> GetChildShapeData() => Array.Empty<IShapeData>();

        // ── IShapeData：外框外观（代理到 _pen / HatchParamInfo，不暴露 SKPaint）──────────
        /// <summary>
        /// 外框颜色。_pen 为 null 时返回 null（使用图层共享颜色）。
        /// </summary>
        DrawingColor? IShapeData.OutlineColor => _pen == null
            ? null
            : new DrawingColor(_pen.Color.Red, _pen.Color.Green, _pen.Color.Blue, _pen.Color.Alpha);

        /// <summary>
        /// 外框样式。从 HatchParamInfo 读取（可 Hatchable 图形），
        /// 否则根据 _pen.PathEffect 推断：无 PathEffect 为实线，有 PathEffect 根据虚线参数判断。
        /// </summary>
        OutlineStyle IShapeData.OutlineStyle
        {
            get
            {
                if (this is IHatchable hatchable && hatchable.HatchParamInfo != null)
                    return (OutlineStyle)hatchable.HatchParamInfo.OutlineStyleIndex;

                if (_pen == null)
                    return OutlineStyle.Solid;

                if (_pen.Style == SKPaintStyle.StrokeAndFill && _pen.StrokeWidth <= 0)
                    return OutlineStyle.None;

                return _pen.PathEffect == null ? OutlineStyle.Solid : OutlineStyle.Dashed;
            }
        }

        // ── IShapeData：打标轮廓点、镂空跳点（代理到现有字段）────────────────────
        IReadOnlyList<(float X, float Y)> IShapeData.OutlinePoints =>
            OutlinePoints.Select(p => (p.X, p.Y)).ToArray();
        IReadOnlyList<(float X, float Y)> IShapeData.IntersectionSkipPoints =>
            WorldIntersectionSkipPoints.Count > 0
                ? WorldIntersectionSkipPoints.Select(p => (p.X, p.Y)).ToArray()
                : Array.Empty<(float, float)>();
        float IShapeData.IntersectionSkipRadius => IntersectionSkipRadius;
        int IShapeData.SelfIntersectionSkipCount => SelfIntersectionSkipCount;

        // SpatialGrid.Query 高频去重标记。
        // 仅用于单次视口查询窗口内避免跨格重复，不参与业务状态。
        internal long SpatialQueryStamp;

        //是否显示加工路径
        public bool ShowJumpLine { get; set; } = true;

        public bool IsClockwise { get; set; } = true; // 激光加工方向：true=顺时针，false=逆时针
        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                if (_isVisible == value) return;
                _isVisible = value;
                (_context.ActiveCanvas as Models.DrawingCanvas)?.InvalidateVisibleCache();
            }
        }
        public ShapeType Type { get; set; }

        // ── 世界坐标包围盒缓存 ──
        protected SKRect? _cachedBoundingBox;
        protected bool _bboxDirty = true;
        protected SKPoint[]? _cachedObbCorners;
        protected SKPoint _cachedObbCenter;
        protected bool _obbDirty = true;

        /// <summary>
        /// 当包围盒缓存失效时触发（子图形变换导致父级容器需重新计算）
        /// </summary>
        public event Action<DrawObject>? BoundingBoxInvalidated;

        // ── 世界变换矩阵缓存 ──
        private SKMatrix? _cachedTransformMatrix;
        private SKMatrix? _cachedInverseTransformMatrix;
        private bool _transformDirty = true;
        private bool _inverseTransformDirty = true;

        protected void NotifyBoundingBoxInvalidated()
        {
            BoundingBoxInvalidated?.Invoke(this);
        }
        // ── Points 懒分配：DXF 导入由 SceneStore 直接赋值，无需默认空列表 ──
        private List<SKPoint>? _points;
        public List<SKPoint> Points
        {
            get => _points ??= new List<SKPoint>();
            set => _points = value;
        }

        // ── 共享默认画笔──────
        private static readonly SKPaint SharedDefaultPen = new() { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 0.15f };
        private SKPaint? _pen;
        /// <summary>
        /// 图形画笔。优先使用图形自定义画笔（_pen），其次使用所属图层画笔，
        /// 再次使用当前活动图层画笔（绘制预览时图形尚未加入图层），最后回退到全局默认画笔。
        /// 同一图层所有未自定义画笔的图形共享图层画笔，零额外分配。
        /// </summary>
        public SKPaint Pen
        {
            get => _pen ?? OwningLayer?.LayerPen ?? ActiveLayerPen ?? SharedDefaultPen;
            set => _pen = value;
        }

        /// <summary>
        /// 获取图形自定义画笔（_pen），null 表示使用图层共享画笔。
        /// 仅用于撤销/重做命令的快照捕获。
        /// </summary>
        internal SKPaint? CustomPen => _pen;

        /// <summary>
        /// 若 _pen 引用的是旧图层的 LayerPen（非用户自定义画笔），则清空以让 Pen getter 解析到新图层。
        /// 当 shape 首次加入图层（oldLayer==null）时，_pen 一定是 Clone() 复制的 LayerPen 引用或复制的颜色，也应清空。
        /// </summary>
        internal void ClearLayerPenReference(DrawingLayer? oldLayer)
        {
            if (_pen == null) return;
            if (oldLayer != null && ReferenceEquals(_pen, oldLayer.LayerPen))
                _pen = null;       // _pen 直接引用旧图层 LayerPen
            else if (oldLayer == null)
                _pen = null;       // 首次加入图层（Clone 后），_pen 是复制值，清空让它走 LayerPen
            // else: _pen 是用户自定义画笔（非图层共享），保留
        }

        /// <summary>
        /// 获取当前活动图层的画笔，用于绘制预览（图形尚未加入图层时）。
        /// </summary>
        private static SKPaint? ActiveLayerPen
            => (DocumentContext.Instance?.ActiveCanvas as DrawingCanvas)
                ?.LayerViewViewModel?.ActiveLayer?.Model?.LayerPen;

        // ── 选中状态（增量注册到所属图层，避免全量遍历） ──
        private bool _isSelected;
        /// <summary>
        /// 所属图层引用，由 DrawingLayer.AddShape 设置，用于 IsSelected 增量注册/注销。
        /// </summary>
        internal DrawingLayer? OwningLayer { get; set; }

        /// <summary>
        /// 画布级选中通知回调，由 DrawingLayer 注入，指向 DrawingCanvas.OnShapeSelected/OnShapeDeselected。
        /// </summary>
        internal Action<IShape>? OnShapeSelectedAction { get; set; }
        internal Action<IShape>? OnShapeDeselectedAction { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                if (value)
                {
                    OwningLayer?.RegisterSelected(this);
                    OnShapeSelectedAction?.Invoke(this);
                }
                else
                {
                    OwningLayer?.UnregisterSelected(this);
                    OnShapeDeselectedAction?.Invoke(this);
                }
            }
        }

        /// <summary>
        /// 直接设置选中状态而不通知图层缓存（用于图层清空时的批量清理）。
        /// </summary>
        internal void SetIsSelectedSilent(bool value)
        {
            _isSelected = value;
        }

        public bool IsLocked { get; set; }
        public virtual bool CanTransform => !IsLocked;
        public bool IsPathEditing { get; set; }
        // ── PathNodes 懒分配：仅路径编辑时使用，DXF 导入零分配 ──
        private List<SKPoint>? _pathNodes;
        public List<SKPoint> PathNodes
        {
            get => _pathNodes ??= new List<SKPoint>();
            set => _pathNodes = value;
        }
        public int LayerId { get; set; }

        /// <summary>
        /// 相交镂空点（局部坐标）：跳点功能检测到与后续图形的交点后，
        /// 记录在这里，渲染和打标指令生成时在这些点附近留出镂空缺口，避免重复打标。
        /// 懒分配：DXF 导入数百万图形时无跳点则零分配。
        /// </summary>
        private List<SKPoint>? _intersectionSkipPoints;
        public List<SKPoint> IntersectionSkipPoints
        {
            get => _intersectionSkipPoints ??= new List<SKPoint>();
            set => _intersectionSkipPoints = value;
        }

        /// <summary>
        /// 相交镂空点（世界坐标），将局部坐标的 IntersectionSkipPoints 通过变换矩阵映射到世界坐标。
        /// 打标指令生成时使用此属性，确保跳点裁剪与 MarkCommand 坐标系一致。
        /// </summary>
        public IReadOnlyList<SKPoint> WorldIntersectionSkipPoints
        {
            get
            {
                if (_intersectionSkipPoints == null || _intersectionSkipPoints.Count == 0)
                    return Array.Empty<SKPoint>();
                return _intersectionSkipPoints.Select(p => Matrix.MapPoint(p)).ToArray();
            }
        }

        /// <summary>
        /// 相交镂空圈半径（毫米），作为单个镂空点形成的缺口半径。
        /// </summary>
        public float IntersectionSkipRadius { get; set; } = 0f;

        /// <summary>
        /// 自交跳点数量（IntersectionSkipPoints 前 N 个为自交点）。
        /// 用于渲染时判断哪些 skip point 有对应的桥接方向。
        /// </summary>
        public int SelfIntersectionSkipCount { get; set; } = 0;

        /// <summary>
        /// 自交跳点的桥接线段方向（世界坐标单位向量）。
        /// 渲染时在镂空交点后，沿此方向绘制 2 倍跳点半径的线段，
        /// 补齐被裁剪的"over"线段，使单图形跳点显示正常。
        /// </summary>
        private List<SKPoint>? _intersectionSkipBridgeDirections;
        public List<SKPoint> IntersectionSkipBridgeDirections
        {
            get => _intersectionSkipBridgeDirections ??= new List<SKPoint>();
            set => _intersectionSkipBridgeDirections = value;
        }

        public abstract SKPath GetPath();

        /// <summary>
        /// 使用路径池获取 SKPath，避免每次渲染都 new SKPath()。
        /// </summary>
        public virtual SKPath GetPath(SKPaintCache cache)
        {
            var path = cache.GetPath();
            FillPath(path);
            return path;
        }

        /// <summary>
        /// 将图形路径数据填充到已有的 SKPath 中（不创建新对象）。
        /// </summary>
        protected virtual void FillPath(SKPath path)
        {
            // 默认实现：用无参 GetPath() 的结果填充（兼容未重写的子类）
            using var src = GetPath();
            path.AddPath(src);
        }




        /// <summary>求 Conic 曲线上参数 t 处的点</summary>
        private static SKPoint EvalConic(SKPoint p0, SKPoint p1, SKPoint p2, float w, float t)
        {
            float mt = 1f - t;
            float denom = mt * mt + 2f * mt * t * w + t * t;
            if (denom < 1e-10f) return p0;
            float invDenom = 1f / denom;
            return new SKPoint(
                (mt * mt * p0.X + 2f * mt * t * w * p1.X + t * t * p2.X) * invDenom,
                (mt * mt * p0.Y + 2f * mt * t * w * p1.Y + t * t * p2.Y) * invDenom);
        }

        private static SKPoint MidPoint(SKPoint a, SKPoint b) =>
            new SKPoint((a.X + b.X) / 2, (a.Y + b.Y) / 2);

        private static float DistanceSquared(SKPoint a, SKPoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        // 计算旋转中心时的递归保护标志
        private bool _calculatingRotationCenter = false;

        public virtual void UpdateSetProperty(List<SKPoint> points) { }

        public abstract IShape Clone();

        private readonly DocumentContext _context = DocumentContext.Instance;


        public float X
        {
            get => SharpCenter.X;
        }

        public float Y
        {
            get => SharpCenter.Y;
        }

        public SKPoint RotationCenterLocal { get; set; } = new SKPoint(0, 0);

        //public SKPoint RotationCenter
        //{
        //    get => this.Matrix.MapPoint(RotationCenterLocal);
        //    set
        //    {
        //        RotationCenterLocal = GetInverseMatrix().MapPoint(value);
        //    }
        //}

        private SKPoint _worldRotationCenter = new SKPoint(0, 0);
        /// <summary>
        /// 获取或设置旋转中心的世界坐标
        /// </summary>
        //public SKPoint RotationCenter
        //{
        //    get => _worldRotationCenter;
        //    // 设置时，直接存储用户提供的世界坐标值
        //    set => _worldRotationCenter = value;
        //}
        /// <summary>
        /// 打标卡获取打标数据
        /// </summary>
        public virtual List<Point2D> OutlinePoints
        {
            get => Points.Select(it => new Point2D(it.X, it.Y)).ToList();
            set => Points = value.Select(it => new SKPoint(it.X, it.Y)).ToList();
        }


        public const float rectH = 0.5f * 6.83f;//选择框节点，高度
        public const float controlPointOffset = 0f;//控制点与实体，偏移
        public const float thirdControlPointOffset = 3f;//第三次点击控制点与实体，偏移
        //public const float controlPointOffset = 0.8f * 6.83f;//控制点与实体，偏移
        public const float sharpeOffset = 3.0f;//选择框与实体，偏移
        //public const float sharpeOffset = 2.6f;//选择框与实体，偏移
        public const float lineWidth = 0.2f * 6.83f;//选择框线宽
        /// <summary>
        /// 图形缩放时允许的最小宽高尺寸，防止图形缩到不可见。
        /// </summary>
        public const float MinDimension = 0.1f;

        public SKPoint GetCalculatedRotationCenterLocal()
        {
            // 防止递归调用导致 StackOverflow（某些路径可能在计算时触发属性访问回到此方法）
            if (_calculatingRotationCenter)
            {
                // 返回一个安全的后备值（使用当前的 RotationCenterLocal）
                System.Diagnostics.Debug.WriteLine("警告: GetCalculatedRotationCenterLocal 递归调用，被保护并返回后备值");
                return RotationCenterLocal;
            }
            _calculatingRotationCenter = true;

            // 1. 计算目标世界中心相对于图形基准点 (SharpCenter) 的偏移量
            SKPoint offsetFromBase = new SKPoint(
                _worldRotationCenter.X - SharpCenter.X,
                _worldRotationCenter.Y - SharpCenter.Y
            );

            // 2. 构建一个只包含核心变换（旋转、缩放、倾斜）的矩阵
            // 注意：顺序要与 GetTransformMatrix 中的一致
            var coreMatrix = SKMatrix.CreateIdentity();
            if (ScaleX != 1 || ScaleY != 1)
            {
                coreMatrix = coreMatrix.PostConcat(SKMatrix.CreateScale(ScaleX, ScaleY, 0, 0));
            }
            if (SkewX != 0 || SkewY != 0)
            {
                float tanX = MathF.Tan(SkewX * MathF.PI / 180f);
                float tanY = MathF.Tan(SkewY * MathF.PI / 180f);
                coreMatrix = coreMatrix.PostConcat(SKMatrix.CreateSkew(tanX, tanY));
            }
            if (Rotation != 0)
            {
                coreMatrix = coreMatrix.PostConcat(SKMatrix.CreateRotationDegrees(Rotation, 0, 0));
            }

            // 3. 计算核心变换矩阵的逆矩阵
            try
            {
                if (coreMatrix.TryInvert(out SKMatrix coreMatrixInv))
                {
                    // 4. 将偏移量通过逆矩阵映射，得到在未发生旋转/缩放/倾斜前的本地坐标
                    // 这个点就是我们要找的 RotationCenterLocal
                    return coreMatrixInv.MapPoint(offsetFromBase);
                }
                else
                {
                    // 如果矩阵不可逆（例如缩放为0），则返回一个fallback值
                    System.Diagnostics.Debug.WriteLine("警告: 核心变换矩阵无法求逆");
                    return new SKPoint(_worldRotationCenter.X - SharpCenter.X, _worldRotationCenter.Y - SharpCenter.Y);
                }
            }
            finally
            {
                _calculatingRotationCenter = false;
            }
        }
        /// <summary>
        /// 缩放锚点（局部坐标系）
        /// 在控制点拖动时设置，默认在图形中心(0,0)
        /// </summary>
        private SKPoint _scaleAnchorPoint = SKPoint.Empty;
        public SKPoint ScaleAnchorPoint
        {
            get => _scaleAnchorPoint;
            set
            {
                if (_scaleAnchorPoint == value)
                {
                    return;
                }

                var oldValue = _scaleAnchorPoint;
                _scaleAnchorPoint = value;
            }
        }

        /// <summary>
        /// 控制点拖动预览状态：拖动期间暂存目标 Width/Height/SharpCenter，
        /// 图形本体仍用原始值渲染，只有选择框用预览值绘制。
        /// 鼠标松开时一次性提交，ESC 取消时丢弃。
        /// </summary>
        //public bool IsControlPointDragging
        //{
        //    get;
        //    set;
        //} = false;
        public float PreviewWidth
        {
            get;
            set;
        }
        public float PreviewHeight { get; set; }
        public SKPoint PreviewSharpCenter { get; set; }
        public float PreviewRotation { get; set; }
        public float PreviewScaleX { get; set; } = 1f;
        public float PreviewScaleY { get; set; } = 1f;
        public float PreviewSkewX { get; set; }
        public float PreviewSkewY { get; set; }
        public SKPoint PreviewScaleAnchorPoint { get; set; } = SKPoint.Empty;

        /// <summary>
        /// 拖拽期间的目标 AABB（世界坐标轴对齐包围盒）。
        /// 当有值时，GetEffectiveWorldBounds 直接返回此值，
        /// 使选择框高度不受旋转影响（保持 AABB 绝对坐标语义）。
        /// </summary>
        public SKRect? PreviewAABB { get; set; } = null;

        private ControlPointType _currentDraggingControlPoint = ControlPointType.None;
        public SKMatrix GetTransformMatrix()
        {
            return this.Matrix;
        }

        public SKMatrix GetInverseMatrix()
        {
            //if (!_inverseTransformDirty && _cachedInverseTransformMatrix.HasValue)
            //{
            //    return _cachedInverseTransformMatrix.Value;
            //}

            var inverseMatrix = Matrix.Invert();
            //_cachedInverseTransformMatrix = inverseMatrix;
            //_inverseTransformDirty = false;
            return inverseMatrix;
        }

        public virtual SKRect GetLocalBounds()
        {
            using var path = GetPath();
            if (path == null || path.IsEmpty)
            {
                return SKRect.Empty;
            }

            return path.TightBounds;

            //return new SKRect(-Width / 2, Height / 2, Width / 2, -Height / 2);
        }

        /// <summary>
        /// 将当前图形的有效几何边界投影到目标坐标系。
        /// 默认实现仍基于有效局部内容边界的四角映射；真实几何不等于局部矩形的图形需重写。
        /// </summary>
        //internal virtual SKRect GetEffectiveBoundsIn(SKMatrix worldToTarget, bool usePreviewBounds)
        //{
        //    return GetLocalBounds();
        //    //var localBounds = usePreviewBounds && IsControlPointDragging
        //    //    ? GetEffectiveContentLocalBounds()
        //    //    : GetEffectiveContentLocalBounds();
        //    //if (localBounds.IsEmpty)
        //    //{
        //    //    return SKRect.Empty;
        //    //}

        //    //var localToWorld = usePreviewBounds && IsControlPointDragging
        //    //    ? GetEffectiveTransformMatrix()
        //    //    : GetTransformMatrix();

        //    //var topLeft = worldToTarget.MapPoint(localToWorld.MapPoint(new SKPoint(localBounds.Left, localBounds.Top)));
        //    //var topRight = worldToTarget.MapPoint(localToWorld.MapPoint(new SKPoint(localBounds.Right, localBounds.Top)));
        //    //var bottomRight = worldToTarget.MapPoint(localToWorld.MapPoint(new SKPoint(localBounds.Right, localBounds.Bottom)));
        //    //var bottomLeft = worldToTarget.MapPoint(localToWorld.MapPoint(new SKPoint(localBounds.Left, localBounds.Bottom)));

        //    //var minX = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomRight.X, bottomLeft.X));
        //    //var maxX = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomRight.X, bottomLeft.X));
        //    //var minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomRight.Y, bottomLeft.Y));
        //    //var maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomRight.Y, bottomLeft.Y));
        //    return default;
        //}

        /* /// <summary>
         /// 获取当前有效的变换矩阵（自动处理预览状态）
         /// 控制点拖动时用预览中心构建矩阵，否则用实际变换矩阵
         /// </summary>
         public virtual SKMatrix GetEffectiveTransformMatrix()
         {
             {
                 var matrix = SKMatrix.CreateIdentity();

                 // 缩放（包含镜像：ScaleX=-1 或 ScaleY=-1）
                 var effectiveScaleX = PreviewScaleX;
                 var effectiveScaleY = PreviewScaleY;
                 if (effectiveScaleX != 1 || effectiveScaleY != 1)
                 {
                     matrix = matrix.PostConcat(SKMatrix.CreateScale(
                         effectiveScaleX,
                         effectiveScaleY,
                         PreviewScaleAnchorPoint.X,
                         PreviewScaleAnchorPoint.Y));
                 }

                 var effectiveSkewX = PreviewSkewX;
                 var effectiveSkewY = PreviewSkewY;
                 if (effectiveSkewX != 0 || effectiveSkewY != 0)
                 {
                     float tanX = MathF.Tan(effectiveSkewX * MathF.PI / 180f);
                     float tanY = MathF.Tan(effectiveSkewY * MathF.PI / 180f);
                     matrix = matrix.PostConcat(SKMatrix.CreateSkew(tanX, tanY));
                 }

                 var effectiveRotation = PreviewRotation;
                 if (effectiveRotation != 0)
                     matrix = matrix.PostConcat(SKMatrix.CreateRotationDegrees(-effectiveRotation, 0, 0));

                 matrix = matrix.PostConcat(SKMatrix.CreateTranslation(PreviewSharpCenter.X, PreviewSharpCenter.Y));
                 return matrix;
             }
             return GetTransformMatrix();
         }*/

        /// <summary>
        /// 获取当前有效的世界坐标包围盒。
        /// 通过 path.Transform(matrix).TightBounds 获取精确边界，
        /// 避免局部矩形四角变换在旋转时产生偏大的 AABB。
        /// 控制点预览、正式渲染和脏区标记应统一走这里，避免预览字段在各处重复展开。
        /// </summary>
        public virtual SKRect GetEffectiveWorldBounds()
        {
            return this.GetPreviewAABB().Corners.ToRect();
        }

        public virtual bool IntersectsWith(SKRect rect)
        {
            var bounds = GetAABB();
            if (!bounds.IntersectsWith(rect))
                return false;

            // 无旋转无倾斜时，AABB 就是实际图形范围，直接返回
            if (Rotation == 0 && SkewX == 0 && SkewY == 0)
                return true;

            // 有旋转/倾斜时，AABB 比实际图形大，需做精确路径相交检测
            return IntersectsWithPathPrecise(rect);
        }

        /// <summary>
        /// 精确路径相交检测：用于旋转/倾斜图形的框选判断。
        /// 检查三个条件（任一满足即相交）：
        /// 1. 路径上有任何采样点落在选择矩形内
        /// 2. 选择矩形有任何角点在路径内部
        /// 3. 图形包围盒完全在选择矩形内（处理小图形完全包含场景）
        /// </summary>
        private bool IntersectsWithPathPrecise(SKRect rect)
        {
            try
            {
                var pathInfo = CreateWorldPathInfo();
                if (!pathInfo.HasValue || pathInfo.Value.Path.IsEmpty)
                    return false;

                using var worldPath = pathInfo.Value.Path;

                // 1. 检查路径上是否有采样点落在选择矩形内
                using var measure = new SKPathMeasure(worldPath, false, 1f);
                // 采样步长基于图形尺寸和选择框尺寸的较小值，确保细长三角形的边不会被漏检
                var pathBounds = worldPath.Bounds;
                float shapeSize = MathF.Max(pathBounds.Width, pathBounds.Height);
                float rectSize = MathF.Max(rect.Width, rect.Height);
                float step = MathF.Min(shapeSize, rectSize) / 10f;
                if (step < 0.05f) step = 0.05f;

                do
                {
                    float length = measure.Length;
                    if (length <= 0) continue;

                    int count = Math.Max(8, (int)(length / step));
                    for (int i = 0; i <= count; i++)
                    {
                        float d = length * i / count;
                        if (measure.GetPosition(d, out var pos))
                        {
                            if (pos.X >= rect.Left && pos.X <= rect.Right &&
                                pos.Y >= rect.Top && pos.Y <= rect.Bottom)
                                return true;
                        }
                    }
                } while (measure.NextContour());

                // 2. 检查选择矩形的角点是否在路径内部
                var corners = new[]
                {
                    new SKPoint(rect.Left, rect.Top),
                    new SKPoint(rect.Right, rect.Top),
                    new SKPoint(rect.Left, rect.Bottom),
                    new SKPoint(rect.Right, rect.Bottom)
                };
                foreach (var corner in corners)
                {
                    if (worldPath.Contains(corner.X, corner.Y))
                        return true;
                }

                // 3. 图形包围盒完全在选择矩形内（处理小图形完全被包含的场景）
                var bounds = GetAABB();
                if (bounds.Left >= rect.Left && bounds.Right <= rect.Right &&
                    bounds.Top >= rect.Top && bounds.Bottom <= rect.Bottom)
                    return true;

                return false;
            }
            catch
            {
                // 异常时回退到包围盒结果
                return GetAABB().IntersectsWith(rect);
            }
        }

        public virtual IEnumerable<IShape> Flatten()
        {
            yield return this;
        }

        internal virtual List<IShape> CreateCurveChildren()
        {
            var path = GetPath();
            if (path == null || path.IsEmpty)
                return new List<IShape>();

            var points = SampleCurvePathToPoints(path);
            if (points.Count < 2)
                return new List<IShape>();

            var polyLine = new DrawPolyLines(points)
            {
                IsClosed = IsCurvePathClosed(path),
                Pen = new SKPaint
                {
                    Color = Pen.Color,
                    Style = Pen.Style,
                    StrokeWidth = Pen.StrokeWidth,
                    IsAntialias = Pen.IsAntialias
                },
                Name = $"{Name}"
            };
            return new List<IShape> { polyLine };
        }

        internal virtual WorldPathInfo? CreateWorldPathInfo()
        {
            var localPath = GetPath();
            if (localPath == null || localPath.IsEmpty)
            {
                localPath?.Dispose();
                return null;
            }

            var worldPath = new SKPath(localPath);
            worldPath.Transform(this.Matrix);
            localPath.Dispose();

            return new WorldPathInfo(worldPath, IsCurvePathClosed(worldPath));
        }

        internal virtual List<(SKPoint P1, SKPoint P2)> SamplePathToSegments(float step = 0.5f)
        {
            var result = new List<(SKPoint, SKPoint)>();
            try
            {
                var pathInfo = CreateWorldPathInfo();
                if (!pathInfo.HasValue || pathInfo.Value.Path.IsEmpty)
                    return result;

                using var worldPath = pathInfo.Value.Path;

                using var measure = new SKPathMeasure(worldPath, false, 1f);
                do
                {
                    float length = measure.Length;
                    if (length <= 0) continue;

                    int count = Math.Max(2, (int)Math.Ceiling(length / step) + 1);
                    SKPoint prev = SKPoint.Empty;

                    for (int i = 0; i < count; i++)
                    {
                        float d = length * i / (count - 1);
                        if (!measure.GetPosition(d, out var pos)) continue;
                        if (i > 0) result.Add((prev, pos));
                        prev = pos;
                    }
                } while (measure.NextContour());
            }
            catch
            {
            }

            return result;
        }

        /// <summary>
        /// 采样本地路径（未变换）为线段序列，用于跳点自交检测。
        /// 返回的线段坐标为本地坐标，使跳点数据可随图形变换自动更新。
        /// </summary>
        internal virtual List<(SKPoint P1, SKPoint P2)> SampleLocalPathToSegments(float step = 0.5f)
        {
            var result = new List<(SKPoint, SKPoint)>();
            try
            {
                using var localPath = GetPath();
                if (localPath == null || localPath.IsEmpty)
                    return result;

                using var measure = new SKPathMeasure(localPath, false, 1f);
                do
                {
                    float length = measure.Length;
                    if (length <= 0) continue;

                    int count = Math.Max(2, (int)Math.Ceiling(length / step) + 1);
                    SKPoint prev = SKPoint.Empty;

                    for (int i = 0; i < count; i++)
                    {
                        float d = length * i / (count - 1);
                        if (!measure.GetPosition(d, out var pos)) continue;
                        if (i > 0) result.Add((prev, pos));
                        prev = pos;
                    }
                } while (measure.NextContour());
            }
            catch
            {
            }

            return result;
        }

        internal virtual List<IShape> CreateDotChildren(
            float gap,
            float radius,
            bool isCircle,
            bool needCornerPoints,
            float cornerAngleThreshold)
        {
            var result = new List<IShape>();
            var pathInfo = CreateWorldPathInfo();
            if (!pathInfo.HasValue || pathInfo.Value.Path.IsEmpty)
                return result;

            using var worldPath = pathInfo.Value.Path;
            var samplePoints = SamplePathToDotPositions(
                worldPath,
                gap,
                needCornerPoints,
                cornerAngleThreshold);
            var sourcePen = Pen ?? new SKPaint();
            int i = 0;

            foreach (var pt in samplePoints)
            {
                i++;
                IShape newShape;
                if (isCircle)
                {
                    var circle = new DrawCircle(new Point2D(pt.X, pt.Y), radius)
                    {
                        Name = $"{Name}-{i}",
                        LayerId = LayerId
                    };
                    circle.Pen = new SKPaint
                    {
                        Color = sourcePen.Color,
                        Style = SKPaintStyle.Stroke,
                        StrokeWidth = sourcePen.StrokeWidth,
                        IsAntialias = sourcePen.IsAntialias
                    };
                    newShape = circle;
                }
                else
                {
                    var dot = new DrawDot(pt)
                    {
                        Name = $"{Name}-{i}",
                        LayerId = LayerId
                    };
                    newShape = dot;
                }

                result.Add(newShape);
            }

            return result;
        }

        internal virtual List<IShape> CreateBooleanChildrenFromWorldPath(
            SKPath path,
            string name,
            float stepMm = 0.1f)
        {
            var children = new List<IShape>();
            if (path == null || path.IsEmpty || stepMm <= 0f)
                return children;

            // 简化容差：保留转折点，移除直线段上的冗余点
            const float simplifyTolerance = 0.05f;

            using var measure = new SKPathMeasure(path, false, 1f);
            int contourIndex = 0;
            do
            {
                float length = measure.Length;
                if (length < stepMm)
                    continue;

                bool isClosed = measure.IsClosed;
                int approxCount = Math.Max(8, (int)(length / stepMm) + 2);
                var pts = new List<SKPoint>(approxCount);

                for (float d = 0f; d < length; d += stepMm)
                {
                    if (measure.GetPosition(d, out var pt))
                        pts.Add(pt);
                }

                if (measure.GetPosition(length, out var endPt))
                {
                    if (pts.Count == 0)
                    {
                        pts.Add(endPt);
                    }
                    else
                    {
                        var last = pts[^1];
                        float dx = last.X - endPt.X;
                        float dy = last.Y - endPt.Y;
                        if (dx * dx + dy * dy > 1e-10f)
                            pts.Add(endPt);
                    }
                }

                if (pts.Count < 2)
                    continue;

                // 简化：仅保留转折点
                pts = SimplifyPolyline(pts, simplifyTolerance, isClosed);

                if (pts.Count < 2)
                    continue;

                string childName = contourIndex == 0 ? name : $"{name}_{contourIndex}";
                var polyLine = new DrawPolyLines(new List<SKPoint>(pts))
                {
                    IsClosed = isClosed,
                    Pen = new SKPaint
                    {
                        Color = Pen.Color,
                        Style = Pen.Style,
                        StrokeWidth = Pen.StrokeWidth,
                        IsAntialias = Pen.IsAntialias
                    },
                    Name = childName
                };
                children.Add(polyLine);
                contourIndex++;
            } while (measure.NextContour());

            return children;
        }

        /// <summary>
        /// Ramer-Douglas-Peucker 简化：仅保留转折点，移除直线段上的冗余点。
        /// </summary>
        /// <param name="points">原始密集点序列</param>
        /// <param name="tolerance">容差(mm)，点到直线的距离小于此值则被移除</param>
        /// <param name="isClosed">是否闭合路径</param>
        private static List<SKPoint> SimplifyPolyline(
            List<SKPoint> points, float tolerance, bool isClosed)
        {
            int n = points.Count;
            if (n <= 2) return points;

            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;

            // 闭合路径：首尾同点，额外保留末尾点
            if (isClosed && n > 2)
                keep[n - 1] = true;

            RdpSimplify(points, 0, n - 1, tolerance * tolerance, keep);

            var result = new List<SKPoint>(n);
            for (int i = 0; i < n; i++)
            {
                if (keep[i])
                    result.Add(points[i]);
            }

            // 闭合路径：去除与首点重复的末尾点
            if (isClosed && result.Count > 1)
            {
                var first = result[0];
                var last = result[^1];
                float dx = first.X - last.X;
                float dy = first.Y - last.Y;
                if (dx * dx + dy * dy < 1e-6f)
                    result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static void RdpSimplify(
            List<SKPoint> pts, int start, int end, float tolSq, bool[] keep)
        {
            if (end - start < 2) return;

            float ax = pts[start].X, ay = pts[start].Y;
            float bx = pts[end].X, by = pts[end].Y;
            float abx = bx - ax, aby = by - ay;
            float abLenSq = abx * abx + aby * aby;

            float maxDistSq = 0f;
            int maxIndex = start;

            for (int i = start + 1; i < end; i++)
            {
                float px = pts[i].X - ax, py = pts[i].Y - ay;
                float distSq;

                if (abLenSq < 1e-12f)
                {
                    distSq = px * px + py * py;
                }
                else
                {
                    float cross = px * aby - py * abx;
                    distSq = cross * cross / abLenSq;
                }

                if (distSq > maxDistSq)
                {
                    maxDistSq = distSq;
                    maxIndex = i;
                }
            }

            if (maxDistSq > tolSq)
            {
                keep[maxIndex] = true;
                RdpSimplify(pts, start, maxIndex, tolSq, keep);
                RdpSimplify(pts, maxIndex, end, tolSq, keep);
            }
        }

        internal virtual List<IShape> CreateClippedBooleanChildrenFromWorldPath(
            SKPath openPath,
            SKPath closedUnion,
            string name,
            bool keepInside,
            float clipStepMm = 0.02f,
            float outputStepMm = 0.1f)
        {
            if (openPath == null || openPath.IsEmpty || closedUnion == null || closedUnion.IsEmpty)
                return new List<IShape>();

            clipStepMm = Math.Max(0.001f, clipStepMm);
            using var clippedPath = new SKPath();
            using var measure = new SKPathMeasure(openPath, false, 1f);
            do
            {
                float length = measure.Length;
                if (length <= 0f)
                    continue;

                int sampleCount = Math.Max(2, (int)Math.Ceiling(length / clipStepMm));
                float previousDistance = 0f;
                bool previousInside = IsPathPointInside(measure, previousDistance, closedUnion);
                float segmentStart = previousInside == keepInside ? 0f : -1f;

                for (int i = 1; i <= sampleCount; i++)
                {
                    float currentDistance = length * i / sampleCount;
                    bool currentInside = IsPathPointInside(measure, currentDistance, closedUnion);
                    if (currentInside != previousInside)
                    {
                        float boundary = FindPathRegionBoundary(
                            measure, previousDistance, currentDistance, previousInside, closedUnion);
                        if (previousInside == keepInside)
                        {
                            if (segmentStart >= 0f && boundary > segmentStart + 1e-6f)
                                measure.GetSegment(segmentStart, boundary, clippedPath, true);
                            segmentStart = -1f;
                        }
                        else if (currentInside == keepInside)
                        {
                            segmentStart = boundary;
                        }
                    }

                    previousDistance = currentDistance;
                    previousInside = currentInside;
                }

                if (previousInside == keepInside && segmentStart >= 0f && length > segmentStart + 1e-6f)
                    measure.GetSegment(segmentStart, length, clippedPath, true);
            } while (measure.NextContour());

            return CreateBooleanChildrenFromWorldPath(clippedPath, name, outputStepMm);
        }

        private static bool IsPathPointInside(SKPathMeasure measure, float distance, SKPath closedPath)
        {
            return measure.GetPosition(distance, out var point)
                && closedPath.Contains(point.X, point.Y);
        }

        private static float FindPathRegionBoundary(
            SKPathMeasure measure,
            float start,
            float end,
            bool startInside,
            SKPath closedPath)
        {
            for (int i = 0; i < 16 && end - start > 1e-5f; i++)
            {
                float middle = (start + end) * 0.5f;
                if (IsPathPointInside(measure, middle, closedPath) == startInside)
                    start = middle;
                else
                    end = middle;
            }

            return (start + end) * 0.5f;
        }

        private static bool DoSegmentBoundsIntersect(SKPoint firstStart, SKPoint firstEnd, SKPoint secondStart, SKPoint secondEnd)
        {
            float firstLeft = Math.Min(firstStart.X, firstEnd.X);
            float firstRight = Math.Max(firstStart.X, firstEnd.X);
            float firstTop = Math.Min(firstStart.Y, firstEnd.Y);
            float firstBottom = Math.Max(firstStart.Y, firstEnd.Y);
            float secondLeft = Math.Min(secondStart.X, secondEnd.X);
            float secondRight = Math.Max(secondStart.X, secondEnd.X);
            float secondTop = Math.Min(secondStart.Y, secondEnd.Y);
            float secondBottom = Math.Max(secondStart.Y, secondEnd.Y);

            return firstLeft <= secondRight
                && firstRight >= secondLeft
                && firstTop <= secondBottom
                && firstBottom >= secondTop;
        }

        internal virtual List<IShape> CreateSplitBooleanChildrenFromWorldPath(
            SKPath openPath,
            IEnumerable<SKPath> cuttingPaths,
            string name,
            float sampleStepMm = 0.1f)
        {
            if (openPath == null || openPath.IsEmpty || cuttingPaths == null)
                return new List<IShape>();

            sampleStepMm = Math.Max(0.001f, sampleStepMm);
            var cutterSegments = new List<(SKPoint P1, SKPoint P2)>();
            foreach (var cuttingPath in cuttingPaths)
            {
                if (cuttingPath == null || cuttingPath.IsEmpty)
                    continue;

                using var cuttingMeasure = new SKPathMeasure(cuttingPath, false, 1f);
                do
                {
                    float length = cuttingMeasure.Length;
                    if (length <= 0f)
                        continue;

                    int sampleCount = Math.Max(2, (int)Math.Ceiling(length / sampleStepMm) + 1);
                    if (!cuttingMeasure.GetPosition(0f, out var previous))
                        continue;

                    for (int i = 1; i < sampleCount; i++)
                    {
                        float distance = length * i / (sampleCount - 1);
                        if (!cuttingMeasure.GetPosition(distance, out var current))
                            continue;

                        cutterSegments.Add((previous, current));
                        previous = current;
                    }
                } while (cuttingMeasure.NextContour());
            }

            if (cutterSegments.Count == 0)
                return CreateBooleanChildrenFromWorldPath(openPath, name, sampleStepMm);

            using var splitPath = new SKPath();
            using var measure = new SKPathMeasure(openPath, false, 1f);
            do
            {
                float length = measure.Length;
                if (length <= 0f || !measure.GetPosition(0f, out var previous))
                    continue;

                int sampleCount = Math.Max(2, (int)Math.Ceiling(length / sampleStepMm) + 1);
                var splitDistances = new List<float> { 0f, length };
                for (int i = 1; i < sampleCount; i++)
                {
                    float currentDistance = length * i / (sampleCount - 1);
                    if (!measure.GetPosition(currentDistance, out var current))
                        continue;

                    float segmentLength = SKPoint.Distance(previous, current);
                    if (segmentLength <= 1e-6f)
                    {
                        previous = current;
                        continue;
                    }

                    foreach (var cutter in cutterSegments)
                    {
                        if (!DoSegmentBoundsIntersect(previous, current, cutter.P1, cutter.P2)
                            || !DrawObjectExtensions.TryComputeSegmentIntersection(
                                previous, current, cutter.P1, cutter.P2, out var intersection))
                            continue;

                        float distanceOnSegment = SKPoint.Distance(previous, intersection);
                        float splitDistance = currentDistance - segmentLength + distanceOnSegment;
                        if (splitDistance > 1e-5f && splitDistance < length - 1e-5f)
                            splitDistances.Add(splitDistance);
                    }

                    previous = current;
                }

                splitDistances.Sort();
                for (int i = splitDistances.Count - 2; i >= 0; i--)
                {
                    if (Math.Abs(splitDistances[i + 1] - splitDistances[i]) < 1e-5f)
                        splitDistances.RemoveAt(i + 1);
                }

                for (int i = 0; i < splitDistances.Count - 1; i++)
                {
                    float start = splitDistances[i];
                    float end = splitDistances[i + 1];
                    if (end > start + 1e-5f)
                        measure.GetSegment(start, end, splitPath, true);
                }
            } while (measure.NextContour());

            return CreateBooleanChildrenFromWorldPath(splitPath, name, sampleStepMm);
        }

        internal virtual void CommitPreviewBounds()
        {
            if (Type != ShapeType.Point)
            {
                //Width = Math.Max(MinDimension, PreviewWidth);
                //Height = Math.Max(MinDimension, PreviewHeight);
            }
            Rotation = PreviewRotation;
            ScaleX = PreviewScaleX;
            ScaleY = PreviewScaleY;
            SkewX = PreviewSkewX;
            SkewY = PreviewSkewY;
            ScaleAnchorPoint = PreviewScaleAnchorPoint;
        }

        internal virtual void ApplyScaling(float scaleX, float scaleY, SKPoint scaleCenter)
        {
            if (Type is ShapeType.Point or ShapeType.Hatch)
                return;

            Scale(scaleX, scaleY, scaleCenter, GetWorldRotationRad(), true);
        }

        internal virtual bool TryApplyDimension(float targetWidth, float targetHeight)
        {
            if (Width < 0.001f || Height < 0.001f)
                return false;

            targetWidth = Math.Max(MinDimension, targetWidth);
            targetHeight = Math.Max(MinDimension, targetHeight);

            var obbLine1 = GetDistance(GetOBB().Corners[0], GetOBB().Corners[1]);
            var obbLine2 = GetDistance(GetOBB().Corners[0], GetOBB().Corners[3]);

            ApplyScaling(targetWidth / (float)obbLine1, targetHeight / (float)obbLine2, SharpCenter);
            return true;
        }

        private double GetDistance(SKPoint p1, SKPoint p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        internal virtual void ApplyCircleCopyTransform(
            SKPoint centerOffset,
            float angleDelta,
            bool rotateWithCircle,
            bool counterClockwise)
        {
            Translate(centerOffset.X, centerOffset.Y);
            if (!rotateWithCircle)
                return;

            float rotationSign = counterClockwise ? 1f : -1f;
            Rotation += rotationSign * angleDelta;
        }

        internal virtual void ApplyMatrixCopyOffset(SKPoint offset)
        {
            this.Translate(offset.X, offset.Y);
        }



        internal virtual void ResetJumpPointState(float skipRadius)
        {
            IntersectionSkipPoints?.Clear();
            IntersectionSkipRadius = skipRadius;
            SelfIntersectionSkipCount = 0;
            IntersectionSkipBridgeDirections?.Clear();
        }

        internal virtual void ApplyClosePath()
        {
            if (this is IClosable closable)
            {
                closable.IsClosed = true;
            }
        }

        internal virtual void ReverseDirection()
        {
            IsClockwise = !IsClockwise;
        }

        internal virtual void ApplyLockState(bool isLocked)
        {
            IsLocked = isLocked;
            Pen = new SKPaint
            {
                Color = isLocked ? SKColors.Gray : SKColors.Black,
                Style = Pen.Style,
                StrokeWidth = Pen.StrokeWidth
            };
        }

        internal virtual List<IShape> CreatePartitionShapes(
            float pw,
            float ph,
            float stepX,
            float stepY,
            Func<List<List<SKPoint>>, SKRect, List<List<SKPoint>>>? clipContours = null)
        {
            var results = new List<IShape>();
            var contourData = BuildPartitionContourData();
            if (contourData == null)
                return results;

            var bbox = contourData.Value.Bounds;
            var contours = contourData.Value.Contours;
            if (bbox.IsEmpty || contours.Count == 0)
                return results;

            clipContours ??= ClipPartitionContours;

            int childIndex = 0;
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

                    var clippedChains = clipContours(contours, new SKRect(left, top, right, bottom));
                    var partitionChildren = new List<DrawObject>();
                    foreach (var chain in clippedChains)
                    {
                        if (chain.Count < 2)
                            continue;

                        childIndex++;
                        partitionChildren.Add(CreatePartitionChild(chain, childIndex));
                    }

                    var partitionShape = CreatePartitionShape(partitionChildren, ++partIndex);
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

        private List<Point2D> SampleCurvePathToPoints(SKPath path)
        {
            var nodes = ExtractCurvePathNodes(path);
            var transformMatrix = this.Matrix;
            return nodes.Select(p =>
            {
                var world = transformMatrix.MapPoint(p);
                return new Point2D(world.X, world.Y);
            }).ToList();
        }

        /// <summary>
        /// 转曲线时多线段采样步长默认值（mm）。
        /// 比 GlobalVariableManagement.Resolution (0.02mm) 大很多，
        /// 避免圆/圆弧生成过多节点。
        /// </summary>
        protected const float CurveConversionStepMm = 0.5f;

        /// <summary>
        /// 使用 SKPathMeasure 按指定步长采样世界坐标路径，
        /// 将曲线段采样为折线点列表，供转多线段使用。
        /// </summary>
        /// <param name="worldPath">世界坐标路径</param>
        /// <param name="stepMm">采样步长（mm），默认 CurveConversionStepMm (0.5mm)</param>
        protected static List<Point2D> SampleWorldPathToPolylinePoints(SKPath worldPath, float stepMm = CurveConversionStepMm)
        {
            var result = new List<Point2D>();

            stepMm = GetSampleNodeStep(worldPath);

            using var measure = new SKPathMeasure(worldPath, resScale: 1, forceClosed: false);
            do
            {
                float length = measure.Length;
                for (float distance = 0; distance < length; distance += stepMm)
                {
                    if (measure.GetPosition(distance, out var point))
                        result.Add(new Point2D(point.X, point.Y));
                }
                // 确保终点被加入
                if (measure.GetPosition(length, out var lastPoint))
                {
                    if (result.Count == 0 ||
                        Math.Abs(lastPoint.X - result[result.Count - 1].X) > 1e-4f ||
                        Math.Abs(lastPoint.Y - result[result.Count - 1].Y) > 1e-4f)
                    {
                        result.Add(new Point2D(lastPoint.X, lastPoint.Y));
                    }
                }
            } while (measure.NextContour());

            return result;
        }

        private List<SKPoint> SamplePathToDotPositions(
            SKPath worldPath,
            float gap,
            bool needCornerPoints,
            float cornerAngleThreshold)
        {
            var allPoints = new List<SKPoint>();

            using var measure = new SKPathMeasure(worldPath, false, 1f);
            do
            {
                float length = measure.Length;
                if (length < 0.01f) continue;

                var cornerDistances = new List<float>();
                if (needCornerPoints && cornerAngleThreshold > 0)
                {
                    cornerDistances = DetectCornerDistances(measure, length, cornerAngleThreshold);
                }

                var distances = new List<float>();
                for (float d = 0; d <= length; d += gap)
                {
                    distances.Add(d);
                }

                if (distances.Count == 0 || Math.Abs(distances[^1] - length) > 0.01f)
                {
                    distances.Add(length);
                }

                if (cornerDistances.Count > 0)
                {
                    distances.AddRange(cornerDistances);
                    distances.Sort();
                    var merged = new List<float> { distances[0] };
                    for (int i = 1; i < distances.Count; i++)
                    {
                        if (distances[i] - merged[^1] > gap * 0.3f)
                            merged.Add(distances[i]);
                    }

                    distances = merged;
                }

                foreach (float d in distances)
                {
                    if (measure.GetPosition(d, out var pos))
                        allPoints.Add(pos);
                }
            } while (measure.NextContour());

            return allPoints;
        }

        private List<float> DetectCornerDistances(SKPathMeasure measure, float length, float angleThreshold)
        {
            var corners = new List<float>();
            const float sampleStep = 0.2f;
            int sampleCount = Math.Max(3, (int)Math.Ceiling(length / sampleStep) + 1);

            SKPoint prevTangent = SKPoint.Empty;
            bool hasPrev = false;

            for (int i = 0; i < sampleCount; i++)
            {
                float d = length * i / (sampleCount - 1);
                if (!measure.GetPositionAndTangent(d, out _, out var tangent))
                    continue;

                if (hasPrev)
                {
                    float dot = prevTangent.X * tangent.X + prevTangent.Y * tangent.Y;
                    dot = Math.Clamp(dot, -1f, 1f);
                    float angleDeg = (float)(Math.Acos(dot) * 180.0 / Math.PI);

                    if (angleDeg > angleThreshold)
                    {
                        corners.Add(d);
                    }
                }

                prevTangent = tangent;
                hasPrev = true;
            }

            return corners;
        }

        private static List<SKPoint> ExtractCurvePathNodes(SKPath path)
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
                    var last = nodes[nodes.Count - 1];
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
                var last = nodes[nodes.Count - 1];
                float dx = last.X - first.X;
                float dy = last.Y - first.Y;
                if (dx * dx + dy * dy < 1e-6f)
                    nodes.RemoveAt(nodes.Count - 1);
            }

            return nodes;
        }

        protected static bool IsCurvePathClosed(SKPath path)
        {
            using var iter = path.CreateRawIterator();
            var pts = new SKPoint[4];
            SKPathVerb verb;
            while ((verb = iter.Next(pts)) != SKPathVerb.Done)
            {
                if (verb == SKPathVerb.Close)
                    return true;
            }
            return false;
        }

        internal virtual PartitionContourData? BuildPartitionContourData()
        {
            SKPath? localPath;
            try
            {
                localPath = GetPath();
            }
            catch (NotImplementedException)
            {
                return null;
            }

            if (localPath == null || localPath.IsEmpty)
            {
                localPath?.Dispose();
                return null;
            }

            using (localPath)
            using (var worldPath = new SKPath(localPath))
            {
                worldPath.Transform(this.Matrix);
                var contours = SamplePartitionPathToContours(worldPath);
                if (contours.Count == 0)
                    return null;

                var bbox = worldPath.TightBounds;
                return bbox.IsEmpty ? null : new PartitionContourData(bbox, contours);
            }
        }

        internal virtual List<List<SKPoint>> SamplePartitionPathToContours(SKPath worldPath)
        {
            var contours = new List<List<SKPoint>>();
            float sampleStep = (float)GlobalVariableManagement.Resolution;

            using var measure = new SKPathMeasure(worldPath, false, 1f);
            do
            {
                float length = measure.Length;
                if (length < 0.01f)
                    continue;

                int count = Math.Max(2, (int)Math.Ceiling(length / sampleStep) + 1);
                var pts = new List<SKPoint>(count);

                for (int i = 0; i < count; i++)
                {
                    float d = length * i / (count - 1);
                    if (measure.GetPosition(d, out var pos))
                        pts.Add(pos);
                }

                if (pts.Count >= 2)
                    contours.Add(pts);
            } while (measure.NextContour());

            return contours;
        }

        internal virtual List<List<SKPoint>> ClipPartitionContours(List<List<SKPoint>> contours, SKRect rect)
        {
            var result = new List<List<SKPoint>>();

            foreach (var contour in contours)
            {
                List<SKPoint>? currentChain = null;

                for (int i = 0; i < contour.Count - 1; i++)
                {
                    var p1 = contour[i];
                    var p2 = contour[i + 1];

                    if (LiangBarskyClip(p1, p2, rect.Left, rect.Top, rect.Right, rect.Bottom,
                            out var clipped1, out var clipped2))
                    {
                        if (currentChain == null || !IsPointClose(currentChain[currentChain.Count - 1], clipped1))
                        {
                            if (currentChain != null && currentChain.Count >= 2)
                                result.Add(currentChain);
                            currentChain = new List<SKPoint> { clipped1 };
                        }
                        currentChain.Add(clipped2);
                    }
                    else
                    {
                        if (currentChain != null && currentChain.Count >= 2)
                            result.Add(currentChain);
                        currentChain = null;
                    }
                }

                if (currentChain != null && currentChain.Count >= 2)
                    result.Add(currentChain);
            }

            if (result.Count < 2)
                return result;

            var temp = result[0];
            var combined = new List<List<SKPoint>>();
            for (int i = 1; i < result.Count; i++)
            {
                var chain = result[i];
                if (IsPointClose(temp[temp.Count - 1], chain[0]))
                {
                    temp.AddRange(chain);
                }
                else if (IsPointClose(temp[temp.Count - 1], chain[chain.Count - 1]))
                {
                    chain.Reverse();
                    temp.AddRange(chain);
                }
                else if (IsPointClose(temp[0], chain[chain.Count - 1]))
                {
                    temp.InsertRange(0, chain);
                }
                else if (IsPointClose(temp[0], chain[0]))
                {
                    chain.Reverse();
                    temp.InsertRange(0, chain);
                }
                else
                {
                    combined.Add(temp);
                    temp = chain;
                }
            }

            combined.Add(temp);
            return combined;
        }

        internal bool IsPointClose(SKPoint a, SKPoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy < 1e-4f;
        }

        internal bool LiangBarskyClip(SKPoint p1, SKPoint p2, float left, float top, float right, float bottom, out SKPoint clipped1, out SKPoint clipped2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float tMin = 0f, tMax = 1f;

            Span<float> p = stackalloc float[4];
            Span<float> q = stackalloc float[4];
            p[0] = -dx; q[0] = p1.X - left;
            p[1] = dx; q[1] = right - p1.X;
            p[2] = -dy; q[2] = p1.Y - top;
            p[3] = dy; q[3] = bottom - p1.Y;

            for (int i = 0; i < 4; i++)
            {
                if (Math.Abs(p[i]) < 1e-10f)
                {
                    if (q[i] < 0)
                    {
                        clipped1 = clipped2 = SKPoint.Empty;
                        return false;
                    }
                }
                else
                {
                    float t = q[i] / p[i];
                    if (p[i] < 0)
                    {
                        if (t > tMin) tMin = t;
                    }
                    else
                    {
                        if (t < tMax) tMax = t;
                    }
                }
            }

            if (tMin > tMax)
            {
                clipped1 = clipped2 = SKPoint.Empty;
                return false;
            }

            clipped1 = new SKPoint(p1.X + dx * tMin, p1.Y + dy * tMin);
            clipped2 = new SKPoint(p1.X + dx * tMax, p1.Y + dy * tMax);
            return true;
        }

        internal virtual DrawObject CreatePartitionChild(IReadOnlyList<SKPoint> chain, int childIndex)
        {
            return new DrawPolyLines(chain.ToList())
            {
                Pen = new SKPaint
                {
                    Color = Pen.Color,
                    Style = Pen.Style,
                    StrokeWidth = Pen.StrokeWidth,
                    IsAntialias = Pen.IsAntialias
                },
                Name = $"{Name}_{childIndex}",
                IsClockwise = IsClockwise,
                LayerId = LayerId
            };
        }

        internal virtual IShape? CreatePartitionShape(IReadOnlyList<DrawObject> partitionChildren, int partIndex)
        {
            if (partitionChildren.Count == 0)
                return null;

            if (partitionChildren.Count == 1)
                return partitionChildren[0];

            return new DrawCombination(partitionChildren.Cast<IShape>().ToList())
            {
                Pen = new SKPaint
                {
                    Color = Pen.Color,
                    Style = Pen.Style,
                    StrokeWidth = Pen.StrokeWidth,
                    IsAntialias = Pen.IsAntialias
                },
                Name = $"{Name}_分区",
                IsClockwise = IsClockwise,
                LayerId = LayerId
            };
        }

        internal virtual T UsePreviewBounds<T>(Func<T> action)
        {
            float savedW = Width;
            float savedH = Height;
            SKPoint savedCenter = SharpCenter;

            try
            {
                return action();
            }
            finally
            {
            }
        }

        internal virtual void RefreshPathNodesAfterPreviewCommit()
        {
            if (PathNodes?.Count == 0)
                return;

            try
            {
                using var path = GetPath();
                PathNodes.Clear();

                if (path == null || path.IsEmpty)
                    return;

                using var iter = path.CreateRawIterator();
                var points = new SKPoint[4];
                SKPathVerb verb;

                while ((verb = iter.Next(points)) != SKPathVerb.Done)
                {
                    SKPoint localPos = verb switch
                    {
                        SKPathVerb.Move => points[0],
                        SKPathVerb.Line => points[1],
                        SKPathVerb.Quad => points[2],
                        SKPathVerb.Cubic => points[3],
                        SKPathVerb.Conic => points[2],
                        _ => SKPoint.Empty
                    };

                    if (verb == SKPathVerb.Close || localPos.IsEmpty)
                        continue;

                    if (PathNodes.Count > 0)
                    {
                        var last = PathNodes[^1];
                        float dx = localPos.X - last.X;
                        float dy = localPos.Y - last.Y;
                        if (dx * dx + dy * dy < 1e-6f)
                            continue;
                    }

                    PathNodes.Add(localPos);

                    if (PathNodes.Count > 2)
                    {
                        var first = PathNodes[0];
                        var last = PathNodes[^1];
                        float dx = last.X - first.X;
                        float dy = last.Y - first.Y;
                        if (dx * dx + dy * dy < 1e-6f)
                            PathNodes.RemoveAt(PathNodes.Count - 1);
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Flatten() 返回的元素数量。叶子图形固定返回 1。
        /// 容器图形（DrawCombination/DrawingGroup）重写此属性，使用懒缓存避免 O(n) 全量遍历。
        /// </summary>
        public virtual int FlattenCount => 1;

        //private SKPoint _skewCenter = new SKPoint(0, 0);
        //public SKPoint SkewCenter { get => _skewCenter; set => _skewCenter = value; }

        internal bool TryMarkSpatialQuery(long stamp)
        {
            if (SpatialQueryStamp == stamp)
            {
                return false;
            }

            SpatialQueryStamp = stamp;
            return true;
        }

        #region --DrawObjectMemento --------------------------------
        public virtual IShapeMemento CaptureSnapshot()
        {
            return new DrawObjectMemento(this);
        }

        /// <summary>
        /// 基类状态快照：捕获/恢复所有 DrawObject 通用属性（Points、变换属性等）。
        /// 子类可继承此快照并重写 <see cref="RestoreDerived"/> 以捕获/恢复特有属性。
        /// </summary>
        protected class DrawObjectMemento : IShapeMemento
        {
            protected readonly DrawObject Shape;

            // 几何属性
            protected readonly List<SKPoint> _points;
            // 变换属性
            private readonly TransformCommandSnapshot _transformSnapshot;

            public DrawObjectMemento(DrawObject shape)
            {
                Shape = shape;

                // 手动复制 Points，避免 LINQ ToList 的额外开销
                if (shape.Points != null)
                {
                    _points = new List<SKPoint>(shape.Points.Count);
                    for (int i = 0; i < shape.Points.Count; i++)
                        _points.Add(shape.Points[i]);
                }
                else
                {
                    _points = new List<SKPoint>();
                }

                _transformSnapshot = shape.CaptureTransformCommandSnapshot();
            }

            public virtual void Restore()
            {
                RestoreGeometry();
                RestoreTransform();
                RestoreDerived();
            }

            /// <summary>
            /// 恢复几何属性（Points）。
            /// 先恢复 Points 以建立基础几何，再通过 UpdateSetProperty 同步内部派生状态。
            /// 对于 DrawCircle 等特殊图形，子类可重写此方法直接赋值而不走 UpdateSetProperty。
            /// </summary>
            protected virtual void RestoreGeometry()
            {
                if (_points != null && _points.Count > 0)
                {
                    // 创建副本传入 UpdateSetProperty，避免 Shape 持有 Snapshot 的列表引用
                    var pointsCopy = new List<SKPoint>(_points.Count);
                    for (int i = 0; i < _points.Count; i++)
                        pointsCopy.Add(_points[i]);
                    Shape.UpdateSetProperty(pointsCopy);
                }
                else
                {
                    Shape.Points = _points;
                }
            }

            /// <summary>
            /// 恢复变换属性。在 RestoreGeometry 之后调用，
            /// 确保 UpdateSetProperty 重算的值可被显式捕获的值覆盖。
            /// 统一复用矩阵快照恢复逻辑，避免命令快照与 memento
            /// 在矩阵时代出现状态不一致。
            /// </summary>
            protected virtual void RestoreTransform()
            {
                Shape.RestoreTransformCommandSnapshot(_transformSnapshot);
            }

            /// <summary>
            /// 恢复子类特有属性。子类重写此方法以恢复其特有状态。
            /// 基类实现为空，遵循开闭原则。
            /// </summary>
            protected virtual void RestoreDerived() { }
        }
        #endregion


        /// <summary>
        /// 设置内部矩阵（供子类使用）
        /// </summary>
        protected void SetMatrixInternal(SKMatrix matrix)
        {
            _matrix = matrix;
            OnCommittedMatrixChanged();
            SyncCommittedBoundsFromMatrix();
        }

        /// <summary>
        /// 重置倾斜属性（供子类使用）
        /// </summary>
        protected void ResetSkewProperties()
        {
            ScaleX = 1;
            ScaleY = 1;
            Rotation = 0;
            _skewTanX = 0;
            _skewTanY = 0;
            SkewX = 0;
            SkewY = 0;
        }
    }
}







