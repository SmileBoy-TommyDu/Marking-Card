using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Algorithm;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using static DrSoft.Drawing.Controls.Rendering.HatchRenderHelper;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    public class DrawRectangle : DrawObject, IHatchable, IRectangleShapeData
    {
        // ── IRectangleShapeData：四角圆角半径（已有公共属性，自动满足接口）──────────
        // CornerRadiusTopLeft/TopRight/BottomRight/BottomLeft 直接暴露给打标卡
        // CenterX/CenterY/ChildShapes 由基类 DrawObject 统一处理
        public float CornerRadiusTopLeft { get; set; } = 0;
        public float CornerRadiusTopRight { get; set; } = 0;
        public float CornerRadiusBottomRight { get; set; } = 0;
        public float CornerRadiusBottomLeft { get; set; } = 0;

        // ── 倒角属性（与圆角互斥，同时存在时优先倒角）──────────────────────
        public float ChamferTopLeft { get; set; } = 0;
        public float ChamferTopRight { get; set; } = 0;
        public float ChamferBottomRight { get; set; } = 0;
        public float ChamferBottomLeft { get; set; } = 0;

        // ── 原始圆角/倒角设定值（用户通过 UI 设定的值，缩放时不丢失）──────────
        // 当矩形缩小时，圆角/倒角被 LimitCornerRadii/LimitChamferLengths 限制，
        // 但原始值保留在此；放大时从原始值恢复（不超过当前尺寸限制）。
        private float _originalCornerRadiusTopLeft = 0;
        private float _originalCornerRadiusTopRight = 0;
        private float _originalCornerRadiusBottomRight = 0;
        private float _originalCornerRadiusBottomLeft = 0;
        private float _originalChamferTopLeft = 0;
        private float _originalChamferTopRight = 0;
        private float _originalChamferBottomRight = 0;
        private float _originalChamferBottomLeft = 0;
        // 标记原始值是否已初始化（首次设置圆角/倒角时记录）
        private bool _hasOriginalCornerValues = false;
        private float _localWidth = MinDimension;
        private float _localHeight = MinDimension;

        public bool hasRoundedCorners { get; set; } = false;
        public bool hasChamfer { get; set; } = false;
        public List<Point2D> Vertices { get; set; } = new List<Point2D>();
        public float CornerRadius
        {
            get => CornerRadiusTopLeft; // 返回左上角半径作为代表
            set
            {
                CornerRadiusTopLeft = value;
                CornerRadiusTopRight = value;
                CornerRadiusBottomRight = value;
                CornerRadiusBottomLeft = value;
            }
        }

        public float ChamferRadius
        {
            get => ChamferTopLeft; // 返回左上角倒角半径作为代表
            set
            {
                ChamferTopLeft = value;
                ChamferTopRight = value;
                ChamferBottomRight = value;
                ChamferBottomLeft = value;
            }
        }

        public override List<Point2D> OutlinePoints
        {
            get
            {
                if (hasChamfer)
                {
                    return GetChamferVertices().Select(it => new Point2D(it.X, it.Y)).ToList();
                }
                return GetVertices().Select(it => new Point2D(it.X, it.Y)).ToList();
            }
            set => throw new NotImplementedException();
        }

        private float LocalWidth => _localWidth;

        private float LocalHeight => _localHeight;

        private static SKRect CreateLocalBounds(float width, float height)
        {
            float clampedWidth = Math.Max(width, 0f);
            float clampedHeight = Math.Max(height, 0f);
            return new SKRect(-clampedWidth / 2f, clampedHeight / 2f, clampedWidth / 2f, -clampedHeight / 2f);
        }

        private SKRect GetRectangleLocalBounds()
        {
            return CreateLocalBounds(LocalWidth, LocalHeight);
        }

        private SKRect GetInsetLocalBounds(float margin)
        {
            var bounds = GetRectangleLocalBounds();
            return new SKRect(
                bounds.Left + margin,
                bounds.Top - margin,
                bounds.Right - margin,
                bounds.Bottom + margin);
        }

        private void SetLocalGeometry(float width, float height)
        {
            _localWidth = Math.Max(Math.Abs(width), 0f);
            _localHeight = Math.Max(Math.Abs(height), 0f);
            RecalcCornerChamferFromOriginals();
        }

        private void SyncDiagonalPointsFromMatrix()
        {
            var localBounds = GetRectangleLocalBounds();
            var transformMatrix = GetTransformMatrix();
            var p0 = transformMatrix.MapPoint(new SKPoint(localBounds.Left, localBounds.Bottom));
            var p1 = transformMatrix.MapPoint(new SKPoint(localBounds.Right, localBounds.Top));
            Points = new List<SKPoint> { p0, p1 };
        }

        internal override List<IShape> CreateCurveChildren()
        {
            if (IsChamferRadiusRectangle())
            {
                var worldVertices = GetChamferVertices();
                var polyLine = new DrawPolyLines(worldVertices.Select(it => new Point2D(it.X, it.Y)).ToList())
                {
                    IsClosed = true,
                    Pen = Pen,
                    Name = $"{Name}_折线"
                };
                return new List<IShape> { polyLine };
            }

            var matrix = GetTransformMatrix();

            var localBounds = GetRectangleLocalBounds();
            float left = localBounds.Left;
            float right = localBounds.Right;
            float bottom = localBounds.Bottom;
            float top = localBounds.Top;

            var adjustedCorners = LimitCornerRadii(
                left,
                top,
                right,
                bottom,
                Math.Max(0, CornerRadiusTopLeft),
                Math.Max(0, CornerRadiusTopRight),
                Math.Max(0, CornerRadiusBottomRight),
                Math.Max(0, CornerRadiusBottomLeft));
            float tl = adjustedCorners.TopLeft;
            float tr = adjustedCorners.TopRight;
            float br = adjustedCorners.BottomRight;
            float bl = adjustedCorners.BottomLeft;

            bool hasCorner = tl > 0.001f || tr > 0.001f || br > 0.001f || bl > 0.001f;
            SKPoint ToWorld(SKPoint local) => matrix.MapPoint(local);

            if (!hasCorner)
            {
                var corners = new List<Point2D>
                {
                    new Point2D(ToWorld(new SKPoint(left, top)).X, ToWorld(new SKPoint(left, top)).Y),
                    new Point2D(ToWorld(new SKPoint(right, top)).X, ToWorld(new SKPoint(right, top)).Y),
                    new Point2D(ToWorld(new SKPoint(right, bottom)).X, ToWorld(new SKPoint(right, bottom)).Y),
                    new Point2D(ToWorld(new SKPoint(left, bottom)).X, ToWorld(new SKPoint(left, bottom)).Y),
                };
                var polyLine = new DrawPolyLines(corners)
                {
                    IsClosed = true,
                    Pen = Pen,
                    Name = $"{Name}_折线"
                };
                return new List<IShape> { polyLine };
            }

            var anchors = new List<SKPoint>();
            var handles = new List<SKPoint>();

            void AppendArc(SKPoint centerLocal, float radius, float startDeg, float endDeg)
            {
                float startRad = startDeg * MathF.PI / 180f;
                float endRad = endDeg * MathF.PI / 180f;
                float totalSweep = endRad - startRad;
                while (totalSweep > MathF.PI) totalSweep -= 2 * MathF.PI;
                while (totalSweep < -MathF.PI) totalSweep += 2 * MathF.PI;

                int numSegs = Math.Max(1, (int)MathF.Ceiling(MathF.Abs(totalSweep) / (MathF.PI / 2 + 0.001f)));
                float segSweep = totalSweep / numSegs;
                float sign = segSweep >= 0 ? 1f : -1f;

                for (int i = 0; i < numSegs; i++)
                {
                    float alpha = startRad + i * segSweep;
                    float beta = alpha + segSweep;
                    SKPoint p0Local = new(centerLocal.X + radius * MathF.Cos(alpha), centerLocal.Y + radius * MathF.Sin(alpha));
                    SKPoint p1Local = new(centerLocal.X + radius * MathF.Cos(beta), centerLocal.Y + radius * MathF.Sin(beta));
                    SKPoint p0 = ToWorld(p0Local);
                    SKPoint p1 = ToWorld(p1Local);
                    float h = (4f / 3f) * MathF.Tan(MathF.Abs(segSweep) / 4f) * radius;
                    SKPoint outH = ToWorld(new SKPoint(p0Local.X + sign * h * (-MathF.Sin(alpha)), p0Local.Y + sign * h * MathF.Cos(alpha)));
                    SKPoint inH = ToWorld(new SKPoint(p1Local.X - sign * h * (-MathF.Sin(beta)), p1Local.Y - sign * h * MathF.Cos(beta)));

                    if (anchors.Count == 0)
                    {
                        anchors.Add(p0);
                        handles.Add(outH);
                        handles.Add(SKPoint.Empty);
                    }
                    else
                    {
                        handles[handles.Count - 2] = outH;
                    }

                    anchors.Add(p1);
                    handles.Add(SKPoint.Empty);
                    handles.Add(inH);
                }
            }

            void AppendLine(SKPoint aLocal, SKPoint bLocal)
            {
                SKPoint a = ToWorld(aLocal);
                SKPoint b = ToWorld(bLocal);
                SKPoint cp1 = new(
                    a.X + (b.X - a.X) / 3f,
                    a.Y + (b.Y - a.Y) / 3f);
                SKPoint cp2 = new(
                    a.X + (b.X - a.X) * 2f / 3f,
                    a.Y + (b.Y - a.Y) * 2f / 3f);

                if (anchors.Count == 0)
                {
                    anchors.Add(a);
                    handles.Add(cp1);
                    handles.Add(SKPoint.Empty);
                }
                else
                {
                    handles[handles.Count - 2] = cp1;
                }

                anchors.Add(b);
                handles.Add(SKPoint.Empty);
                handles.Add(cp2);
            }

            if (right - tr > left + tl)
                AppendLine(new SKPoint(left + tl, top), new SKPoint(right - tr, top));

            if (tr > 0.001f)
                AppendArc(new SKPoint(right - tr, top - tr), tr, 90f, 0f);

            if (top - tr > bottom + br)
                AppendLine(new SKPoint(right, top - tr), new SKPoint(right, bottom + br));

            if (br > 0.001f)
                AppendArc(new SKPoint(right - br, bottom + br), br, 0f, -90f);

            if (right - br > left + bl)
                AppendLine(new SKPoint(right - br, bottom), new SKPoint(left + bl, bottom));

            if (bl > 0.001f)
                AppendArc(new SKPoint(left + bl, bottom + bl), bl, -90f, -180f);

            if (top - tl > bottom + bl)
                AppendLine(new SKPoint(left, bottom + bl), new SKPoint(left, top - tl));

            if (tl > 0.001f)
                AppendArc(new SKPoint(left + tl, top - tl), tl, 180f, 90f);

            if (anchors.Count >= 2)
            {
                var first = anchors[0];
                var last = anchors[anchors.Count - 1];
                float dx = first.X - last.X;
                float dy = first.Y - last.Y;
                if (dx * dx + dy * dy < 1e-4f)
                {
                    handles[1] = handles[handles.Count - 1];
                    anchors.RemoveAt(anchors.Count - 1);
                    handles.RemoveAt(handles.Count - 1);
                    handles.RemoveAt(handles.Count - 1);
                }
            }

            var cubic = new DrawCubicPath
            {
                IsClosed = true,
                Pen = Pen,
                Name = $"{Name}_曲线"
            };
            cubic.Initialize(anchors, handles);
            return new List<IShape> { cubic };
        }

        public SKPoint[] GetVertices()
        {
            var localBounds = GetLocalBounds();

            // 获取四个角点
            var vertices = new[]
            {
                new SKPoint(localBounds.Left, localBounds.Top),
                new SKPoint(localBounds.Right, localBounds.Top),
                new SKPoint(localBounds.Right, localBounds.Bottom),
                new SKPoint(localBounds.Left, localBounds.Bottom)
            };

            // 应用变换矩阵
            var transformMatrix = GetTransformMatrix();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = transformMatrix.MapPoint(vertices[i]);
            }

            return vertices;
        }


        public bool IsCornerRadiusRectangle()
        {
            hasRoundedCorners = CornerRadiusTopLeft > 0 || CornerRadiusTopRight > 0 || CornerRadiusBottomRight > 0 || CornerRadiusBottomLeft > 0;
            return hasRoundedCorners;
        }

        public bool IsChamferRadiusRectangle()
        {
            hasChamfer = ChamferTopLeft > 0 || ChamferTopRight > 0 || ChamferBottomRight > 0 || ChamferBottomLeft > 0;
            return hasChamfer;
        }

        /// <summary>
        /// 获取倒角矩形的8个顶点（世界坐标），按逆时针顺序：
        /// 上边左端、上边右端、右上倒角点、右边下端、右下倒角点、下边左端、左下倒角点、左边上端。
        /// 无倒角的角：两条边端点重合，仍输出8个点保持索引一致。
        /// </summary>
        public SKPoint[] GetChamferVertices()
        {
            var localBounds = GetRectangleLocalBounds();
            float left = localBounds.Left;
            float right = localBounds.Right;
            float bottom = localBounds.Bottom;
            float top = localBounds.Top;

            var adj = LimitChamferLengths(left, top, right, bottom,
                ChamferTopLeft, ChamferTopRight, ChamferBottomRight, ChamferBottomLeft);

            var localVerts = new[]
            {
                new SKPoint(left + adj.TopLeft, top),
                new SKPoint(right - adj.TopRight, top),
                new SKPoint(right, top - adj.TopRight),
                new SKPoint(right, bottom + adj.BottomRight),
                new SKPoint(right - adj.BottomRight, bottom),
                new SKPoint(left + adj.BottomLeft, bottom),
                new SKPoint(left, bottom + adj.BottomLeft),
                new SKPoint(left, top - adj.TopLeft),
            };

            var transformMatrix = GetTransformMatrix();
            for (int i = 0; i < localVerts.Length; i++)
                localVerts[i] = transformMatrix.MapPoint(localVerts[i]);

            return localVerts;
        }

        public override SKPath GetPath()
        {
            var path = new SKPath();
            FillPath(path);
            return path;
        }

        protected override void FillPath(SKPath path)
        {
            FillPath(path, LocalWidth, LocalHeight);
        }

        public override SKRect GetLocalBounds()
        {
            return GetRectangleLocalBounds();
        }

        private void FillPath(SKPath path, float width, float height)
        {
            // 本地坐标系：Y轴向上为正（与世界坐标一致）
            // 较小的Y值在下（bottom），较大的Y值在上（top）
            float left = -width / 2;
            float bottom = -height / 2;   // 较小的Y值（更下方）
            float right = width / 2;
            float top = height / 2;       // 较大的Y值（更上方）

            if (!IsChamferRadiusRectangle() && !IsCornerRadiusRectangle())
            {
                // 直角矩形
                path.AddRect(new SKRect(left, bottom, right, top)); // 注意：skia中rect参数顺序是left,top,right,bottom
            }
            else if (IsChamferRadiusRectangle())
            {
                // 倒角矩形（优先于圆角）
                var adj = LimitChamferLengths(left, top, right, bottom,
                    ChamferTopLeft, ChamferTopRight, ChamferBottomRight, ChamferBottomLeft);

                path.MoveTo(left + adj.TopLeft, top);

                // 上边
                path.LineTo(right - adj.TopRight, top);
                // 右上角倒角
                if (adj.TopRight > 0) path.LineTo(right, top - adj.TopRight);

                // 右边
                path.LineTo(right, bottom + adj.BottomRight);
                // 右下角倒角
                if (adj.BottomRight > 0) path.LineTo(right - adj.BottomRight, bottom);

                // 下边
                path.LineTo(left + adj.BottomLeft, bottom);
                // 左下角倒角
                if (adj.BottomLeft > 0) path.LineTo(left, bottom + adj.BottomLeft);

                // 左边
                path.LineTo(left, top - adj.TopLeft);
                // 左上角倒角
                if (adj.TopLeft > 0) path.LineTo(left + adj.TopLeft, top);

                path.Close();
            }
            else
            {
                // 圆角矩形，每个角独立控制
                var adjustedCorners = LimitCornerRadii(left, top, right, bottom,
                    CornerRadiusTopLeft, CornerRadiusTopRight,
                    CornerRadiusBottomRight, CornerRadiusBottomLeft);

                path.MoveTo(left + adjustedCorners.TopLeft, top);

                // 上边线
                path.LineTo(right - adjustedCorners.TopRight, top);

                // 右上角
                if (adjustedCorners.TopRight > 0)
                {
                    path.ArcTo(
                        new SKRect(right - adjustedCorners.TopRight * 2, top - adjustedCorners.TopRight * 2, right, top),
                        90, -90, false);
                }

                // 右边线
                path.LineTo(right, bottom + adjustedCorners.BottomRight);

                // 右下角
                if (adjustedCorners.BottomRight > 0)
                {
                    path.ArcTo(
                        new SKRect(right - adjustedCorners.BottomRight * 2, bottom, right, bottom + adjustedCorners.BottomRight * 2),
                        0, -90, false);
                }

                // 下边线
                path.LineTo(left + adjustedCorners.BottomLeft, bottom);

                // 左下角
                if (adjustedCorners.BottomLeft > 0)
                {
                    path.ArcTo(
                        new SKRect(left, bottom, left + adjustedCorners.BottomLeft * 2, bottom + adjustedCorners.BottomLeft * 2),
                        270, -90, false);
                }

                // 左边线
                path.LineTo(left, top - adjustedCorners.TopLeft);

                // 左上角
                if (adjustedCorners.TopLeft > 0)
                {
                    path.ArcTo(
                        new SKRect(left, top - adjustedCorners.TopLeft * 2, left + adjustedCorners.TopLeft * 2, top),
                        180, -90, false);
                }

                path.Close();
            }
        }

        // 限制圆角半径，防止相邻圆角重叠
        // 仅返回限制后的值，不修改属性（属性由 RecalcCornerChamferFromOriginals 统一管理）
        private (float TopLeft, float TopRight, float BottomRight, float BottomLeft) LimitCornerRadii(
            float left, float top, float right, float bottom,
            float cornerRadiusTopLeft, float cornerRadiusTopRight,
            float cornerRadiusBottomRight, float cornerRadiusBottomLeft)
        {
            var width = right - left;
            var height = top - bottom;

            var maxTopRadius = width / 2;
            var maxBottomRadius = width / 2;
            var maxLeftRadius = height / 2;
            var maxRightRadius = height / 2;

            return (
                TopLeft: Math.Min(cornerRadiusTopLeft, Math.Min(maxLeftRadius, maxTopRadius)),
                TopRight: Math.Min(cornerRadiusTopRight, Math.Min(maxRightRadius, maxTopRadius)),
                BottomRight: Math.Min(cornerRadiusBottomRight, Math.Min(maxRightRadius, maxBottomRadius)),
                BottomLeft: Math.Min(cornerRadiusBottomLeft, Math.Min(maxLeftRadius, maxBottomRadius))
            );
        }

        // 限制倒角长度，防止相邻倒角重叠
        // 仅返回限制后的值，不修改属性（属性由 RecalcCornerChamferFromOriginals 统一管理）
        private (float TopLeft, float TopRight, float BottomRight, float BottomLeft) LimitChamferLengths(
            float left, float top, float right, float bottom,
            float chamferTopLeft, float chamferTopRight,
            float chamferBottomRight, float chamferBottomLeft)
        {
            var width = right - left;
            var height = top - bottom;

            var maxTop = width / 2;
            var maxBottom = width / 2;
            var maxLeft = height / 2;
            var maxRight = height / 2;

            return (
                TopLeft: Math.Min(chamferTopLeft, Math.Min(maxLeft, maxTop)),
                TopRight: Math.Min(chamferTopRight, Math.Min(maxRight, maxTop)),
                BottomRight: Math.Min(chamferBottomRight, Math.Min(maxRight, maxBottom)),
                BottomLeft: Math.Min(chamferBottomLeft, Math.Min(maxLeft, maxBottom))
            );
        }

        /// <summary>
        /// 根据 Width/Height 变化，从原始设定值重新计算圆角和倒角半径。
        /// 放大时恢复到原始设定值（不超过当前尺寸限制），缩小时限制到当前尺寸允许的最大值。
        /// </summary>
        private void RecalcCornerChamferFromOriginals()
        {
            if (!_hasOriginalCornerValues) return;

            var localBounds = GetRectangleLocalBounds();
            float left = localBounds.Left;
            float right = localBounds.Right;
            float bottom = localBounds.Bottom;
            float top = localBounds.Top;

            // 从原始值重新计算，应用当前尺寸限制
            if (_originalChamferTopLeft > 0 || _originalChamferTopRight > 0 ||
                _originalChamferBottomRight > 0 || _originalChamferBottomLeft > 0)
            {
                var adj = LimitChamferLengths(left, top, right, bottom,
                    _originalChamferTopLeft, _originalChamferTopRight,
                    _originalChamferBottomRight, _originalChamferBottomLeft);
                ChamferTopLeft = adj.TopLeft;
                ChamferTopRight = adj.TopRight;
                ChamferBottomRight = adj.BottomRight;
                ChamferBottomLeft = adj.BottomLeft;
            }
            else if (_originalCornerRadiusTopLeft > 0 || _originalCornerRadiusTopRight > 0 ||
                     _originalCornerRadiusBottomRight > 0 || _originalCornerRadiusBottomLeft > 0)
            {
                var adj = LimitCornerRadii(left, top, right, bottom,
                    _originalCornerRadiusTopLeft, _originalCornerRadiusTopRight,
                    _originalCornerRadiusBottomRight, _originalCornerRadiusBottomLeft);
                CornerRadiusTopLeft = adj.TopLeft;
                CornerRadiusTopRight = adj.TopRight;
                CornerRadiusBottomRight = adj.BottomRight;
                CornerRadiusBottomLeft = adj.BottomLeft;
            }
        }

        internal void AdjustCornerRadius(RoundMode mode, double topLeft, double topRight, double bottomRight, double bottomLeft)
        {
            if (IsLocked)
                return;

            // Round/chamfer are mutually exclusive. Clear stale chamfer values so
            // subsequent renders and hashes reflect the newly selected corner type.
            ChamferTopLeft = 0;
            ChamferTopRight = 0;
            ChamferBottomRight = 0;
            ChamferBottomLeft = 0;

            float minDim = Math.Min(LocalWidth, LocalHeight);
            CornerRadiusTopLeft = mode == RoundMode.Percent ? (float)(topLeft / 100.0 * minDim) : (float)topLeft;
            CornerRadiusTopRight = mode == RoundMode.Percent ? (float)(topRight / 100.0 * minDim) : (float)topRight;
            CornerRadiusBottomRight = mode == RoundMode.Percent ? (float)(bottomRight / 100.0 * minDim) : (float)bottomRight;
            CornerRadiusBottomLeft = mode == RoundMode.Percent ? (float)(bottomLeft / 100.0 * minDim) : (float)bottomLeft;

            // 对设定值应用当前尺寸限制，将限制后的值记录为原始值
            var localBounds = GetRectangleLocalBounds();
            float left = localBounds.Left, right = localBounds.Right, bottom = localBounds.Bottom, top = localBounds.Top;
            var limited = LimitCornerRadii(left, top, right, bottom,
                CornerRadiusTopLeft, CornerRadiusTopRight,
                CornerRadiusBottomRight, CornerRadiusBottomLeft);
            CornerRadiusTopLeft = limited.TopLeft;
            CornerRadiusTopRight = limited.TopRight;
            CornerRadiusBottomRight = limited.BottomRight;
            CornerRadiusBottomLeft = limited.BottomLeft;

            // 记录限制后的原始圆角值，缩放时用于恢复
            _originalCornerRadiusTopLeft = limited.TopLeft;
            _originalCornerRadiusTopRight = limited.TopRight;
            _originalCornerRadiusBottomRight = limited.BottomRight;
            _originalCornerRadiusBottomLeft = limited.BottomLeft;
            _originalChamferTopLeft = 0;
            _originalChamferTopRight = 0;
            _originalChamferBottomRight = 0;
            _originalChamferBottomLeft = 0;
            _hasOriginalCornerValues = true;
        }

        internal void AdjustChamfer(RoundMode mode, double topLeft, double topRight, double bottomRight, double bottomLeft)
        {
            if (IsLocked)
                return;

            // Round/chamfer are mutually exclusive. Clear stale round-corner values
            // before applying chamfer values.
            CornerRadiusTopLeft = 0;
            CornerRadiusTopRight = 0;
            CornerRadiusBottomRight = 0;
            CornerRadiusBottomLeft = 0;

            float minDim = Math.Min(LocalWidth, LocalHeight);
            ChamferTopLeft = mode == RoundMode.Percent ? (float)(topLeft / 100.0 * minDim) : (float)topLeft;
            ChamferTopRight = mode == RoundMode.Percent ? (float)(topRight / 100.0 * minDim) : (float)topRight;
            ChamferBottomRight = mode == RoundMode.Percent ? (float)(bottomRight / 100.0 * minDim) : (float)bottomRight;
            ChamferBottomLeft = mode == RoundMode.Percent ? (float)(bottomLeft / 100.0 * minDim) : (float)bottomLeft;

            // 对设定值应用当前尺寸限制，将限制后的值记录为原始值
            var localBounds = GetRectangleLocalBounds();
            float left = localBounds.Left, right = localBounds.Right, bottom = localBounds.Bottom, top = localBounds.Top;
            var limited = LimitChamferLengths(left, top, right, bottom,
                ChamferTopLeft, ChamferTopRight,
                ChamferBottomRight, ChamferBottomLeft);
            ChamferTopLeft = limited.TopLeft;
            ChamferTopRight = limited.TopRight;
            ChamferBottomRight = limited.BottomRight;
            ChamferBottomLeft = limited.BottomLeft;

            // 记录限制后的原始倒角值，缩放时用于恢复
            _originalChamferTopLeft = limited.TopLeft;
            _originalChamferTopRight = limited.TopRight;
            _originalChamferBottomRight = limited.BottomRight;
            _originalChamferBottomLeft = limited.BottomLeft;
            _originalCornerRadiusTopLeft = 0;
            _originalCornerRadiusTopRight = 0;
            _originalCornerRadiusBottomRight = 0;
            _originalCornerRadiusBottomLeft = 0;
            _hasOriginalCornerValues = true;
        }

        // 无参构造函数，供 AutoMapper 映射使用
        public DrawRectangle() : base()
        {
            UId = UniqueIdGenerator.NextId();
            Points = new List<SKPoint>();
            Type = ShapeType.Rectangle;
            IsClockwise = true; // 默认顺时针
        }

        /// <summary>
        /// 创建矩形（接收两个对角点，自动计算四个顶点）
        /// </summary>
        public DrawRectangle(List<SKPoint> points, List<float>? CornerPara = null, bool isDxf = false) : this()
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("绘制矩形需要2个点!");
            }

            if (isDxf)
            {
                InitializeFromDxfPoints(points, CornerPara);
                return;
            }

            UpdateSetProperty(points);
        }
        public DrawRectangle(List<Point2D> points, List<float>? CornerPara = null, bool isDxf = false) : this(points.Select(p => new SKPoint(p.X, p.Y)).ToList(), CornerPara, isDxf) { }

        public override void UpdateSetProperty(List<SKPoint> points)
        {
            if (points == null || points.Count < 2)
            {
                throw new ArgumentException("绘制矩形需要2个点!");
            }

            // 计算
            Type = ShapeType.Rectangle;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minY = float.MaxValue;
            float maxY = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                var point = points[i];
                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
            }

            SetLocalGeometry(maxX - minX, maxY - minY);
            SyncDiagonalPointsFromMatrix();
        }

        /// <summary>
        /// DXF 导入初始化：从已变换的绝对世界坐标点反算矩形属性。
        /// 支持 2 个对角点或 4 个角点：
        ///   - 2 个对角点：无旋转，直接用 AABB 计算宽高
        ///   - 4 个角点：从第一条边反算旋转角度，再反旋转求宽高
        /// </summary>
        private void InitializeFromDxfPoints(List<SKPoint> points, List<float>? CornerPara)
        {
            Points = points;
            Type = ShapeType.Rectangle;

            if (points.Count >= 4)
            {
                float cx = 0f;
                float cy = 0f;
                for (int i = 0; i < 4; i++)
                {
                    cx += points[i].X;
                    cy += points[i].Y;
                }

                var center = new SKPoint(cx / 4f, cy / 4f);
                var widthVector = points[1] - points[0];
                var heightVector = points[2] - points[1];
                SetLocalGeometry(widthVector.Length, heightVector.Length);

                float rotationDegrees = (float)(Math.Atan2(widthVector.Y, widthVector.X) * 180.0 / Math.PI);
                var matrix = SKMatrix.CreateRotationDegrees(rotationDegrees, 0, 0)
                    .PostConcat(SKMatrix.CreateTranslation(center.X, center.Y));
                RestoreTransformCommandSnapshot(new TransformCommandSnapshot(
                    matrix,
                    rotationDegrees,
                    1f,
                    1f,
                    0f,
                    0f,
                    center,
                    SKPoint.Empty,
                    SKPoint.Empty));
            }
            else
            {
                UpdateSetProperty(points);
            }

            if (CornerPara != null && CornerPara.Count >= 4)
            {
                // 记录为原始设定值并按当前尺寸 clamp，
                // 保证后续缩放/编辑时圆角行为与 UI 设定的矩形一致
                _originalCornerRadiusTopLeft = Math.Max(CornerPara[0], 0f);
                _originalCornerRadiusTopRight = Math.Max(CornerPara[1], 0f);
                _originalCornerRadiusBottomRight = Math.Max(CornerPara[2], 0f);
                _originalCornerRadiusBottomLeft = Math.Max(CornerPara[3], 0f);
                _hasOriginalCornerValues = true;
                RecalcCornerChamferFromOriginals();
                IsCornerRadiusRectangle();
            }

            return;
        }

        protected override void OnCommittedMatrixChanged()
        {
            SyncDiagonalPointsFromMatrix();
        }

        public override bool HitTest(SKPoint p, float tol = 6.0f)
        {
            if (Points == null || Points.Count < 2)
                return false;
            return base.HitTest(p, tol);
        }

        public override bool IntersectsWith(SKRect rect)
        {
            if (Points == null || Points.Count < 2)
                return false;

            return base.IntersectsWith(rect);
        }

        public bool HasNonRectangularCorners()
        {
            return IsChamferRadiusRectangle() || IsCornerRadiusRectangle();
        }

        public override IShape Clone()
        {
            var clonedPoints = new List<SKPoint>();
            if (Points != null)
            {
                foreach (var point in Points)
                {
                    clonedPoints.Add(point);
                }
            }

            var clone = new DrawRectangle()
            {
                HatchParamInfo = HatchParamInfo,
                CornerRadiusTopLeft = CornerRadiusTopLeft,
                CornerRadiusTopRight = CornerRadiusTopRight,
                CornerRadiusBottomRight = CornerRadiusBottomRight,
                CornerRadiusBottomLeft = CornerRadiusBottomLeft,
                ChamferTopLeft = ChamferTopLeft,
                ChamferTopRight = ChamferTopRight,
                ChamferBottomRight = ChamferBottomRight,
                ChamferBottomLeft = ChamferBottomLeft,
            };

            clone.Points = clonedPoints;
            clone._localWidth = _localWidth;
            clone._localHeight = _localHeight;

            // 复制原始圆角/倒角设定值，确保克隆对象缩放时也能正确恢复
            clone._originalCornerRadiusTopLeft = _originalCornerRadiusTopLeft;
            clone._originalCornerRadiusTopRight = _originalCornerRadiusTopRight;
            clone._originalCornerRadiusBottomRight = _originalCornerRadiusBottomRight;
            clone._originalCornerRadiusBottomLeft = _originalCornerRadiusBottomLeft;
            clone._originalChamferTopLeft = _originalChamferTopLeft;
            clone._originalChamferTopRight = _originalChamferTopRight;
            clone._originalChamferBottomRight = _originalChamferBottomRight;
            clone._originalChamferBottomLeft = _originalChamferBottomLeft;
            clone._hasOriginalCornerValues = _hasOriginalCornerValues;

            return FinalizeClone(clone);
        }
        #region 填充
        // 填充
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
        /// </summary>
        public List<(SKPoint Start, SKPoint End)> GetFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (LocalWidth <= 0 || LocalHeight <= 0)
                return result;

            return hatchInfo.FillTypeIndex switch
            {
                0 => GetScanlineFillLines(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                1 => GetScanlineFillLines(hatchInfo),      // S型单向 / 弓字型双向 / 优化弓字
                //2 => GetConcentricFillLines(hatchInfo),   // 回字形
                2 => GetConcentricFillLines2(hatchInfo),   // 回字形
                3 => GetSpiralFillLines(hatchInfo),
                _ => new List<(SKPoint, SKPoint)>(),      // 其他
            };
        }

        /// <summary>
        /// 获取矩形的世界坐标边框点（带 margin 内缩）
        /// </summary>
        private List<SKPoint> BuildWorldInsetPolygon(float margin)
        {
            // 获取局部坐标的内缩多边形
            var localPts = BuildInsetPolygon(margin);
            if (localPts.Count < 3) return localPts;

            // 应用变换矩阵到世界坐标
            var matrix = GetTransformMatrix();
            var worldPts = new List<SKPoint>(localPts.Count);
            foreach (var pt in localPts)
            {
                worldPts.Add(matrix.MapPoint(pt));
            }
            return worldPts;
        }

        /// <summary>
        /// 扫描线填充（S 型单向）。支持直角/圆角矩形，
        /// 圆角部分用多边形近似后进行扫描线填充。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GetScanlineFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo.LineSpacing <= 0)
                return result;

            //var polygon = BuildInsetPolygon((float)hatchInfo.Margin);
            // 世界角度：在世界坐标中计算
            var polygon = BuildWorldInsetPolygon((float)hatchInfo.Margin);

            if (polygon.Count < 3) return result;
            // FillTypeIndex：0 = S型单向，1 = S型双向（逆行反向）
            bool bidirectional = hatchInfo.FillTypeIndex == 1;
            // Extension 延伸（沿填充线方向两端各延长 extension；负值收缩，<=0 丢弃）
            // ReverseFillLine 全局反向，与 S 型双向的奇行翻转叠加。
            float extension = (float)hatchInfo.Extension;
            bool reverseAll = hatchInfo.ReverseFillLine;
            bool relativeToAngle = hatchInfo.RelativeToAngle;
            // 将多边形绕原点按 -FillAngle 旋转，使填充方向水平。
            //double rad = -hatchInfo.StartAngle * Math.PI / 180.0;
            //double rad = -(relativeToAngle ? hatchInfo.StartAngle : hatchInfo.StartAngle + Rotation) * Math.PI / 180.0;
            double rad = -(relativeToAngle ? hatchInfo.StartAngle + Rotation : hatchInfo.StartAngle) * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            var rotated = new SKPoint[polygon.Count];
            for (int i = 0; i < polygon.Count; i++)
            {
                rotated[i] = new SKPoint(
                    (float)(polygon[i].X * cos - polygon[i].Y * sin),
                    (float)(polygon[i].X * sin + polygon[i].Y * cos));
            }

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < rotated.Length; i++)
            {
                if (rotated[i].Y < minY) minY = rotated[i].Y;
                if (rotated[i].Y > maxY) maxY = rotated[i].Y;
            }

            // AverageDistribute ：将 LineSpacing 作为目标值，重算间距使扫描线在 [minY, maxY]
            // 区间均等分布；将 span 平均分成 nGaps 份，生成 nGaps-1 条填充线，
            // 使 “边界→首线 / 线间 / 尾线→边界” 的间距全部相等 = span / nGaps
            float spacing = (float)hatchInfo.LineSpacing;
            float startOffset = spacing / 2f;
            float yLimit = maxY;
            if (hatchInfo.AverageDistribute && maxY > minY)
            {
                float span = maxY - minY;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startOffset = spacing;
                yLimit = maxY - spacing * 0.5f; // 避免浮点误差导致多出一条边界线
            }

            // 水平扫描线：从 minY + spacing/2 开始，等间距向上。
            // 每条扫描线与多边形求交点，成对的交点作为一条填充线段的起止端。
            double cosBack = Math.Cos(-rad), sinBack = Math.Sin(-rad);
            var xs = new List<float>(8);
            int lineIndex = 0;
            for (float y = minY + startOffset; y < yLimit; y += spacing, lineIndex++)
            {
                xs.Clear();
                for (int i = 0; i < rotated.Length; i++)
                {
                    var p1 = rotated[i];
                    var p2 = rotated[(i + 1) % rotated.Length];
                    // 排除水平边，以半开区间规则避免端点重复计数
                    if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                    {
                        float t = (y - p1.Y) / (p2.Y - p1.Y);
                        xs.Add(p1.X + t * (p2.X - p1.X));
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                // 本行方向：S型双向时奇数行翻转，叠加全局 ReverseFillLine
                bool reverseLine = reverseAll;
                if (bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float x1 = xs[i], x2 = xs[i + 1];
                    // Extension 延伸：沿填充线方向（旋转系 x 轴）两端各延长 extension（负值则收缩）
                    if (extension != 0f)
                    {
                        x1 -= extension;
                        x2 += extension;
                        if (x2 <= x1) continue;
                    }
                    // 反旋转回本地坐标
                    var s = new SKPoint(
                        (float)(x1 * cosBack - y * sinBack),
                        (float)(x1 * sinBack + y * cosBack));
                    var e = new SKPoint(
                        (float)(x2 * cosBack - y * sinBack),
                        (float)(x2 * sinBack + y * cosBack));
                    if (reverseLine)
                    {
                        result.Add((e, s));
                    }
                    else
                    {
                        result.Add((s, e));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 返回Line和Path
        /// </summary>
        /// <param name="hatchInfo"></param>
        /// <returns></returns>
        private (List<(SKPoint Start, SKPoint End)>, SKPath path) GetScanlineFillPathLines(HatchParamDto hatchInfo)
        {
            SKPath path = new SKPath();
            var result = new List<(SKPoint, SKPoint)>();
            if (hatchInfo.LineSpacing <= 0)
                return (result, path);

            var polygon = BuildInsetPolygon((float)hatchInfo.Margin);
            if (polygon.Count < 3) return (result, path);
            // FillTypeIndex：0 = S型单向，1 = S型双向（逆行反向）
            bool bidirectional = hatchInfo.FillTypeIndex == 1;
            // Extension 延伸（沿填充线方向两端各延长 extension；负值收缩，<=0 丢弃）
            // ReverseFillLine 全局反向，与 S 型双向的奇行翻转叠加。
            float extension = (float)hatchInfo.Extension;
            bool reverseAll = hatchInfo.ReverseFillLine;
            bool relativeToAngle = hatchInfo.RelativeToAngle;
            // 将多边形绕原点按 -FillAngle 旋转，使填充方向水平。
            //double rad = -hatchInfo.StartAngle * Math.PI / 180.0;
            double rad = -(relativeToAngle ? hatchInfo.StartAngle : hatchInfo.StartAngle + Rotation) * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            var rotated = new SKPoint[polygon.Count];
            for (int i = 0; i < polygon.Count; i++)
            {
                rotated[i] = new SKPoint(
                    (float)(polygon[i].X * cos - polygon[i].Y * sin),
                    (float)(polygon[i].X * sin + polygon[i].Y * cos));
            }

            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < rotated.Length; i++)
            {
                if (rotated[i].Y < minY) minY = rotated[i].Y;
                if (rotated[i].Y > maxY) maxY = rotated[i].Y;
            }

            // AverageDistribute ：将 LineSpacing 作为目标值，重算间距使扫描线在 [minY, maxY]
            // 区间均等分布；将 span 平均分成 nGaps 份，生成 nGaps-1 条填充线，
            // 使 “边界→首线 / 线间 / 尾线→边界” 的间距全部相等 = span / nGaps
            float spacing = (float)hatchInfo.LineSpacing;
            float startOffset = spacing / 2f;
            float yLimit = maxY;
            if (hatchInfo.AverageDistribute && maxY > minY)
            {
                float span = maxY - minY;
                int nGaps = Math.Max(2, (int)Math.Round(span / spacing));
                spacing = span / nGaps;
                startOffset = spacing;
                yLimit = maxY - spacing * 0.5f; // 避免浮点误差导致多出一条边界线
            }

            // 水平扫描线：从 minY + spacing/2 开始，等间距向上。
            // 每条扫描线与多边形求交点，成对的交点作为一条填充线段的起止端。
            double cosBack = Math.Cos(-rad), sinBack = Math.Sin(-rad);
            var xs = new List<float>(8);
            int lineIndex = 0;
            for (float y = minY + startOffset; y < yLimit; y += spacing, lineIndex++)
            {
                xs.Clear();
                for (int i = 0; i < rotated.Length; i++)
                {
                    var p1 = rotated[i];
                    var p2 = rotated[(i + 1) % rotated.Length];
                    // 排除水平边，以半开区间规则避免端点重复计数
                    if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                    {
                        float t = (y - p1.Y) / (p2.Y - p1.Y);
                        xs.Add(p1.X + t * (p2.X - p1.X));
                    }
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                // 本行方向：S型双向时奇数行翻转，叠加全局 ReverseFillLine
                bool reverseLine = reverseAll;
                if (bidirectional && (lineIndex & 1) == 1) reverseLine = !reverseLine;

                for (int i = 0; i + 1 < xs.Count; i += 2)
                {
                    float x1 = xs[i], x2 = xs[i + 1];
                    // Extension 延伸：沿填充线方向（旋转系 x 轴）两端各延长 extension（负值则收缩）
                    if (extension != 0f)
                    {
                        x1 -= extension;
                        x2 += extension;
                        if (x2 <= x1) continue;
                    }
                    // 反旋转回本地坐标
                    var s = new SKPoint(
                        (float)(x1 * cosBack - y * sinBack),
                        (float)(x1 * sinBack + y * cosBack));
                    var e = new SKPoint(
                        (float)(x2 * cosBack - y * sinBack),
                        (float)(x2 * sinBack + y * cosBack));
                    if (reverseLine)
                    {
                        result.Add((e, s));
                    }
                    else
                    {
                        result.Add((s, e));
                    }
                }
            }

            return (result, path);
        }

        ///// <summary>
        ///// 回字形（同心矩形）填充。
        ///// 从外圈向内逐圈生成矩形轮廓线段，圈距由 FillRingSpacing 控制。
        ///// 支持直角和圆角矩形，圆角半径保持与外圈一致，仅当矩形过小时自动缩减。
        ///// </summary>
        //private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines(HatchParamDto hatchInfo)
        //{
        //    var result = new List<(SKPoint, SKPoint)>();

        //    float spacing = hatchInfo.RingSpacing > 0 ? (float)hatchInfo.RingSpacing : (float)hatchInfo.LineSpacing;
        //    if (spacing <= 0)
        //        return result;

        //    float margin = (float)hatchInfo.Margin;
        //    bool reverseAll = hatchInfo.ReverseFillLine;
        //    // 方向：0=向内（固定起点）、1=向外（固定起点）、2=向内（变动起点）、3=向外（变动起点）、4=向内再向外（变动起点）、5=向外再向内（变动起点）
        //    int directionType = hatchInfo.DirectionTypeIndex;

        //    // 起始矩形（应用 margin 后）
        //    var localBounds = GetInsetLocalBounds(margin);
        //    float left = localBounds.Left;
        //    float right = localBounds.Right;
        //    float bottom = localBounds.Bottom;
        //    float top = localBounds.Top;

        //    if (left >= right || bottom >= top)
        //        return result;

        //    // 起始圆角半径（减去 margin）
        //    float baseTL = Math.Max(0, CornerRadiusTopLeft - margin);
        //    float baseTR = Math.Max(0, CornerRadiusTopRight - margin);
        //    float baseBR = Math.Max(0, CornerRadiusBottomRight - margin);
        //    float baseBL = Math.Max(0, CornerRadiusBottomLeft - margin);

        //    while (left < right && bottom < top)
        //    {
        //        // 当前矩形的半宽半高
        //        float halfW = (right - left) / 2f;
        //        float halfH = (top - bottom) / 2f;

        //        // 回字形填充：保持外圈圆角半径不变，仅当矩形过小时自动缩减
        //        // （几何内缩会令圆角快速消失，视觉上不理想；保持圆角使各圈风格一致）
        //        float tl = Math.Min(baseTL, Math.Min(halfW, halfH));
        //        float tr = Math.Min(baseTR, Math.Min(halfW, halfH));
        //        float br = Math.Min(baseBR, Math.Min(halfW, halfH));
        //        float bl = Math.Min(baseBL, Math.Min(halfW, halfH));

        //        if (reverseAll)
        //        {
        //            // 上边: (left + tl, top) → (right - tr, top)
        //            if (right - tr > left + tl)
        //                result.Add((new SKPoint(left + tl, top), new SKPoint(right - tr, top)));

        //            // 右上角弧
        //            if (tr > 0.01f)
        //                AddCornerArcSegments(result, right - tr, top - tr, tr, 90f, 0f);

        //            // 右边: (right, top - tr) → (right, bottom + br)
        //            if (top - tr > bottom + br)
        //                result.Add((new SKPoint(right, top - tr), new SKPoint(right, bottom + br)));

        //            // 右下角弧
        //            if (br > 0.01f)
        //                AddCornerArcSegments(result, right - br, bottom + br, br, 0f, -90f);

        //            // 下边: (right - br, bottom) → (left + bl, bottom)
        //            if (right - br > left + bl)
        //                result.Add((new SKPoint(right - br, bottom), new SKPoint(left + bl, bottom)));

        //            // 左下角弧
        //            if (bl > 0.01f)
        //                AddCornerArcSegments(result, left + bl, bottom + bl, bl, -90f, -180f);

        //            // 左边: (left, bottom + bl) → (left, top - tl)
        //            if (top - tl > bottom + bl)
        //                result.Add((new SKPoint(left, bottom + bl), new SKPoint(left, top - tl)));

        //            // 左上角弧
        //            if (tl > 0.01f)
        //                AddCornerArcSegments(result, left + tl, top - tl, tl, 180f, 90f);
        //        }
        //        else
        //        {
        //            // 左边: (left, bottom + bl) → (left, top - tl)
        //            if (top - tl > bottom + bl)
        //                result.Add((new SKPoint(left, top - tl), new SKPoint(left, bottom + bl)));

        //            // 左下角弧
        //            if (bl > 0.01f)
        //                AddCornerArcSegments(result, left + bl, bottom + bl, bl, -180f, -90f);

        //            // 下边: (right - br, bottom) → (left + bl, bottom)
        //            if (right - br > left + bl)
        //                result.Add((new SKPoint(left + bl, bottom), new SKPoint(right - br, bottom)));

        //            // 右下角弧
        //            if (br > 0.01f)
        //                AddCornerArcSegments(result, right - br, bottom + br, br, -90f, 0f);

        //            // 右边: (right, top - tr) → (right, bottom + br)
        //            if (top - tr > bottom + br)
        //                result.Add((new SKPoint(right, bottom + br), new SKPoint(right, top - tr)));

        //            // 右上角弧
        //            if (tr > 0.01f)
        //                AddCornerArcSegments(result, right - tr, top - tr, tr, 0f, 90f);

        //            // 上边: (left + tl, top) → (right - tr, top)
        //            if (right - tr > left + tl)
        //                result.Add((new SKPoint(right - tr, top), new SKPoint(left + tl, top)));

        //            // 左上角弧
        //            if (tl > 0.01f)
        //                AddCornerArcSegments(result, left + tl, top - tl, tl, 90f, 180f);
        //        }

        //        // 内缩一圈
        //        left += spacing;
        //        right -= spacing;
        //        bottom += spacing;
        //        top -= spacing;
        //    }

        //    return result;
        //}


        #region 优化参考
        /// <summary>
        /// 回字形（同心矩形）填充。
        /// 从外圈向内逐圈生成矩形轮廓线段，圈距由 FillRingSpacing 控制。
        /// 支持直角和圆角矩形，圆角半径保持与外圈一致，仅当矩形过小时自动缩减。
        /// 方向类型：
        ///   0 = 向内（固定起点-左上角）
        ///   1 = 向外（固定起点-左上角）
        ///   2 = 向内（变动起点-四个顶点循环）
        ///   3 = 向外（变动起点-四个顶点循环）
        ///   4 = 向内再向外（变动起点-四个顶点循环）
        ///   5 = 向外再向内（变动起点-四个顶点循环）
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GetConcentricFillLines2(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();

            float spacing = hatchInfo.RingSpacing > 0 ? (float)hatchInfo.RingSpacing : (float)hatchInfo.LineSpacing;
            if (spacing <= 0)
                return result;

            float margin = (float)hatchInfo.Margin;
            bool reverseAll = hatchInfo.ReverseFillLine;
            // 方向：0=向内（固定起点）、1=向外（固定起点）、2=向内（变动起点）、3=向外（变动起点）、4=向内再向外（变动起点）、5=向外再向内（变动起点）
            int directionType = hatchInfo.DirectionTypeIndex;

            // 起始矩形（应用 margin 后）
            var localBounds = GetInsetLocalBounds(margin);
            float left = localBounds.Left;
            float right = localBounds.Right;
            float bottom = localBounds.Bottom;
            float top = localBounds.Top;

            if (left >= right || bottom >= top)
                return result;

            // 起始圆角半径（减去 margin）
            float baseTL = Math.Max(0, CornerRadiusTopLeft - margin);
            float baseTR = Math.Max(0, CornerRadiusTopRight - margin);
            float baseBR = Math.Max(0, CornerRadiusBottomRight - margin);
            float baseBL = Math.Max(0, CornerRadiusBottomLeft - margin);

            // 起始倒角长度（减去 margin）
            float baseCTL = Math.Max(0, ChamferTopLeft - margin);
            float baseCTR = Math.Max(0, ChamferTopRight - margin);
            float baseCBR = Math.Max(0, ChamferBottomRight - margin);
            float baseCBL = Math.Max(0, ChamferBottomLeft - margin);
            bool isChamferMode = baseCTL > 0 || baseCTR > 0 || baseCBR > 0 || baseCBL > 0;

            // 计算最大圈数
            float halfW = (right - left) / 2f;
            float halfH = (top - bottom) / 2f;
            float minHalf = Math.Min(halfW, halfH);
            int maxPossibleTurns = (int)(minHalf / spacing);
            if (maxPossibleTurns < 1) maxPossibleTurns = 1;

            int totalTurns = hatchInfo.InternalRings > 0
                ? Math.Min(hatchInfo.InternalRings, maxPossibleTurns)
                : maxPossibleTurns;

            // 收集所有圈的矩形数据
            var rectangles = new List<RectangleInfo>();
            float currentLeft = left;
            float currentRight = right;
            float currentBottom = bottom;
            float currentTop = top;
            int turnIndex = 0;

            while (currentLeft < currentRight && currentBottom < currentTop && turnIndex <= totalTurns)
            {
                float halfWCurr = (currentRight - currentLeft) / 2f;
                float halfHCurr = (currentTop - currentBottom) / 2f;
                float minHalfCurr = Math.Min(halfWCurr, halfHCurr);

                float insetDepth = turnIndex * spacing;
                var rectInfo = new RectangleInfo
                {
                    Left = currentLeft,
                    Right = currentRight,
                    Bottom = currentBottom,
                    Top = currentTop,
                    IsChamfer = isChamferMode,
                    TurnIndex = turnIndex
                };

                if (isChamferMode)
                {
                    // 倒角模式：倒角长度随内缩减小
                    rectInfo.CTL = Math.Max(0, Math.Min(baseCTL - insetDepth, minHalfCurr));
                    rectInfo.CTR = Math.Max(0, Math.Min(baseCTR - insetDepth, minHalfCurr));
                    rectInfo.CBR = Math.Max(0, Math.Min(baseCBR - insetDepth, minHalfCurr));
                    rectInfo.CBL = Math.Max(0, Math.Min(baseCBL - insetDepth, minHalfCurr));
                }
                else
                {
                    // 圆角模式：圆角半径随内缩减小
                    rectInfo.TL = Math.Min(Math.Max(0, baseTL - insetDepth), minHalfCurr);
                    rectInfo.TR = Math.Min(Math.Max(0, baseTR - insetDepth), minHalfCurr);
                    rectInfo.BR = Math.Min(Math.Max(0, baseBR - insetDepth), minHalfCurr);
                    rectInfo.BL = Math.Min(Math.Max(0, baseBL - insetDepth), minHalfCurr);
                }
                rectangles.Add(rectInfo);

                currentLeft += spacing;
                currentRight -= spacing;
                currentBottom += spacing;
                currentTop -= spacing;
                turnIndex++;
            }

            if (rectangles.Count == 0) return result;

            // 根据方向类型生成线段
            switch (directionType)
            {
                case 0: // 向内（固定起点-左上角）
                    for (int i = 0; i < rectangles.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        AddRectangleSegments(result, rectangles[i], startIndex: (int)CornerType.TopLeft, reverse: reverseAll);
                    }
                    break;

                case 1: // 向外（固定起点-左上角）- 从内向外输出
                    for (int i = rectangles.Count - 1; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        AddRectangleSegments(result, rectangles[i], startIndex: (int)CornerType.TopLeft, reverse: reverseAll);
                    }
                    break;

                case 2: // 向内（变动起点-四个顶点循环）
                    for (int i = 0; i < rectangles.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startCorner = (int)(CornerType)(i % 4);
                        AddRectangleSegments(result, rectangles[i], startIndex: startCorner, reverse: reverseAll);
                    }
                    break;

                case 3: // 向外（变动起点-四个顶点循环）- 从内向外输出
                    for (int i = rectangles.Count - 1; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startCorner = (int)(CornerType)(i % 4);
                        AddRectangleSegments(result, rectangles[i], startIndex: startCorner, reverse: reverseAll);
                    }
                    break;

                case 4: // 向内再向外（变动起点-四个顶点循环）
                        // 第一段：向内
                    for (int i = 0; i < rectangles.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startCorner = (int)(CornerType)(i % 4);
                        AddRectangleSegments(result, rectangles[i], startIndex: startCorner, reverse: reverseAll);
                    }
                    // 第二段：向外（跳过最内圈避免重复）
                    for (int i = rectangles.Count - 2; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startCorner = (int)(CornerType)(i % 4);
                        AddRectangleSegments(result, rectangles[i], startIndex: startCorner, reverse: reverseAll);
                    }
                    break;

                case 5: // 向外再向内（变动起点-四个顶点循环）
                        // 第一段：向外
                    for (int i = rectangles.Count - 1; i >= 0; i--)
                    {
                        if (i == 0 && margin == 0) continue;
                        int startCorner = (i % 4);
                        AddRectangleSegments(result, rectangles[i], startIndex: startCorner, reverse: reverseAll);
                    }
                    // 第二段：向内（跳过最外圈避免重复）
                    for (int i = 1; i < rectangles.Count; i++)
                    {
                        int startCorner = (int)(CornerType)(i % 4);
                        AddRectangleSegments(result, rectangles[i], startIndex: startCorner, reverse: reverseAll);
                    }
                    break;

                default:
                    // 默认向内（固定起点）
                    for (int i = 0; i < rectangles.Count; i++)
                    {
                        if (i == 0 && margin == 0) continue;
                        AddRectangleSegments(result, rectangles[i], startIndex: (int)CornerType.TopLeft, reverse: reverseAll);
                    }
                    break;
            }

            // ✅ 将局部坐标转换到世界坐标
            var matrix = GetTransformMatrix();

            for (int i = 0; i < result.Count; i++)
            {
                var startWorld = matrix.MapPoint(result[i].Item1);
                var endWorld = matrix.MapPoint(result[i].Item2);
                result[i] = ((startWorld, endWorld));
            }

            return result;
        }

        /// <summary>
        /// 矩形角点类型
        /// </summary>
        private enum CornerType
        {
            TopLeft,     // 左上角
            TopRight,    // 右上角
            BottomRight, // 右下角
            BottomLeft   // 左下角
        }

        /// <summary>
        /// 矩形信息
        /// </summary>
        private class RectangleInfo
        {
            public float Left, Right, Bottom, Top;
            public float TL, TR, BR, BL;    // 圆角半径
            public float CTL, CTR, CBR, CBL; // 倒角长度
            public bool IsChamfer;           // true=倒角模式, false=圆角模式
            public int TurnIndex;
        }

        /// <summary>
        /// 添加矩形轮廓线段
        /// </summary>
        /// <param name="result">结果列表</param>
        /// <param name="rect">矩形信息</param>
        /// <param name="startCorner">起始角点</param>
        /// <param name="reverse">是否反向绘制</param>
        private void AddRectangleSegments(List<(SKPoint, SKPoint)> result, RectangleInfo rect, int startIndex, bool reverse)
        {
            var allSegments = new List<(SKPoint, SKPoint)>();

            if (rect.IsChamfer)
                AddChamferRectSegments(allSegments, rect, reverse);
            else
                AddRoundedRectSegments(allSegments, rect, reverse);

            var reorderedSegments = ReorderSegmentsByStartCorner(allSegments, startIndex);
            result.AddRange(reorderedSegments);
        }

        /// <summary>添加圆角矩形轮廓线段</summary>
        private void AddRoundedRectSegments(List<(SKPoint, SKPoint)> allSegments, RectangleInfo rect, bool reverse)
        {
            if (reverse)
            {
                // 上边: (left + TL, top) → (right - TR, top)
                if (rect.Right - rect.TR > rect.Left + rect.TL)
                    allSegments.Add((new SKPoint(rect.Left + rect.TL, rect.Top), new SKPoint(rect.Right - rect.TR, rect.Top)));

                // 右上角弧
                if (rect.TR > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Right - rect.TR, rect.Top - rect.TR, rect.TR, 90f, 0f);

                // 右边: (right, top - TR) → (right, bottom + BR)
                if (rect.Top - rect.TR > rect.Bottom + rect.BR)
                    allSegments.Add((new SKPoint(rect.Right, rect.Top - rect.TR), new SKPoint(rect.Right, rect.Bottom + rect.BR)));

                // 右下角弧
                if (rect.BR > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Right - rect.BR, rect.Bottom + rect.BR, rect.BR, 0f, -90f);

                // 下边: (right - BR, bottom) → (left + BL, bottom)
                if (rect.Right - rect.BR > rect.Left + rect.BL)
                    allSegments.Add((new SKPoint(rect.Right - rect.BR, rect.Bottom), new SKPoint(rect.Left + rect.BL, rect.Bottom)));

                // 左下角弧
                if (rect.BL > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Left + rect.BL, rect.Bottom + rect.BL, rect.BL, -90f, -180f);

                // 左边: (left, bottom + BL) → (left, top - TL)
                if (rect.Top - rect.TL > rect.Bottom + rect.BL)
                    allSegments.Add((new SKPoint(rect.Left, rect.Bottom + rect.BL), new SKPoint(rect.Left, rect.Top - rect.TL)));

                // 左上角弧
                if (rect.TL > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Left + rect.TL, rect.Top - rect.TL, rect.TL, 180f, 90f);
            }
            else
            {
                // 左边: (left, bottom + bl) → (left, top - tl)
                if (rect.Top - rect.TL > rect.Bottom + rect.BL)
                    allSegments.Add((new SKPoint(rect.Left, rect.Top - rect.TL), new SKPoint(rect.Left, rect.Bottom + rect.BL)));

                // 左下角弧
                if (rect.BL > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Left + rect.BL, rect.Bottom + rect.BL, rect.BL, -180f, -90f);

                // 下边: (right - br, bottom) → (left + bl, bottom)
                if (rect.Right - rect.BR > rect.Left + rect.BL)
                    allSegments.Add((new SKPoint(rect.Left + rect.BL, rect.Bottom), new SKPoint(rect.Right - rect.BR, rect.Bottom)));

                // 右下角弧
                if (rect.BR > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Right - rect.BR, rect.Bottom + rect.BR, rect.BR, -90f, 0f);

                // 右边: (right, top - tr) → (right, bottom + br)
                if (rect.Top - rect.TR > rect.Bottom + rect.BR)
                    allSegments.Add((new SKPoint(rect.Right, rect.Bottom + rect.BR), new SKPoint(rect.Right, rect.Top - rect.TR)));

                // 右上角弧
                if (rect.TR > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Right - rect.TR, rect.Top - rect.TR, rect.TR, 0f, 90f);

                // 上边: (left + tl, top) → (right - tr, top)
                if (rect.Right - rect.TR > rect.Left + rect.TL)
                    allSegments.Add((new SKPoint(rect.Right - rect.TR, rect.Top), new SKPoint(rect.Left + rect.TL, rect.Top)));

                // 左上角弧
                if (rect.TL > 0.01f)
                    AddCornerArcSegments(allSegments, rect.Left + rect.TL, rect.Top - rect.TL, rect.TL, 90f, 180f);
            }
        }

        /// <summary>添加倒角矩形轮廓线段</summary>
        private void AddChamferRectSegments(List<(SKPoint, SKPoint)> allSegments, RectangleInfo rect, bool reverse)
        {
            float cTL = rect.CTL, cTR = rect.CTR, cBR = rect.CBR, cBL = rect.CBL;
            if (reverse)
            {
                if (rect.Right - cTR > rect.Left + cTL)
                    allSegments.Add((new SKPoint(rect.Left + cTL, rect.Top), new SKPoint(rect.Right - cTR, rect.Top)));
                if (cTR > 0.01f)
                    allSegments.Add((new SKPoint(rect.Right - cTR, rect.Top), new SKPoint(rect.Right, rect.Top - cTR)));
                if (rect.Top - cTR > rect.Bottom + cBR)
                    allSegments.Add((new SKPoint(rect.Right, rect.Top - cTR), new SKPoint(rect.Right, rect.Bottom + cBR)));
                if (cBR > 0.01f)
                    allSegments.Add((new SKPoint(rect.Right, rect.Bottom + cBR), new SKPoint(rect.Right - cBR, rect.Bottom)));
                if (rect.Right - cBR > rect.Left + cBL)
                    allSegments.Add((new SKPoint(rect.Right - cBR, rect.Bottom), new SKPoint(rect.Left + cBL, rect.Bottom)));
                if (cBL > 0.01f)
                    allSegments.Add((new SKPoint(rect.Left + cBL, rect.Bottom), new SKPoint(rect.Left, rect.Bottom + cBL)));
                if (rect.Top - cTL > rect.Bottom + cBL)
                    allSegments.Add((new SKPoint(rect.Left, rect.Bottom + cBL), new SKPoint(rect.Left, rect.Top - cTL)));
                if (cTL > 0.01f)
                    allSegments.Add((new SKPoint(rect.Left, rect.Top - cTL), new SKPoint(rect.Left + cTL, rect.Top)));
            }
            else
            {
                if (rect.Top - cTL > rect.Bottom + cBL)
                    allSegments.Add((new SKPoint(rect.Left, rect.Top - cTL), new SKPoint(rect.Left, rect.Bottom + cBL)));
                if (cBL > 0.01f)
                    allSegments.Add((new SKPoint(rect.Left, rect.Bottom + cBL), new SKPoint(rect.Left + cBL, rect.Bottom)));
                if (rect.Right - cBR > rect.Left + cBL)
                    allSegments.Add((new SKPoint(rect.Left + cBL, rect.Bottom), new SKPoint(rect.Right - cBR, rect.Bottom)));
                if (cBR > 0.01f)
                    allSegments.Add((new SKPoint(rect.Right - cBR, rect.Bottom), new SKPoint(rect.Right, rect.Bottom + cBR)));
                if (rect.Top - cTR > rect.Bottom + cBR)
                    allSegments.Add((new SKPoint(rect.Right, rect.Bottom + cBR), new SKPoint(rect.Right, rect.Top - cTR)));
                if (cTR > 0.01f)
                    allSegments.Add((new SKPoint(rect.Right, rect.Top - cTR), new SKPoint(rect.Right - cTR, rect.Top)));
                if (rect.Right - cTR > rect.Left + cTL)
                    allSegments.Add((new SKPoint(rect.Right - cTR, rect.Top), new SKPoint(rect.Left + cTL, rect.Top)));
                if (cTL > 0.01f)
                    allSegments.Add((new SKPoint(rect.Left + cTL, rect.Top), new SKPoint(rect.Left, rect.Top - cTL)));
            }
        }

        /// <summary>
        /// 根据起始角点重新排列线段顺序
        /// </summary>
        private List<(SKPoint, SKPoint)> ReorderSegmentsByStartCorner(List<(SKPoint, SKPoint)> segments, int startIndex)
        {
            if (segments == null || segments.Count == 0) return new List<(SKPoint, SKPoint)>();

            var reordered = new List<(SKPoint, SKPoint)>();
            int n = segments.Count;
            startIndex = startIndex % n;  // 处理索引越界
            if (startIndex < 0)
                startIndex += n;  // 处理负索引

            // 从 startIndex 处断开，将后半部分和前半部分拼接
            return segments.Skip(startIndex).Concat(segments.Take(startIndex)).ToList();
        }

        #endregion

        /// <summary>
        /// 矩形螺旋线填充（“依次递进”的真螺旋效果）。
        /// 参数：Margin 边距、RingSpacing 圈距、InternalRings 内圈数、
        /// DirectionTypeIndex 方向(0=向内,1=向外,2=向内再向外,3=向外再向内)。
        /// 算法核心：每一条“边”(side k) 处于一个恒定的内缩深度 depth(k)，
        /// 每走到一个转角处只有这一个转角向内/向外“递进一次”（步进 spacing/4），
        /// 而不是 4 个角同时跳动。连续走完 4 边为一周期，正好整体推进 spacing。
        /// 借鉴圆/椭圆螺旋(<see cref="DrawCircle"/>)做法：向内/向外均沿
        /// 同一旋向（CCW，底→右→顶→左），向外路径并非将向内路径反转，
        /// 而是从内圈左下角出发以同样旋向逐圈外扩到边距收缩后的最外层边框。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> GetSpiralFillLines(HatchParamDto hatchInfo)
        {
            var result = new List<(SKPoint, SKPoint)>();

            float spacing = hatchInfo.RingSpacing > 0 ? (float)hatchInfo.RingSpacing : (float)hatchInfo.LineSpacing;
            if (spacing <= 0)
                return result;

            float margin = (float)hatchInfo.Margin;

            // 起始矩形（应用 margin 后）
            var localBounds = GetInsetLocalBounds(margin);
            float left = localBounds.Left;
            float right = localBounds.Right;
            float bottom = localBounds.Bottom;
            float top = localBounds.Top;

            if (left >= right || bottom >= top)
                return result;

            // 螺旋方向：0=向内、1=向外、2=向内再向外、3=向外再向内
            int directionType = hatchInfo.DirectionTypeIndex;

            float halfW = (right - left) / 2f;
            float halfH = (top - bottom) / 2f;
            float minHalf = Math.Min(halfW, halfH);

            // 计算可容纳的最大圈数
            int maxPossibleTurns = (int)(minHalf / spacing);
            if (maxPossibleTurns < 1) maxPossibleTurns = 1;

            int totalTurns = hatchInfo.InternalRings > 0
                ? Math.Min(hatchInfo.InternalRings, maxPossibleTurns)
                : maxPossibleTurns;

            // 内层封闭矩形（位于 depth = totalTurns * spacing）
            float maxDepth = totalTurns * spacing;
            float innerLeft = left + maxDepth;
            float innerRight = right - maxDepth;
            float innerBottom = bottom + maxDepth;
            float innerTop = top - maxDepth;

            // 圆角基准（减去 margin）
            float baseTL = Math.Max(0, CornerRadiusTopLeft - margin);
            float baseTR = Math.Max(0, CornerRadiusTopRight - margin);
            float baseBR = Math.Max(0, CornerRadiusBottomRight - margin);
            float baseBL = Math.Max(0, CornerRadiusBottomLeft - margin);

            // 倒角基准（减去 margin）
            float baseCTL = Math.Max(0, ChamferTopLeft - margin);
            float baseCTR = Math.Max(0, ChamferTopRight - margin);
            float baseCBR = Math.Max(0, ChamferBottomRight - margin);
            float baseCBL = Math.Max(0, ChamferBottomLeft - margin);
            bool isChamferMode = baseCTL > 0 || baseCTR > 0 || baseCBR > 0 || baseCBL > 0;

            // 角部参数数组：[BR, TR, TL, BL] 对应螺旋旋向 CCW 转角顺序
            float[] baseCorners = isChamferMode
                ? new[] { baseCBR, baseCTR, baseCTL, baseCBL }
                : new[] { baseBR, baseTR, baseTL, baseBL };

            // 外圈封闭矩形
            var outerRect = new List<(SKPoint, SKPoint)>();
            if (isChamferMode)
            {
                float cTL = Math.Min(baseCTL, minHalf);
                float cTR = Math.Min(baseCTR, minHalf);
                float cBR = Math.Min(baseCBR, minHalf);
                float cBL = Math.Min(baseCBL, minHalf);
                EmitChamferRectSegments(outerRect, left, bottom, right, top, cTL, cTR, cBR, cBL);
            }
            else
            {
                float TL = Math.Min(baseTL, minHalf);
                float TR = Math.Min(baseTR, minHalf);
                float BR = Math.Min(baseBR, minHalf);
                float BL = Math.Min(baseBL, minHalf);
                EmitRoundedRectSegments(outerRect, left, bottom, right, top, TL, TR, BR, BL);
            }

            // 内圈封闭矩形
            var innerRect = new List<(SKPoint, SKPoint)>();
            if (innerLeft < innerRight && innerBottom < innerTop)
            {
                float halfWi = (innerRight - innerLeft) / 2f;
                float halfHi = (innerTop - innerBottom) / 2f;
                float minHalfI = Math.Min(halfWi, halfHi);
                if (isChamferMode)
                {
                    float cTL = Math.Max(0, Math.Min(baseCTL - maxDepth, minHalfI));
                    float cTR = Math.Max(0, Math.Min(baseCTR - maxDepth, minHalfI));
                    float cBR = Math.Max(0, Math.Min(baseCBR - maxDepth, minHalfI));
                    float cBL = Math.Max(0, Math.Min(baseCBL - maxDepth, minHalfI));
                    EmitChamferRectSegments(innerRect, innerLeft, innerBottom, innerRight, innerTop, cTL, cTR, cBR, cBL);
                }
                else
                {
                    float TL = Math.Min(Math.Max(0, baseTL - maxDepth), minHalfI);
                    float TR = Math.Min(Math.Max(0, baseTR - maxDepth), minHalfI);
                    float BR = Math.Min(Math.Max(0, baseBR - maxDepth), minHalfI);
                    float BL = Math.Min(Math.Max(0, baseBL - maxDepth), minHalfI);
                    EmitRoundedRectSegments(innerRect, innerLeft, innerBottom, innerRight, innerTop, TL, TR, BR, BL);
                }
            }

            // 向内螺旋
            var inwardPts = new List<SKPoint>();
            BuildRectSpiralPath(inwardPts, left, bottom, right, top, spacing, totalTurns,
                innerLeft, innerBottom, inward: true, baseCorners, isChamferMode);

            // 向外螺旋
            var outwardPts = new List<SKPoint>();
            BuildRectSpiralPath(outwardPts, left, bottom, right, top, spacing, totalTurns,
                innerLeft, innerBottom, inward: false, baseCorners, isChamferMode);

            void AppendSegments(List<SKPoint> pts)
            {
                for (int i = 0; i + 1 < pts.Count; i++)
                    result.Add((pts[i], pts[i + 1]));
            }

            switch (directionType)
            {
                case 1: // 向外：内圈闭合 → 螺旋外扩 → 外圈闭合
                    result.AddRange(innerRect);
                    AppendSegments(outwardPts);
                    if (margin > 0)
                    {
                        result.AddRange(outerRect);
                    }
                    break;
                case 2: // 向内再向外：外圈 → 内缩螺旋 → 内圈 → 外扩螺旋 → 外圈
                    if (margin > 0)
                    {
                        result.AddRange(outerRect);
                    }
                    AppendSegments(inwardPts);
                    result.AddRange(innerRect);
                    AppendSegments(outwardPts);
                    if (margin > 0)
                    {
                        result.AddRange(outerRect);
                    }
                    break;
                case 3: // 向外再向内：内圈 → 外扩螺旋 → 外圈 → 内缩螺旋 → 内圈
                    result.AddRange(innerRect);
                    AppendSegments(outwardPts);
                    if (margin > 0)
                    {
                        result.AddRange(outerRect);
                    }
                    AppendSegments(inwardPts);
                    result.AddRange(innerRect);
                    break;
                case 0: // 向内
                default:
                    if (margin > 0)
                    {
                        result.AddRange(outerRect);
                    }
                    AppendSegments(inwardPts);
                    result.AddRange(innerRect);
                    break;
            }

            // ✅ 将局部坐标转换到世界坐标
            var matrix = GetTransformMatrix();

            for (int i = 0; i < result.Count; i++)
            {
                var startWorld = matrix.MapPoint(result[i].Item1);
                var endWorld = matrix.MapPoint(result[i].Item2);
                result[i] = ((startWorld, endWorld));
            }

            return result;
        }

        /// <summary>
        /// 构造矩形螺旋线的连续路径点，支持圆角/倒角处理。
        /// 旋向恒为 CCW（底 → 右 → 顶 → 左）。
        /// baseCorners 顺序: [BR, TR, TL, BL]。
        /// </summary>
        private static void BuildRectSpiralPath(List<SKPoint> pts,
            float left, float bottom, float right, float top,
            float spacing, int totalTurns,
            float innerLeft, float innerBottom, bool inward,
            float[] baseCorners, bool isChamfer)
        {
            if (totalTurns <= 0) return;

            float quarter = spacing / 4f;
            float maxDepth = totalTurns * spacing;

            // 左下角的圆角/倒角值，用于确定螺旋起止点在矩形边界上
            float blR = Math.Max(0, baseCorners[3]);
            float halfW = (right - left) / 2f;
            float halfH = (top - bottom) / 2f;
            float minHalf = Math.Min(halfW, halfH);
            blR = Math.Min(blR, minHalf);

            float innerBlR = Math.Max(0, blR - maxDepth);
            float innerHalfW = Math.Max(0, (right - 2 * maxDepth - left) / 2f);
            float innerHalfH = Math.Max(0, (top - 2 * maxDepth - bottom) / 2f);
            float innerMinHalf = Math.Min(innerHalfW, innerHalfH);
            innerBlR = Math.Min(innerBlR, innerMinHalf);

            // 起点：在 BL 圆弧的底边切点处，与第一段底边对齐
            pts.Add(inward
                ? new SKPoint(left + blR, bottom)
                : new SKPoint(innerLeft + innerBlR, innerBottom));

            int totalSides = 4 * totalTurns;
            for (int i = 0; i < totalSides; i++)
            {
                int t = i % 4; // 角类型: 0=BR, 1=TR, 2=TL, 3=BL
                float depth = inward
                    ? (i + 1) * quarter
                    : maxDepth - (i + 1) * quarter;

                // 当前角的圆角/倒角半径（随深度减小）
                float r = Math.Max(0, baseCorners[t] - depth);
                float curHalfW = Math.Max(0, halfW - depth);
                float curHalfH = Math.Max(0, halfH - depth);
                r = Math.Min(r, Math.Min(curHalfW, curHalfH));

                if (r < 0.01f)
                {
                    pts.Add(GetSpiralCornerPoint(t, left, bottom, right, top, depth));
                }
                else if (isChamfer)
                {
                    AddSpiralChamferPoints(pts, t, left, bottom, right, top, depth, r);
                }
                else
                {
                    AddSpiralArcPoints(pts, t, left, bottom, right, top, depth, r);
                }
            }

            // 末端小连接
            pts.Add(inward
                ? new SKPoint(innerLeft + innerBlR, innerBottom)
                : new SKPoint(left + blR, bottom));
        }

        /// <summary>获取螺旋转角的尖角点（无圆角/倒角时）</summary>
        private static SKPoint GetSpiralCornerPoint(int t, float left, float bottom, float right, float top, float depth)
        {
            return t switch
            {
                0 => new SKPoint(right - depth, bottom + depth),
                1 => new SKPoint(right - depth, top - depth),
                2 => new SKPoint(left + depth, top - depth),
                _ => new SKPoint(left + depth, bottom + depth),
            };
        }

        /// <summary>在螺旋转角处添加倒角点（两个点）</summary>
        private static void AddSpiralChamferPoints(List<SKPoint> pts, int t,
            float left, float bottom, float right, float top, float depth, float c)
        {
            switch (t)
            {
                case 0: // BR: 底边→右边
                    pts.Add(new SKPoint(right - depth - c, bottom + depth));
                    pts.Add(new SKPoint(right - depth, bottom + depth + c));
                    break;
                case 1: // TR: 右边→顶边
                    pts.Add(new SKPoint(right - depth, top - depth - c));
                    pts.Add(new SKPoint(right - depth - c, top - depth));
                    break;
                case 2: // TL: 顶边→左边
                    pts.Add(new SKPoint(left + depth + c, top - depth));
                    pts.Add(new SKPoint(left + depth, top - depth - c));
                    break;
                default: // BL: 左边→底边
                    pts.Add(new SKPoint(left + depth, bottom + depth + c));
                    pts.Add(new SKPoint(left + depth + c, bottom + depth));
                    break;
            }
        }

        /// <summary>在螺旋转角处添加圆弧离散点</summary>
        private static void AddSpiralArcPoints(List<SKPoint> pts, int t,
            float left, float bottom, float right, float top, float depth, float r)
        {
            const int arcSteps = 16;
            float cx, cy, startDeg, endDeg;

            switch (t)
            {
                case 0: cx = right - depth - r; cy = bottom + depth + r; startDeg = -90f; endDeg = 0f; break;
                case 1: cx = right - depth - r; cy = top - depth - r; startDeg = 0f; endDeg = 90f; break;
                case 2: cx = left + depth + r; cy = top - depth - r; startDeg = 90f; endDeg = 180f; break;
                default: cx = left + depth + r; cy = bottom + depth + r; startDeg = 180f; endDeg = 270f; break;
            }

            for (int s = 0; s <= arcSteps; s++)
            {
                float angle = (startDeg + (endDeg - startDeg) * s / arcSteps) * MathF.PI / 180f;
                pts.Add(new SKPoint(cx + r * MathF.Cos(angle), cy + r * MathF.Sin(angle)));
            }
        }

        /// <summary>生成一个倒角矩形封闭轮廓的所有线段（CCW方向）。</summary>
        private static void EmitChamferRectSegments(List<(SKPoint, SKPoint)> result,
            float left, float bottom, float right, float top,
            float cTL, float cTR, float cBR, float cBL)
        {
            if (top - cTL > bottom + cBL)
                result.Add((new SKPoint(left, top - cTL), new SKPoint(left, bottom + cBL)));
            if (cBL > 0.01f)
                result.Add((new SKPoint(left, bottom + cBL), new SKPoint(left + cBL, bottom)));
            if (right - cBR > left + cBL)
                result.Add((new SKPoint(left + cBL, bottom), new SKPoint(right - cBR, bottom)));
            if (cBR > 0.01f)
                result.Add((new SKPoint(right - cBR, bottom), new SKPoint(right, bottom + cBR)));
            if (top - cTR > bottom + cBR)
                result.Add((new SKPoint(right, bottom + cBR), new SKPoint(right, top - cTR)));
            if (cTR > 0.01f)
                result.Add((new SKPoint(right, top - cTR), new SKPoint(right - cTR, top)));
            if (right - cTR > left + cTL)
                result.Add((new SKPoint(right - cTR, top), new SKPoint(left + cTL, top)));
            if (cTL > 0.01f)
                result.Add((new SKPoint(left + cTL, top), new SKPoint(left, top - cTL)));
        }

        /// <summary>
        /// 生成一个圆角矩形封闭轮廓的所有线段（按上→右→下→左方向）。
        /// </summary>
        private static void EmitRoundedRectSegments(List<(SKPoint, SKPoint)> result,
            float left, float bottom, float right, float top,
            float TL, float TR, float BR, float BL)
        {
            if (top - TL > bottom + BL)
                result.Add((new SKPoint(left, top - TL), new SKPoint(left, bottom + BL)));
            if (TL > 0.01f) AddCornerArcSegments(result, left + TL, top - TL, TL, 180f, 90f);
            if (right - BR > left + BL)
                result.Add((new SKPoint(left + BL, bottom), new SKPoint(right - BR, bottom)));
            if (BL > 0.01f) AddCornerArcSegments(result, left + BL, bottom + BL, BL, -90f, -180f);
            if (top - TR > bottom + BR)
                result.Add((new SKPoint(right, bottom + BR), new SKPoint(right, top - TR)));
            if (BR > 0.01f) AddCornerArcSegments(result, right - BR, bottom + BR, BR, 0f, -90f);
            if (right - TR > left + TL)
                result.Add((new SKPoint(right - TR, top), new SKPoint(left + TL, top)));
            if (TR > 0.01f) AddCornerArcSegments(result, right - TR, top - TR, TR, 90f, 0f);
        }

        /// <summary>
        /// 将圆角弧段近似为若干短线段并添加到结果列表
        /// </summary>
        private static void AddCornerArcSegments(List<(SKPoint, SKPoint)> result,
            float cx, float cy, float r, float startDeg, float endDeg)
        {
            const int steps = 16;
            for (int i = 0; i < steps; i++)
            {
                float t1 = i / (float)steps;
                float t2 = (i + 1) / (float)steps;
                double ang1 = (startDeg + (endDeg - startDeg) * t1) * Math.PI / 180.0;
                double ang2 = (startDeg + (endDeg - startDeg) * t2) * Math.PI / 180.0;
                result.Add((
                    new SKPoint(cx + r * (float)Math.Cos(ang1), cy + r * (float)Math.Sin(ang1)),
                    new SKPoint(cx + r * (float)Math.Cos(ang2), cy + r * (float)Math.Sin(ang2))
                ));
            }
        }

        /// <summary>
        /// 将圆角矩形按 margin 向内收缩后离散为多边形（本地坐标系，中心在原点）。
        /// 返回顺序为闭合环（不重复起点）。
        /// </summary>
        private List<SKPoint> BuildInsetPolygon(float margin)
        {
            var localBounds = GetInsetLocalBounds(margin);
            float left = localBounds.Left;
            float right = localBounds.Right;
            float bottom = localBounds.Bottom;
            float top = localBounds.Top;

            var pts = new List<SKPoint>(32);
            if (left >= right || bottom >= top) return pts;

            // 优先处理倒角
            float cTL = Math.Max(0, ChamferTopLeft - margin);
            float cTR = Math.Max(0, ChamferTopRight - margin);
            float cBR = Math.Max(0, ChamferBottomRight - margin);
            float cBL = Math.Max(0, ChamferBottomLeft - margin);
            bool hasChamfer = cTL > 0 || cTR > 0 || cBR > 0 || cBL > 0;

            if (hasChamfer)
            {
                var adj = LimitChamferLengths(left, top, right, bottom, cTL, cTR, cBR, cBL);
                float CTL = adj.TopLeft, CTR = adj.TopRight, CBR = adj.BottomRight, CBL = adj.BottomLeft;

                pts.Add(new SKPoint(left + CTL, top));
                pts.Add(new SKPoint(right - CTR, top));
                if (CTR > 0) pts.Add(new SKPoint(right, top - CTR));
                pts.Add(new SKPoint(right, bottom + CBR));
                if (CBR > 0) pts.Add(new SKPoint(right - CBR, bottom));
                pts.Add(new SKPoint(left + CBL, bottom));
                if (CBL > 0) pts.Add(new SKPoint(left, bottom + CBL));
                pts.Add(new SKPoint(left, top - CTL));
                return pts;
            }

            // 圆角处理
            float rTL = Math.Max(0, CornerRadiusTopLeft - margin);
            float rTR = Math.Max(0, CornerRadiusTopRight - margin);
            float rBR = Math.Max(0, CornerRadiusBottomRight - margin);
            float rBL = Math.Max(0, CornerRadiusBottomLeft - margin);

            var adjR = LimitCornerRadii(left, top, right, bottom, rTL, rTR, rBR, rBL);
            float TL = adjR.TopLeft, TR = adjR.TopRight, BR = adjR.BottomRight, BL = adjR.BottomLeft;

            const int arcSteps = 8; // 每个圆角离散段数

            // 顺序：从左上圆弧终点 (left + TL, top) 开始，沿上→右→下→左 绕一周
            pts.Add(new SKPoint(left + TL, top));
            // 上边 -> 右上圆弧 (90° → 0°)
            pts.Add(new SKPoint(right - TR, top));
            if (TR > 0) AddArcPoints(pts, right - TR, top - TR, TR, 90f, 0f, arcSteps);
            // 右边 -> 右下圆弧 (0° → -90°)
            pts.Add(new SKPoint(right, bottom + BR));
            if (BR > 0) AddArcPoints(pts, right - BR, bottom + BR, BR, 0f, -90f, arcSteps);
            // 下边 -> 左下圆弧 (-90° → -180°)
            pts.Add(new SKPoint(left + BL, bottom));
            if (BL > 0) AddArcPoints(pts, left + BL, bottom + BL, BL, -90f, -180f, arcSteps);
            // 左边 -> 左上圆弧 (180° → 90°)
            pts.Add(new SKPoint(left, top - TL));
            if (TL > 0) AddArcPoints(pts, left + TL, top - TL, TL, 180f, 90f, arcSteps);

            return pts;
        }

        private static void AddArcPoints(List<SKPoint> pts, float cx, float cy, float r,
            float startDeg, float endDeg, int steps)
        {
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                double ang = (startDeg + (endDeg - startDeg) * t) * Math.PI / 180.0;
                pts.Add(new SKPoint(cx + r * (float)Math.Cos(ang), cy + r * (float)Math.Sin(ang)));
            }
        }

        #endregion

        // ── ISnapshotable ──────────────────────────────────────────────────

        public override IShapeMemento CaptureSnapshot()
        {
            return new DrawRectangleMemento(this);
        }

        protected class DrawRectangleMemento : DrawObjectMemento
        {
            private readonly float _localWidth;
            private readonly float _localHeight;
            private readonly float _cornerRadiusTopLeft;
            private readonly float _cornerRadiusTopRight;
            private readonly float _cornerRadiusBottomRight;
            private readonly float _cornerRadiusBottomLeft;
            private readonly float _chamferTopLeft;
            private readonly float _chamferTopRight;
            private readonly float _chamferBottomRight;
            private readonly float _chamferBottomLeft;
            private readonly bool _hasRoundedCorners;
            private readonly bool _hasChamfer;
            private readonly float _originalCornerRadiusTopLeft;
            private readonly float _originalCornerRadiusTopRight;
            private readonly float _originalCornerRadiusBottomRight;
            private readonly float _originalCornerRadiusBottomLeft;
            private readonly float _originalChamferTopLeft;
            private readonly float _originalChamferTopRight;
            private readonly float _originalChamferBottomRight;
            private readonly float _originalChamferBottomLeft;
            private readonly bool _hasOriginalCornerValues;

            public DrawRectangleMemento(DrawRectangle rect) : base(rect)
            {
                _localWidth = rect._localWidth;
                _localHeight = rect._localHeight;
                _cornerRadiusTopLeft = rect.CornerRadiusTopLeft;
                _cornerRadiusTopRight = rect.CornerRadiusTopRight;
                _cornerRadiusBottomRight = rect.CornerRadiusBottomRight;
                _cornerRadiusBottomLeft = rect.CornerRadiusBottomLeft;
                _chamferTopLeft = rect.ChamferTopLeft;
                _chamferTopRight = rect.ChamferTopRight;
                _chamferBottomRight = rect.ChamferBottomRight;
                _chamferBottomLeft = rect.ChamferBottomLeft;
                _hasRoundedCorners = rect.hasRoundedCorners;
                _hasChamfer = rect.hasChamfer;
                _originalCornerRadiusTopLeft = rect._originalCornerRadiusTopLeft;
                _originalCornerRadiusTopRight = rect._originalCornerRadiusTopRight;
                _originalCornerRadiusBottomRight = rect._originalCornerRadiusBottomRight;
                _originalCornerRadiusBottomLeft = rect._originalCornerRadiusBottomLeft;
                _originalChamferTopLeft = rect._originalChamferTopLeft;
                _originalChamferTopRight = rect._originalChamferTopRight;
                _originalChamferBottomRight = rect._originalChamferBottomRight;
                _originalChamferBottomLeft = rect._originalChamferBottomLeft;
                _hasOriginalCornerValues = rect._hasOriginalCornerValues;
            }

            public override void Restore()
            {
                RestoreGeometry();
                if (Shape is DrawRectangle rect)
                {
                    rect._localWidth = _localWidth;
                    rect._localHeight = _localHeight;
                }

                RestoreTransform();
                RestoreDerived();
            }

            protected override void RestoreDerived()
            {
                if (Shape is DrawRectangle rect)
                {
                    rect.CornerRadiusTopLeft = _cornerRadiusTopLeft;
                    rect.CornerRadiusTopRight = _cornerRadiusTopRight;
                    rect.CornerRadiusBottomRight = _cornerRadiusBottomRight;
                    rect.CornerRadiusBottomLeft = _cornerRadiusBottomLeft;
                    rect.ChamferTopLeft = _chamferTopLeft;
                    rect.ChamferTopRight = _chamferTopRight;
                    rect.ChamferBottomRight = _chamferBottomRight;
                    rect.ChamferBottomLeft = _chamferBottomLeft;
                    rect.hasRoundedCorners = _hasRoundedCorners;
                    rect.hasChamfer = _hasChamfer;
                    rect._originalCornerRadiusTopLeft = _originalCornerRadiusTopLeft;
                    rect._originalCornerRadiusTopRight = _originalCornerRadiusTopRight;
                    rect._originalCornerRadiusBottomRight = _originalCornerRadiusBottomRight;
                    rect._originalCornerRadiusBottomLeft = _originalCornerRadiusBottomLeft;
                    rect._originalChamferTopLeft = _originalChamferTopLeft;
                    rect._originalChamferTopRight = _originalChamferTopRight;
                    rect._originalChamferBottomRight = _originalChamferBottomRight;
                    rect._originalChamferBottomLeft = _originalChamferBottomLeft;
                    rect._hasOriginalCornerValues = _hasOriginalCornerValues;
                    rect.SyncDiagonalPointsFromMatrix();
                }
            }
        }
    }
}
