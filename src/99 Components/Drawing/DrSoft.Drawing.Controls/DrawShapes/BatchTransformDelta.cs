using System.Linq;
using DrSoft.Drawing.Controls.Tools;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.DrawShapes;

internal readonly record struct BatchTransformDelta(
    SKMatrix WorldDelta,
    BatchTransformIntent Intent,
    ControlPointType HandleType,
    SKPoint AnchorWorldPoint,
    SKRect SourceBounds,
    float ScaleX,
    float ScaleY);

internal enum BatchTransformIntent
{
    None,
    Resize,
    Rotate,
    Mirror,
    Skew,
    Dimension
}

internal static class BatchTransformHelper
{
    // 多选缩放会把 merged frame 压到固定最小宽高 0.01。
    // 当选区跨度很大时，合法的非零 world scale 可能落到 1e-5 量级。
    // 这里若继续用 1e-4 判“不可分解”，leaf 会在 commit 时被直接跳过，
    // 表现为缩到最小再拉大后个别旋转图形仍留在旧位置。
    private const float MinimumDecomposableScale = 1e-6f;

    // 批量缩放统一传递同一份 world delta，但写回字段时要分两类。
    // 轴对齐叶子用 Width/Height/Center 快路径最稳定；已有旋转或倾斜的叶子才需要仿射提交，
    // 否则世界轴缩放会被误解释成对象局部宽高变化，SkewX/SkewY 会把它污染成横向漂移。
    public static BatchTransformDelta CreateResize(
        SKPoint scaleCenter,
        float scaleX,
        float scaleY,
        ControlPointType handleType,
        SKRect sourceBounds)
    {
        return new BatchTransformDelta(
            SKMatrix.CreateScale(scaleX, scaleY, scaleCenter.X, scaleCenter.Y),
            BatchTransformIntent.Resize,
            handleType,
            scaleCenter,
            sourceBounds,
            scaleX,
            scaleY);
    }

    public static BatchTransformDelta CreateRotation(
        SKPoint rotationCenter,
        float deltaAngle)
    {
        // 旋转提交统一转成“世界坐标系中的一次仿射增量”。
        // 这样单选叶子图形若已经带有 skew/scale，就不会落回
        // “只改 Rotation 字段、重新解释旧 skew 参数”的旧语义。
        var worldRotation = SKMatrix.CreateRotationDegrees(
            -deltaAngle,
            rotationCenter.X,
            rotationCenter.Y);
        var result = new BatchTransformDelta(
            worldRotation,
            BatchTransformIntent.Rotate,
            ControlPointType.None,
            rotationCenter,
            SKRect.Empty,
            1f,
            1f);
        return result;
    }

    public static BatchTransformDelta CreateVerticalMirror(float axisY)
    {
        var worldMirror = SKMatrix.CreateScale(1f, -1f, 0f, axisY);
        return new BatchTransformDelta(
            worldMirror,
            BatchTransformIntent.Mirror,
            ControlPointType.None,
            new SKPoint(0f, axisY),
            SKRect.Empty,
            1f,
            -1f);
    }

    public static BatchTransformDelta CreateContainerResize(
        SKPoint ownerOldCenter,
        SKPoint ownerNewCenter,
        float scaleX,
        float scaleY,
        SKMatrix ownerTransform)
    {
        var origin = TransformScaledPoint(SKPoint.Empty, ownerOldCenter, ownerNewCenter, scaleX, scaleY, ownerTransform);
        var xAxis = TransformScaledPoint(new SKPoint(1f, 0f), ownerOldCenter, ownerNewCenter, scaleX, scaleY, ownerTransform);
        var yAxis = TransformScaledPoint(new SKPoint(0f, 1f), ownerOldCenter, ownerNewCenter, scaleX, scaleY, ownerTransform);

        var matrix = new SKMatrix
        {
            ScaleX = xAxis.X - origin.X,
            SkewX = yAxis.X - origin.X,
            TransX = origin.X,
            SkewY = xAxis.Y - origin.Y,
            ScaleY = yAxis.Y - origin.Y,
            TransY = origin.Y,
            Persp0 = 0f,
            Persp1 = 0f,
            Persp2 = 1f
        };

        return new BatchTransformDelta(
            matrix,
            BatchTransformIntent.Resize,
            ControlPointType.None,
            ownerOldCenter,
            SKRect.Empty,
            scaleX,
            scaleY);
    }

    public static void CommitChildResize(
        DrawObject child,
        SKPoint ownerOldCenter,
        SKPoint ownerNewCenter,
        float scaleX,
        float scaleY,
        SKMatrix ownerTransform)
    {
        if (!child.CanTransform)
            return;

        try
        {
            float childOldWidth = child.Width;
            float childOldHeight = child.Height;
            SKPoint childOldCenter = child.SharpCenter;
            SKPoint childNewCenter = TransformScaledPoint(childOldCenter, ownerOldCenter, ownerNewCenter, scaleX, scaleY, ownerTransform);
            float childNewWidth = childOldWidth * scaleX;
            float childNewHeight = childOldHeight * scaleY;
            bool requiresAffineResizeCommit = NeedsAffineLeafCommit(child) || RequiresAffineResizeCommit(ownerTransform);
            BatchTransformDelta? containerResize = null;

            switch (child)
            {
                case DrawCombination nestedCombo:
                    nestedCombo.CommitScaledBounds(
                        childOldWidth,
                        childOldHeight,
                        childOldCenter,
                        childNewWidth,
                        childNewHeight,
                        childNewCenter);
                    break;
                //case DrawingGroup nestedGroup:
                //    nestedGroup.CommitScaledBounds(
                //        childOldWidth,
                //        childOldHeight,
                //        childOldCenter,
                //        childNewWidth,
                //        childNewHeight,
                //        childNewCenter);
                //    break;
                case DrawingHatch childHatch:
                    // Rotation 保持不变，渲染通过 GetTransformMatrix 施加旋转。
                    ScaleHatchChildrenProportionally(
                        childHatch,
                        ownerOldCenter, ownerNewCenter,
                        scaleX, scaleY, ownerTransform);
                    break;
                default:
                    if (requiresAffineResizeCommit)
                    {
                        containerResize ??= CreateContainerResize(
                            ownerOldCenter,
                            ownerNewCenter,
                            scaleX,
                            scaleY,
                            ownerTransform);
                        CommitLeafWorldTransform(
                            child,
                            containerResize.Value);
                        break;
                    }

                    if (child.Type != DrSoft.Drawing.Model.ShapeType.Point)
                    {
                    }

                    break;
            }
        }
        finally
        {
            // 容器提交后会立即按子图形真实几何回算自身边界；
            // 若子图形仍挂着拖动期 preview，回算会重新读到旧的 Width/Height 缩放语义。
            ClearCommittedPreviewState(child);
        }
    }

    public static bool NeedsAffineLeafCommit(DrawObject drawObject)
    {
        const float epsilon = 0.001f;
        if (drawObject is DrawCombination or DrawingGroup or DrawingHatch)
            return false;

        // 这里只判断“对象是否已经不是纯轴对齐叶子”。
        // 一旦已有 rotation/skew，后续再叠加 resize/rotate 等世界变换时，
        // 直接改 Width/Height/Center 或单独覆写 Rotation 都会破坏当前几何语义，
        // 必须改走完整的 affine commit。
        return Math.Abs(drawObject.Rotation) > epsilon
            || Math.Abs(drawObject.SkewX) > epsilon
            || Math.Abs(drawObject.SkewY) > epsilon;
    }

    public static void CommitLeafWorldTransform(DrawObject drawObject, BatchTransformDelta delta)
    {
        if (drawObject is DrawCombination or DrawingGroup or DrawingHatch)
            return;

        // 这里仅负责把 world delta 一次性落到当前叶子对象上。
        // 生命周期函数上的 preview 清理、路径节点刷新等，仍由外层会话决定是否走既有提交链。
        var current = drawObject.GetTransformMatrix();
        var target = SKMatrix.Concat(delta.WorldDelta, current);

        if (!TryDecomposeTransform(target, out var targetCommit))
            return;

        // 先把 world delta 叠到当前矩阵上，再统一回写到图形字段。
        // 这是这类修复的关键：目标是保持“最终世界矩阵”等价，
        // 而不是分别猜测 Rotation/Scale/Skew 某一个字段该怎么改。

        // 这里提交的是 worldScale * currentMatrix 的仿射结果。
        // 对非轴对齐图形，世界缩放投到本地轴后会混合出旋转、倾斜和轴长度变化；
        // 因此先把本地轴长度烘焙回 Width/Height，再把剩余矩阵分解回 Rotation/Scale/Skew。
        var commit = targetCommit;
        if (drawObject.Type != DrSoft.Drawing.Model.ShapeType.Point)
        {
            float oldWidth = drawObject.Width;
            float oldHeight = drawObject.Height;

            // 目标矩阵的两列分别表示“本地 X/Y 轴在世界坐标中的去向”。
            // 对非点图元，列长度对应的那部分应该优先烘焙进 Width/Height，
            // 否则普通几何缩放会长期残留在 Scale/Skew 字段里，后续旋转、描边和再次编辑都会继续放大误差。
            float geometryScaleX = CalculateColumnLength(target.ScaleX, target.SkewY);
            float geometryScaleY = CalculateColumnLength(target.SkewX, target.ScaleY);
            var updatedWidth = Math.Max(DrawObject.MinDimension, oldWidth * geometryScaleX);
            var updatedHeight = Math.Max(DrawObject.MinDimension, oldHeight * geometryScaleY);
            float appliedGeometryScaleX = oldWidth > 0.001f ? updatedWidth / oldWidth : geometryScaleX;
            float appliedGeometryScaleY = oldHeight > 0.001f ? updatedHeight / oldHeight : geometryScaleY;

            // Width/Height 已经吃掉了一部分局部轴缩放，剩余矩阵必须先把这部分剥离，
            // 再分解回 Rotation/Scale/Skew。
            // 不先剥离就会把同一份缩放同时记在几何尺寸和仿射字段里，形成 double-apply。
            var normalizedTarget = RemoveCommittedGeometryScale(
                target,
                appliedGeometryScaleX,
                appliedGeometryScaleY);
            if (!TryDecomposeTransform(normalizedTarget, out commit))
                return;

            drawObject.ScaleX = commit.ScaleX;
            drawObject.ScaleY = commit.ScaleY;
        }

        drawObject.Rotation = commit.Rotation;
        drawObject.SkewX = commit.SkewX;
        drawObject.SkewY = commit.SkewY;
        drawObject.ScaleAnchorPoint = commit.ScaleAnchorPoint;
        drawObject.SetRotationCenter(commit.RotationCenter);
        drawObject.RotationCenterLocal = drawObject.GetCalculatedRotationCenterLocal();
    }

    private static SKMatrix RemoveCommittedGeometryScale(
        SKMatrix matrix,
        float geometryScaleX,
        float geometryScaleY)
    {
        // 这里按“列归一化”的方式把已经烘焙到 Width/Height 的局部轴缩放从线性矩阵里剥掉。
        // X 轴列由 (ScaleX, SkewY) 组成，Y 轴列由 (SkewX, ScaleY) 组成；
        // 只对对应列做归一化，才能保持剩余的旋转/剪切语义不变。
        float safeScaleX = Math.Abs(geometryScaleX) > MinimumDecomposableScale ? geometryScaleX : 1f;
        float safeScaleY = Math.Abs(geometryScaleY) > MinimumDecomposableScale ? geometryScaleY : 1f;

        var normalized = matrix;
        normalized.ScaleX /= safeScaleX;
        normalized.SkewY /= safeScaleX;
        normalized.SkewX /= safeScaleY;
        normalized.ScaleY /= safeScaleY;
        return normalized;
    }

    private static float CalculateColumnLength(float x, float y)
    {
        var length = MathF.Sqrt(x * x + y * y);
        return length;
    }

    private static void ClearCommittedPreviewState(DrawObject drawObject)
    {

    }

    /// <summary>
    /// 按比例缩放 DrawingHatch 的子填充线。
    /// 通过直接缩放每条填充线的 Width/Height 属性实现，
    /// 渲染管线通过 _localPoints × (Width/_baseWidth) + GetTransformMatrix(Rotation) 正确呈现，
    /// </summary>
    private static void ScaleHatchChildrenProportionally(
        DrawingHatch hatch,
        SKPoint ownerOldCenter,
        SKPoint ownerNewCenter,
        float scaleX,
        float scaleY,
        SKMatrix ownerTransform)
    {
        float hatchScaleX = hatch.Width > 0.001f ? (hatch.Width * scaleX) / hatch.Width : scaleX;
        float hatchScaleY = hatch.Height > 0.001f ? (hatch.Height * scaleY) / hatch.Height : scaleY;

        hatch.SuppressChildPropagation = true;
        try
        {
            foreach (var child in hatch.Children.OfType<DrawPolyLines>())
            {
                if (child.Points == null || child.Points.Count < 2)
                    continue;

                // 新的 SharpCenter：在群组局部坐标系中缩放位置
                var newCenter = TransformScaledPoint(
                    child.SharpCenter, ownerOldCenter, ownerNewCenter,
                    scaleX, scaleY, ownerTransform);

                // 从当前 Points 重算 _localPoints 和 _baseWidth/Height，
                // 确保 Width/Height 变化时缩放比例正确。
                child.UpdateLocalPointsInPlace(child.Points.ToList());

                // 缩放 Width/Height（渲染公式 _localPoints × (W/baseW) 自然按比例缩放几何体）

                // 更新中心点（Points 随 SharpCenter 平移，_localPoints 不变）
                // Rotation 保持不变：GetTransformMatrix 在渲染时正确施加旋转。
            }
        }
        finally
        {
            hatch.SuppressChildPropagation = false;
        }
        hatch.UpdateSetProperty(new List<SKPoint>());
    }

    public static SKPoint TransformScaledPoint(
        SKPoint point,
        SKPoint oldCenter,
        SKPoint newCenter,
        float scaleX,
        float scaleY,
        SKMatrix ownerTransform)
    {
        var ownerLinear = ExtractLinearTransform(ownerTransform);
        if (ownerLinear.TryInvert(out var ownerLinearInverse))
        {
            var relative = new SKPoint(point.X - oldCenter.X, point.Y - oldCenter.Y);
            var local = ownerLinearInverse.MapPoint(relative);
            var scaledLocal = new SKPoint(local.X * scaleX, local.Y * scaleY);
            var newRelative = ownerLinear.MapPoint(scaledLocal);
            return new SKPoint(newCenter.X + newRelative.X, newCenter.Y + newRelative.Y);
        }

        float rotation = MathF.Atan2(ownerLinear.SkewY, ownerLinear.ScaleX) * 180f / MathF.PI;
        float rotationRad = -rotation * MathF.PI / 180f;
        float cos = MathF.Cos(rotationRad);
        float sin = MathF.Sin(rotationRad);

        float dx = point.X - oldCenter.X;
        float dy = point.Y - oldCenter.Y;
        float localX = dx * cos + dy * sin;
        float localY = -dx * sin + dy * cos;
        float scaledLocalX = localX * scaleX;
        float scaledLocalY = localY * scaleY;
        float newDx = scaledLocalX * cos - scaledLocalY * sin;
        float newDy = scaledLocalX * sin + scaledLocalY * cos;

        return new SKPoint(newCenter.X + newDx, newCenter.Y + newDy);
    }

    private static SKMatrix ExtractLinearTransform(SKMatrix matrix)
    {
        return new SKMatrix
        {
            ScaleX = matrix.ScaleX,
            SkewX = matrix.SkewX,
            TransX = 0f,
            SkewY = matrix.SkewY,
            ScaleY = matrix.ScaleY,
            TransY = 0f,
            Persp0 = 0f,
            Persp1 = 0f,
            Persp2 = 1f
        };
    }

    private static bool RequiresAffineResizeCommit(SKMatrix ownerTransform)
    {
        const float epsilon = 0.001f;
        var linear = ExtractLinearTransform(ownerTransform);
        return Math.Abs(linear.ScaleX - 1f) > epsilon
            || Math.Abs(linear.ScaleY - 1f) > epsilon
            || Math.Abs(linear.SkewX) > epsilon
            || Math.Abs(linear.SkewY) > epsilon;
    }

    private static bool TryDecomposeTransform(SKMatrix matrix, out DecomposedTransform transform)
    {
        // GetTransformMatrix 的线性部分顺序为 Rotation * Skew * Scale（ky=0）。
        // A = R * K * S
        //   = [ sx * cosθ,  sy * (kx * cosθ - sinθ) ]
        //     [ sx * sinθ,  sy * (kx * sinθ + cosθ) ]
        float a = matrix.ScaleX;
        float b = matrix.SkewX;
        float c = matrix.SkewY;
        float d = matrix.ScaleY;
        float tx = matrix.TransX;
        float ty = matrix.TransY;

        float scaleX = MathF.Sqrt(a * a + c * c);
        if (scaleX < MinimumDecomposableScale)
        {
            transform = default;
            return false;
        }

        float rotationRad = MathF.Atan2(c, a);
        float rotationCos = MathF.Cos(rotationRad);
        float rotationSin = MathF.Sin(rotationRad);

        float scaleY = d * rotationCos - b * rotationSin;
        if (Math.Abs(scaleY) < MinimumDecomposableScale)
        {
            transform = default;
            return false;
        }

        float shearX;
        if (Math.Abs(rotationCos) > 0.001f)
        {
            shearX = (b / scaleY + rotationSin) / rotationCos;
        }
        else
        {
            shearX = (d / scaleY - rotationCos) / rotationSin;
        }
        float skewRad = MathF.Atan(shearX);

        transform = new DecomposedTransform(
            new SKPoint(tx, ty),
            -rotationRad * 180f / MathF.PI,
            scaleX,
            scaleY,
            skewRad * 180f / MathF.PI,
            0f,
            SKPoint.Empty,
            new SKPoint(tx, ty));
        return true;
    }

    private readonly record struct DecomposedTransform(
        SKPoint Center,
        float Rotation,
        float ScaleX,
        float ScaleY,
        float SkewX,
        float SkewY,
        SKPoint ScaleAnchorPoint,
        SKPoint RotationCenter);
}
