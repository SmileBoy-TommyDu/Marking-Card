using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Rendering;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Windows.Controls;
using System.Windows.Media;


namespace DrSoft.Drawing.Rendering
{
    public class RenderPipeline
    {
        private const int JumpLineHugeSceneThreshold = 200000;
        private readonly RendererDispatcher? _renderer;
        public readonly GridRenderer Grid = new();
        public readonly RulerRenderer Ruler = new();
        public readonly SelectionRenderer Selection = new();
        public readonly PreviewRenderer Preview = new();
        public readonly MachineBoundsRenderer MachineBounds = new();

        private readonly SKPaintCache paintCache = new SKPaintCache();
        private readonly GridPaintCache gridPaintCache = new GridPaintCache();
        Stopwatch st = new Stopwatch();

        public SKPoint MousePoint { get; set; } = new SKPoint();

        // LOD Level-1 简化渲染画笔（以点代替完整图形）
        private static readonly SKPaint _lodDotPaint = new SKPaint
        {
            Style = SKPaintStyle.Fill,
            IsAntialias = false,
        };

        public RenderPipeline(RendererDispatcher renderer)
        {
            _renderer = renderer;
        }

        private bool isDrawing = false;
        private bool isDragControlPoint = false;

        public void Render(
            SKCanvas canvas,
            SKImageInfo info,
            DocumentContext context)
        {
            st.Restart();
            bool deferredDragLockTaken = false;
            try
            {
                // 延后拖拽提交会在后台真实平移图元；若此时渲染线程同时读取 bbox/path/cache，
                // 容易读到半更新状态。这里不再无限期阻塞 UI 线程等待提交锁，
                // 拿不到锁就直接跳过当前帧，让输入和后续帧继续流动。
                if (context.IsApplyingDeferredDragCommit)
                {
                    deferredDragLockTaken = System.Threading.Monitor.TryEnter(
                        context.DeferredDragCommitSyncRoot,
                        millisecondsTimeout: 1);
                    if (!deferredDragLockTaken)
                    {
                        return;
                    }
                }

                if (context.ActiveCanvas == null)
                {
                    // 没有活动画布时，清除为灰色背景
                    canvas.Clear(SKColors.Gray);
                    context.DirtyRect = null;
                    return;
                }

                // 当前画布宿主是 WPF SKElement，PaintSurface 每帧都需要完整重建画面；
                // 不能假定脏区外像素会从上一帧保留下来。DirtyRect 仍保留为调用方标脏信息，
                // 但实际渲染在该后端上强制全量帧，避免新增图形时旧图形被清掉后未重绘。
                bool supportsRetainedPartialRender = false;
                bool partial = supportsRetainedPartialRender &&
                               context.DirtyRect.HasValue &&
                               context.IsPartialRender;

                // 若以后切到真正保留后台缓冲的渲染后端，再恢复局部刷新时，
                // 绘图工具预览和落图过程仍必须强制全量帧。
                if (context.IsDrawing && context.ActiveTool?.ToolType != ToolType.Select)
                {
                    partial = false;
                }

                isDragControlPoint = context.IsDragControlPoint;
                isDrawing = context.IsDrawing || context.IsApplyingDeferredDragCommit;

                SKRect screenDirty = default;
                if (partial)
                {
                    var vp = context.ActiveCanvas.Viewport;
                    var wr = context.DirtyRect!.Value;
                    float s = (float)vp.Scale;
                    // Y 翻转：screenY = -worldY * scale + offsetY
                    float x1 = wr.Left * s + (float)vp.OffsetX;
                    float x2 = wr.Right * s + (float)vp.OffsetX;
                    float y1 = -wr.Bottom * s + (float)vp.OffsetY;
                    float y2 = -wr.Top * s + (float)vp.OffsetY;
                    screenDirty = new SKRect(
                        Math.Min(x1, x2) - 2f,
                        Math.Min(y1, y2) - 2f,
                        Math.Max(x1, x2) + 2f,
                        Math.Max(y1, y2) + 2f);

                    // 裁剪区域与画布完全不交 -> 直接跳过本帧绘制
                    var full = SKRect.Create(0, 0, info.Width, info.Height);
                    if (!screenDirty.IntersectsWith(full))
                    {
                        context.DirtyRect = null;
                        return;
                    }

                    // 局部刷新：设置裁剪区域，限制清除和绘制范围
                    canvas.Save();
                    canvas.ClipRect(screenDirty);
                    canvas.Clear(SKColors.White);

                    //Debug.WriteLine("局部刷新");
                }
                else
                {
                    //Debug.WriteLine("全量刷新");
                    canvas.Clear(SKColors.White);
                }

                // 设置画布尺寸
                if (info.Width > 0 && info.Height > 0)
                {
                    context.ActiveCanvas.Viewport.SetCanvasSize(info.Width, info.Height);
                }


                canvas.Save();
                canvas.Translate(context.ActiveCanvas.Viewport.OffsetX, context.ActiveCanvas.Viewport.OffsetY);
                canvas.Scale(context.ActiveCanvas.Viewport.Scale, -context.ActiveCanvas.Viewport.Scale); // Y轴翻转

                // 0. 机台范围白色区域和边框（在世界坐标系中绘制）
                if (context.ActiveCanvas is DrawingCanvas drawingCanvas)
                {
                    MachineBounds.Render(canvas, (Viewport)context.ActiveCanvas.Viewport, info,
                        drawingCanvas.MachineBounds);
                }

                // 1. Grid
                Grid.Render(canvas, context.ActiveCanvas.Viewport, info, gridPaintCache);

                // 跳扫虚线端点缓存（在世界坐标系中收集，在屏幕坐标系中绘制）
                IReadOnlyList<(SKPoint Start, SKPoint End)>? jumpLineEndpoints = null;

                // 2. Document elements
                void render(IShape shape, SKCanvas canvas, IViewport viewport, SKPaintCache cache)
                {
                    var drawObj = shape as DrawObject;

                    // 若该图形有相交镂空标记，将本地坐标的跳点变换到世界坐标，
                    // 然后用 ClipPath(Difference) 把镂空圈从画布裁剪区域中扣掉。
                    bool clipped = false;
                    if (drawObj != null &&
                        drawObj.IntersectionSkipPoints != null &&
                        drawObj.IntersectionSkipPoints.Count > 0 &&
                        drawObj.IntersectionSkipRadius > 0f)
                    {
                        var transform = drawObj.GetTransformMatrix();
                        canvas.Save();
                        float r = drawObj.IntersectionSkipRadius;
                        foreach (var localPt in drawObj.IntersectionSkipPoints)
                        {
                            // 本地坐标 → 世界坐标
                            var worldPt = transform.MapPoint(localPt);
                            using var circlePath = new SKPath();
                            circlePath.AddCircle(worldPt.X, worldPt.Y, r);
                            canvas.ClipPath(circlePath, SKClipOperation.Difference, true);
                        }

                        clipped = true;
                    }


                    _renderer?.Render(shape, canvas, viewport, cache);


                    if (clipped)
                    {
                        canvas.Restore();

                        // 自交跳点桥接线段：将本地坐标的方向变换到世界坐标，
                        // 沿"over"段方向绘制 2×半径线段，补齐被裁剪的"over"线
                        if (drawObj!.SelfIntersectionSkipCount > 0 &&
                            drawObj.IntersectionSkipBridgeDirections.Count > 0)
                        {
                            var transform = drawObj.GetTransformMatrix();
                            float r = drawObj.IntersectionSkipRadius;
                            float bridgeWidth = drawObj.Pen.StrokeWidth * 6.83f / viewport.Scale;
                            var bridgePaint = cache.GetStrokePaint(drawObj.Pen.Color, bridgeWidth);
                            for (int i = 0;
                                 i < drawObj.SelfIntersectionSkipCount &&
                                 i < drawObj.IntersectionSkipBridgeDirections.Count;
                                 i++)
                            {
                                // 本地交点 → 世界交点
                                var localPt = drawObj.IntersectionSkipPoints[i];
                                var worldPt = transform.MapPoint(localPt);
                                // 本地方向 → 世界方向（MapVector 不应用平移）
                                var localDir = drawObj.IntersectionSkipBridgeDirections[i];
                                var worldDir = transform.MapVector(localDir);
                                // 归一化（变换可能含非均匀缩放）
                                float len = MathF.Sqrt(worldDir.X * worldDir.X + worldDir.Y * worldDir.Y);
                                if (len > 1e-9f)
                                {
                                    worldDir = new SKPoint(worldDir.X / len, worldDir.Y / len);
                                    canvas.DrawLine(
                                        worldPt.X - worldDir.X * r, worldPt.Y - worldDir.Y * r,
                                        worldPt.X + worldDir.X * r, worldPt.Y + worldDir.Y * r,
                                        bridgePaint);
                                }
                            }
                        }
                    }
                }

                //Debug.WriteLine($"1 渲染耗时：{st.ElapsedMilliseconds}ms");
                var viewport = (Viewport)context.ActiveCanvas.Viewport;

                // 计算视口对应的世界坐标矩形
                var viewRect = CalculateViewportWorldRect(context.ActiveCanvas.Viewport, info);
                // 查询区域始终按完整视口走，保证视口缓存能够在拖动/局部刷新阶段命中。
                // 局部刷新只限制实际绘制区域，不应把查询区域也缩成每帧变化的 DirtyRect，
                // 否则会导致每帧都重新跑一次 SpatialGrid.Query。
                var queryRect = viewRect;
                float edgeTolerance = 3f / Math.Max(viewport.Scale, 0.01f);
                queryRect.Inflate(edgeTolerance, edgeTolerance);

                // ── 阶段1：空间索引查询（帧间缓存，查询区域不变时 O(1)）──
                // 局部刷新时后续再按脏区限制实际绘制对象，查询区域保持稳定以复用视口缓存。
                var filteredList = ((DrawingCanvas)context.ActiveCanvas)
                    .GetVisibleDrawObjectsInViewport(queryRect, viewport.Scale, 0f);

                //Debug.WriteLine($"显示图形数量：{filteredList.Count}");
                //Debug.WriteLine($"2 渲染耗时：{st.ElapsedMilliseconds}ms");

                // ── 阶段2：局部刷新 — 脏区过滤（全量刷新时跳过此步）──
                SKRect? worldDirty = partial ? context.DirtyRect : null;
                IEnumerable<DrawObject> renderTargets = filteredList;
                if (worldDirty.HasValue)
                {
                    renderTargets = filteredList.Where(t =>
                    {
                        var bb = t.GetAABB();
                        if (bb.IsEmpty)
                            return true;

                        // 添加少量容差，避免边界处因浮点误差漏绘。
                        bb.Inflate(3f, 3f);
                        return bb.IntersectsWith(worldDirty.Value);
                    });
                }

                //Debug.WriteLine($"3 渲染耗时：{st.ElapsedMilliseconds}ms");

                // ── 阶段3：按 UId 串行渲染（SKCanvas 非线程安全）──
                int n = 0;
                foreach (var item in renderTargets)
                {
                    n++;
                    render(item, canvas, viewport, paintCache);
                    if (context.ShowDirectionArrow)
                    {
                        if (item is DrawingHatch hatch)
                        {
                            foreach (var h in hatch.Children)
                            {
                                DrawDirectionArrowsOnPath(canvas, h as DrawObject, viewport.Scale);
                            }
                        }
                        else
                        {
                            DrawDirectionArrowsOnPath(canvas, item, viewport.Scale);
                        }
                    }
                }

                //Debug.WriteLine($"4 渲染耗时：{st.ElapsedMilliseconds}ms");
                //Debug.WriteLine("渲染图形数量" + n);

                // ── 跳扫虚线：使用全量可见图形（不受视口过滤限制，确保连线完整）──
                // 局部刷新时跳扫虚线需覆盖全场，必须使用未经视口裁剪的原始缓存
                if (context.ShowJumpLine)
                {
                    var activeCanvas = (DrawingCanvas)context.ActiveCanvas;
                    var allVisibleCount = activeCanvas.GetVisibleDrawObjects().Count;
                    if (allVisibleCount < JumpLineHugeSceneThreshold)
                    {
                        jumpLineEndpoints = activeCanvas.GetJumpLineEndpoints(CollectJumpLineEndpoints);
                    }
                }


                // 绘制选中图形的选择框
                // 使用缓存的 SelectedShapes
                int selectedCount = context.ActiveCanvas.SelectedShapeCount;
                if (selectedCount == 1)
                {
                    // 单个选中：绘制单个图形的选择框
                    var selectedDrawObjects = context.ActiveCanvas.Selection.OfType<DrawObject>().ToList();
                    //Selection.RenderHandles(canvas, selectedDrawObjects, context.ActiveCanvas.Viewport);
                    Selection.RenderHandles(canvas, selectedDrawObjects, context.SelectState,
                        context.ActiveCanvas.Viewport);
                }
                else if (selectedCount > 1)
                {
                    // 多选时绘制对齐基准图形的蓝色指示框
                    var lastSelectedShape = (context.ActiveCanvas as DrawingCanvas)?.LastSelectedShape as DrawObject;
                    if (lastSelectedShape != null && lastSelectedShape.IsSelected)
                    {
                        Selection.RenderReferenceShapeIndicator(canvas, lastSelectedShape,
                            context.ActiveCanvas.Viewport);
                    }

                    // 多选：绘制合并的选择框
                    var mergedSelectionBounds = context.CalculateMergedBounds();
                    var selectedDrawObjects = context.ActiveCanvas.Selection.OfType<DrawObject>().ToList();

                    bool hideEdgeMidpoints = false;
                    switch (context.SelectState)
                    {
                        case SelectState.FirstSelected:
                        case SelectState.SecondSelected:
                            {
                                var constraints =
                                    SelectionResizeConstraintResolver.ResolveForSelection(selectedDrawObjects);
                                hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
                            }
                            break;
                        case SelectState.ThirdSelected:
                            {
                                var constraints = SelectionSkewConstraintResolver.ResolveForSelection(selectedDrawObjects);
                                hideEdgeMidpoints = constraints.HasFlag(SelectionResizeConstraint.HideEdgeMidpointHandles);
                            }
                            break;
                    }

                    Selection.RenderMergedHandles(
                        canvas,
                        selectedDrawObjects,
                        mergedSelectionBounds,
                        context.SelectState,
                        context.MergedRotationCenter,
                        context.ActiveCanvas.Viewport,
                        hideEdgeMidpoints);
                }

                // 绘制框选虚线框
                if (context.BoxSelect.IsActive)
                {
                    // 判断框选方向并传递给绘制方法
                    bool isForwardSelection = IsForwardSelection(context.BoxSelect.Start, context.BoxSelect.Current);
                    DrawBoxSelection(canvas, context.BoxSelect.Start, context.BoxSelect.Current,
                        context.ActiveCanvas.Viewport, isForwardSelection);
                }

                // 绘制拖动预览矩形框（如果正在拖动选中的图形）
                // SelectedShapeCount 已缓存，SelectedShapes 已缓存，避免每帧遍历
                if ((context.IsDrawing || context.IsApplyingDeferredDragCommit)
                    && selectedCount > 0 && context.ActiveTool.ToolType == ToolType.Select
                    && !context.ActiveCanvas.Selection.OfType<DrawObject>().Any(it => it.IsLocked))
                {
                    DrawDragPreview(canvas, context);
                }


                // 3. Tool preview
                DrawShape(canvas, (DrawObject)context.CurrentShape, context.ActiveCanvas!.Viewport);
                DrawTextCaretIndicator(canvas, context, context.ActiveCanvas!.Viewport);

                // 3b. Snap indicator: 当多段线绘制时鼠标靠近起始点，绘制吸附指示框
                if (context.IsSnapToStart && context.IsDrawing)
                {
                    DrawSnapIndicator(canvas, context.SnapStartPoint, context.ActiveCanvas!.Viewport.Scale);
                }

                // 恢复画布变换（回到屏幕坐标系）
                canvas.Restore();

                // 局部刷新时恢复 ClipRect（回到无裁剪状态）
                if (partial)
                {
                    canvas.Restore();
                }


                // 在无 ClipRect 限制的状态下，重新设置世界坐标变换并完整绘制跳扫虚线。
                // 这样跳扫虚线不会被局部刷新的 ClipRect 截断，也不会清除其他图形。
                if (context.ShowJumpLine && jumpLineEndpoints != null && jumpLineEndpoints.Count >= 2)
                {
                    canvas.Save();
                    canvas.Translate(context.ActiveCanvas!.Viewport.OffsetX, context.ActiveCanvas!.Viewport.OffsetY);
                    canvas.Scale(context.ActiveCanvas!.Viewport.Scale, -context.ActiveCanvas!.Viewport.Scale);
                    DrawJumpLinesFromEndpoints(canvas, jumpLineEndpoints, (float)context.ActiveCanvas!.Viewport.Scale);
                    canvas.Restore();
                }

                // 4. Ruler（在屏幕坐标系中绘制，最后绘制以显示在最上层）
                Ruler.Render(canvas, context.ActiveCanvas!.Viewport, info);

                // 无论本帧是否实际走局部刷新，都消费掉当前已提交的脏区，
                // 避免一次强制全量帧后旧 DirtyRect 继续影响下一帧筛选。
                context.DirtyRect = null;

                // 渲染结束后恢复默认局部刷新模式；需要全量刷新的调用方会在下一帧前显式置 false。
                context.IsPartialRender = true;
                //Debug.WriteLine($"渲染耗时：{st.ElapsedMilliseconds}ms");
            }
            finally
            {
                if (deferredDragLockTaken)
                {
                    System.Threading.Monitor.Exit(context.DeferredDragCommitSyncRoot);
                }
            }
        }

        /// <summary>
        /// LOD Level-1 简化渲染：屏幕尺寸在 [LodSkipPx, LodDotPx) 区间时，
        /// 以 ~1.5 屏幕像素的圆点代替完整图形路径渲染。
        /// 调用时 canvas 已应用世界坐标变换。
        /// </summary>
        private static void RenderLodDot(SKCanvas canvas, DrawObject obj, float scale)
        {
            _lodDotPaint.Color = obj.Pen?.Color ?? SKColors.Black;
            // 世界坐标半径 ≈ 1.5 屏幕像素
            float r = 1.5f / scale;
            canvas.DrawCircle(obj.SharpCenter.X, obj.SharpCenter.Y, r, _lodDotPaint);
        }

        private void DrawShape(SKCanvas canvas, DrawObject? shape, IViewport viewport)
        {
            if (shape == null)
            {
                return;
            }

            if (shape is DrawText)
            {
                _renderer?.Render(shape, canvas, viewport, paintCache);
                return;
            }

            var zoom = (float)viewport.Scale;
            var strokePaint = paintCache.GetStrokePaint(shape.Pen.Color, shape.Pen.StrokeWidth * 6.83f / zoom);
            try
            {
                _renderer?.PreviewRender(shape, canvas, strokePaint, paintCache);
            }
            finally
            {
                paintCache.ReturnStrokePaint(strokePaint);
                st.Stop();
            }
        }

        private void DrawTextCaretIndicator(SKCanvas canvas, DocumentContext context, IViewport viewport)
        {
            var activeTool = context.ActiveTool;
            var textTool = activeTool as ToolText;
            if (textTool == null)
            {
                return;
            }

            var shouldShowCaret = textTool.ShouldShowCaretIndicator;
            if (!shouldShowCaret)
            {
                return;
            }

            var hasCaretSegment = textTool.TryGetCaretSegment(out var startPoint, out var endPoint);
            if (!hasCaretSegment)
            {
                return;
            }

            var zoom = (float)viewport.Scale;
            using var caretPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Black,
                StrokeWidth = 1.2f / zoom,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };

            canvas.DrawLine(startPoint, endPoint, caretPaint);
        }

        /// <summary>
        /// 在多段线起始点绘制吸附指示框（一个带有对角线的正方形框）。
        /// 框的大小随视口缩放调整，保持屏幕上的视觉大小一致。
        /// </summary>
        private void DrawSnapIndicator(SKCanvas canvas, SKPoint startPoint, float zoom)
        {
            // 吸附指示框的屏幕像素半径
            const float boxScreenRadius = 6f;
            // 转换为世界坐标半径
            float worldRadius = boxScreenRadius / zoom;

            // 绘制正方形指示框
            using var boxPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(0, 0, 0), // 蓝色，与选择框颜色一致
                StrokeWidth = 1.5f / zoom,
                IsAntialias = true
            };
            canvas.DrawRect(
                startPoint.X - worldRadius,
                startPoint.Y - worldRadius,
                worldRadius * 2,
                worldRadius * 2,
                boxPaint);

            // 绘制对角线标记（增强视觉辨识度）
            float crossRadius = worldRadius * 0.5f;
            using var crossPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(0, 0, 0),
                StrokeWidth = 1f / zoom,
                IsAntialias = true
            };
            canvas.DrawLine(
                startPoint.X - crossRadius, startPoint.Y,
                startPoint.X + crossRadius, startPoint.Y,
                crossPaint);
            canvas.DrawLine(
                startPoint.X, startPoint.Y - crossRadius,
                startPoint.X, startPoint.Y + crossRadius,
                crossPaint);
        }

        /// <summary>
        /// 在图形轨迹上绘制激光加工方向箭头（IsClockwise=true 顺时针，false 逆时针）。
        /// </summary>
        private void DrawDirectionArrowsOnPath(SKCanvas canvas, DrawObject? shape, float zoom)
        {
            if (shape == null) return;
            // 点图形无方向概念，跳过
            if (shape is DrawDot) return;

            SKPath? localPath = null;
            try
            {
                localPath = shape.GetPath();
                if (localPath == null || localPath.IsEmpty) return;

                // 转换到世界坐标
                using var worldPath = new SKPath(localPath);
                worldPath.Transform(shape.GetTransformMatrix());

                // forceClosed=false：对多段线、线段等开放路径按真实轨迹长度测量，
                // 避免采样点落在 SKPathMeasure 虚拟连接起止点的闭合段上导致箭头偏离轨迹
                using var measure = new SKPathMeasure(worldPath, false, 1f);
                bool clockwise = shape.IsClockwise;

                // 箭头画笔：线宽与尺寸随缩放适配（增大显示）
                float strokeWidth = 1.5f / zoom;
                float arrowSize = 8f / zoom; // 箭头半长（世界单位）
                using var arrowPaint = new SKPaint
                {
                    Color = new SKColor(255, 60, 0),
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = strokeWidth,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true
                };

                // 逐段遍历（一个图形的 SKPath 可能包含多个轮廓）
                do
                {
                    float length = measure.Length;
                    if (length <= arrowSize * 2) continue;

                    // 根据长度决定箭头数量：每段大致 1~4 个，最多 6 个
                    int arrowCount = Math.Clamp((int)(length / 20f), 1, 6);
                    for (int i = 0; i < arrowCount; i++)
                    {
                        float t = (i + 0.5f) / arrowCount; // 在每段轨迹上均匀分布
                        float distance = t * length;
                        if (!measure.GetPositionAndTangent(distance, out SKPoint pos, out SKPoint tangent)) continue;

                        // 方向向量：
                        // 按图形类型确定路径的“自然方向”：
                        // - DrawCircle/DrawRectangle 由 Skia 内置的 AddOval/AddRect 生成，自然方向为逆时针
                        // - 其他图形（多段线、多边形、文本、贝塞尔、圆弧、线段等）由绘制点顺序决定，
                        //   约定自然方向为顺时针
                        // 若用户期望的 IsClockwise 与自然方向不一致，则反转切线以让箭头朝相反方向显示
                        // DrawCircle / DrawRectangle（直角）由 Skia 内置 AddOval/AddRect 生成，自然方向为逆时针
                        // 圆角/倒角矩形由 MoveTo/LineTo/ArcTo 构建，自然方向为顺时针
                        bool naturalClockwise;
                        if (shape is DrawRectangle dr)
                            naturalClockwise = dr.HasNonRectangularCorners();
                        else
                            naturalClockwise = !(shape is DrawCircle);
                        bool needReverse = clockwise != naturalClockwise;
                        float dx = needReverse ? -tangent.X : tangent.X;
                        float dy = needReverse ? -tangent.Y : tangent.Y;

                        DrawArrow(canvas, pos, dx, dy, arrowSize, arrowPaint);
                    }
                } while (measure.NextContour());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DrawDirectionArrowsOnPath 错误: {ex.Message}");
            }
            finally
            {
                localPath?.Dispose();
            }
        }

        /// <summary>
        /// 在指定点沿切线方向绘制箭头（V 形）。
        /// </summary>
        private static void DrawArrow(SKCanvas canvas, SKPoint pos, float dx, float dy, float size, SKPaint paint)
        {
            // dx, dy 已为单位向量（SKPathMeasure 返回的切线为单位向量），容错再归一化
            float mag = MathF.Sqrt(dx * dx + dy * dy);
            if (mag < 1e-6f) return;
            dx /= mag;
            dy /= mag;

            // 箭头两翼（40 度张角）
            const float cos = 0.766f; // cos(40°)
            const float sin = 0.643f; // sin(40°)

            // 箭尖向前偏移半个大小，使箭头以当前点为中心
            float tipX = pos.X + dx * size * 0.5f;
            float tipY = pos.Y + dy * size * 0.5f;

            // 两翼端点（从箭尖向后张开）
            float bx1 = tipX - (dx * cos - dy * sin) * size;
            float by1 = tipY - (dy * cos + dx * sin) * size;
            float bx2 = tipX - (dx * cos + dy * sin) * size;
            float by2 = tipY - (dy * cos - dx * sin) * size;

            canvas.DrawLine(bx1, by1, tipX, tipY, paint);
            canvas.DrawLine(bx2, by2, tipX, tipY, paint);
        }

        /// <summary>
        /// 收集跳扫虚线的端点列表（世界坐标）。
        /// 入参为所有可见图形（未经脏区过滤），以保证局部刷新时跳扫虚线端点链完整、可被正确重新渲染。
        /// </summary>
        private List<(SKPoint Start, SKPoint End)> CollectJumpLineEndpoints(IEnumerable<DrawObject> shapes)
        {
            var endpoints = new List<(SKPoint Start, SKPoint End)>();
            if (shapes == null) return endpoints;

            // 仅取启用了跳扫虚线的图形，按 UId 全局排序，作为端点链的连接顺序
            var ordered = shapes.Where(x => x.ShowJumpLine).OrderBy(x => x.UId).ToList();
            if (ordered.Count < 2) return endpoints;

            foreach (var shape in ordered)
            {
                try
                {
                    // 直角矩形特殊处理：起点和终点都使用左上角世界坐标
                    // 圆角/倒角矩形走下方通用路径（从 GetPath 提取首尾点）
                    if (shape is DrawRectangle rect
                        && rect.CornerRadiusTopLeft <= 0 && rect.CornerRadiusTopRight <= 0
                        && rect.CornerRadiusBottomRight <= 0 && rect.CornerRadiusBottomLeft <= 0
                        && rect.ChamferTopLeft <= 0 && rect.ChamferTopRight <= 0
                        && rect.ChamferBottomRight <= 0 && rect.ChamferBottomLeft <= 0)
                    {
                        //var localTopLeft = new SKPoint(-rect.Width / 2, rect.Height / 2);
                        //var worldTopLeft = rect.GetTransformMatrix().MapPoint(localTopLeft);
                        SKPoint start = new SKPoint(rect.OutlinePoints[0].X, rect.OutlinePoints[0].Y);  
                        endpoints.Add((start,start));
                        continue;
                    }

                    // 圆特殊处理：起点和终点都使用左侧点世界坐标（角度 180° 处）
                    if (shape is DrawCircle circle)
                    {
                        var localLeft = new SKPoint(-circle.DrawingRadiusX, 0);
                        var worldLeft = circle.GetTransformMatrix().MapPoint(localLeft);
                        endpoints.Add((worldLeft, worldLeft));
                        continue;
                    }

                    // 点特殊处理：起点和终点都使用点的世界坐标。
                    // DrawDot 的 Points[0] 是提交变换后的权威位置，不能使用可能尚未同步的 SharpCenter。
                    if (shape is DrawDot dot)
                    {
                        if (dot.Points.Count > 0)
                        {
                            var point = dot.Points[0];
                            endpoints.Add((point, point));
                        }

                        continue;
                    }

                    using var localPath = shape.GetPath();
                    if (localPath == null || localPath.IsEmpty) continue;

                    using var worldPath = new SKPath(localPath);
                    worldPath.Transform(shape.GetTransformMatrix());

                    using var measure = new SKPathMeasure(worldPath, false, 1f);

                    SKPoint? firstPoint = null;
                    SKPoint lastPoint = SKPoint.Empty;

                    do
                    {
                        float length = measure.Length;
                        if (length <= 0) continue;

                        if (!firstPoint.HasValue)
                        {
                            if (measure.GetPosition(0, out var startPos))
                            {
                                firstPoint = startPos;
                            }
                        }

                        if (measure.GetPosition(length, out var endPos))
                        {
                            lastPoint = endPos;
                        }
                    } while (measure.NextContour());

                    if (firstPoint.HasValue)
                    {
                        endpoints.Add((firstPoint.Value, lastPoint));
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CollectJumpLineEndpoints 获取图形端点错误: {ex.Message}");
                }
            }

            return endpoints;
        }


        /// <summary>
        /// 使用已收集的端点在世界坐标系中绘制跳扫虚线。
        /// </summary>
        private void DrawJumpLinesFromEndpoints(SKCanvas canvas, IReadOnlyList<(SKPoint Start, SKPoint End)> endpoints,
            float zoom)
        {
            if (endpoints == null || endpoints.Count < 2) return;

            float strokeWidth = 1f / zoom;
            float dashLength = 4f / zoom;
            using var jumpPaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(180, 180, 180),
                StrokeWidth = strokeWidth,
                PathEffect = SKPathEffect.CreateDash(new float[] { dashLength, dashLength }, 0),
                IsAntialias = true
            };

            for (int i = 1; i < endpoints.Count; i++)
            {
                var prevEnd = endpoints[i - 1].End;
                var currStart = endpoints[i].Start;
                canvas.DrawLine(prevEnd, currStart, jumpPaint);
            }
        }


        private void DrawBoxSelection(SKCanvas canvas, SKPoint boxSelectStart, SKPoint boxSelectCurrent,
            IViewport viewport, bool isForwardSelection)
        {
            // 使用SelectionRenderer渲染框选虚线框，传递框选方向和视口
            Selection.RenderRubberBand(canvas, boxSelectStart, boxSelectCurrent, viewport, isForwardSelection);
        }

        /// <summary>
        /// 绘制拖动预览矩形框
        /// </summary>
        private void DrawDragPreview(SKCanvas canvas, DocumentContext context)
        {
            if (context.ActiveCanvas == null) return;

            // 计算移动偏移量
            float dx = context.BoxSelect.Current.X - context.BoxSelect.Start.X;
            float dy = context.BoxSelect.Current.Y - context.BoxSelect.Start.Y;

            if (Math.Abs(dx) < 0.1 && Math.Abs(dy) < 0.1) return; // 几乎没有移动，不绘制预览

            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(255, 91, 0),
                StrokeWidth = 0.5f * 4.8f / context.ActiveCanvas.Viewport.Scale,
                IsAntialias = true,
                PathEffect =
                    SKPathEffect.CreateDash(
                        new float[]
                        {
                            3.42f / context.ActiveCanvas.Viewport.Scale, 2.05f / context.ActiveCanvas.Viewport.Scale
                        }, 0),
            };

            // 拖动会话开始时已经缓存了正式选择框角点；渲染阶段优先平移这组角点，
            // 避免旋转组合/群组被当前 AABB bounds 或暂时未归一的 SelectedShapes 覆盖。
            if (context.CachedDragPreviewCorners is { Length: > 0 } cachedCorners)
            {
                DrawTranslatedSelectionPath(canvas, cachedCorners, dx, dy, strokePaint);
                return;
            }

            var selectedDrawObjects = context.ActiveCanvas.Selection.OfType<DrawObject>().ToList();
            if (selectedDrawObjects.Count == 1)
            {
                // 单选拖动预览必须平移拖动开始时的正式选中框角点。
                // 旋转组合/群组如果在渲染阶段重新从 bounds 推导，容易退回轴对齐 AABB。
                var corners = SelectionGeometryBuilder.BuildForSinglePreviewOBBSelection(
                    selectedDrawObjects[0]).Corners;
                DrawTranslatedSelectionPath(canvas, corners, dx, dy, strokePaint);
                return;
            }

            var cached = context.CachedDragPreviewBounds;
            var mergedBounds = cached.HasValue && !cached.Value.IsEmpty
                ? cached.Value
                : context.CalculateMergedBounds();
            if (mergedBounds.IsEmpty)
                return;

            var previewRect = new SKRect(
                mergedBounds.Left + dx,
                mergedBounds.Top + dy,
                mergedBounds.Right + dx,
                mergedBounds.Bottom + dy);
            var mergedGeometry = SelectionGeometryBuilder.BuildForMergedBounds(
                previewRect,
                (float)context.ActiveCanvas.Viewport.Scale);
            DrawTranslatedSelectionPath(canvas, mergedGeometry.Corners, 0f, 0f, strokePaint);
        }

        private static void DrawTranslatedSelectionPath(SKCanvas canvas, SKPoint[] corners, float dx, float dy,
            SKPaint paint)
        {
            if (corners.Length == 0)
                return;

            using var path = new SKPath();
            path.MoveTo(corners[0].X + dx, corners[0].Y + dy);
            for (int i = 1; i < corners.Length; i++)
            {
                path.LineTo(corners[i].X + dx, corners[i].Y + dy);
            }

            path.Close();
            canvas.DrawPath(path, paint);
        }

        /// <summary>
        /// 判断是否为正向框选（左上→右下）
        /// </summary>
        private bool IsForwardSelection(SKPoint start, SKPoint current)
        {
            return current.X > start.X && current.Y > start.Y;
        }

        /// <summary>
        /// 计算视口在世界坐标系中的范围，用于视口预过滤。
        /// 屏幕坐标与世界坐标的转换关系：
        ///   screenX = worldX * scale + offsetX
        ///   screenY = -worldY * scale + offsetY
        /// </summary>
        private static SKRect CalculateViewportWorldRect(IViewport viewport, SKImageInfo info)
        {
            float s = (float)viewport.Scale;
            if (s <= 1e-6f) return SKRect.Empty;

            // worldX = (screenX - offsetX) / scale
            // worldY = -(screenY - offsetY) / scale
            float worldLeft = (0 - viewport.OffsetX) / s;
            float worldRight = (info.Width - viewport.OffsetX) / s;
            float worldTop = -(0 - viewport.OffsetY) / s; // screenY=0（屏幕上边）=> worldY 最大（世界上方）
            float worldBottom = -(info.Height - viewport.OffsetY) / s; // screenY=Height（屏幕下边）=> worldY 最小（世界下方）

            // SKRect 要求 Top < Bottom，worldBottom 是较小的 Y（下方），worldTop 是较大的 Y（上方），符合要求
            return new SKRect(worldLeft, worldBottom, worldRight, worldTop);
        }
    }


    public class PreviewRenderer
    {
        private static readonly SKPaint _previewPaint = new()
        {
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(80, 80, 80, 180),
            StrokeWidth = 1f,
            IsAntialias = true,
            PathEffect = SKPathEffect.CreateDash(new float[] { 8, 4 }, 0),
        };
    }

    public class SKPaintCache : IDisposable
    {
        private readonly Dictionary<uint, SKPaint> _fillPaintPool = new Dictionary<uint, SKPaint>();
        private readonly Dictionary<ulong, SKPaint> _strokePaintPool = new Dictionary<ulong, SKPaint>();
        private readonly Stack<SKPath> _pathPool = new Stack<SKPath>();
        private readonly Dictionary<float, SKPaint> _selectionPaintPool = new Dictionary<float, SKPaint>();
        private SKPaint? _handleFillPaint;
        private readonly Dictionary<float, SKPaint> _handleStrokePaintPool = new Dictionary<float, SKPaint>();

        private uint GetFillKey(SKColor color) => ((uint)color.Alpha << 24) | ((uint)color.Red << 16) |
                                                  ((uint)color.Green << 8) | color.Blue;

        private ulong GetStrokeKey(SKColor color, float width) =>
            ((ulong)((uint)color.Alpha << 24 | (uint)color.Red << 16 | (uint)color.Green << 8 | color.Blue) << 32) |
            (ulong)BitConverter.SingleToInt32Bits(width);

        public SKPaint GetFillPaint(SKColor color)
        {
            var key = GetFillKey(color);
            if (!_fillPaintPool.TryGetValue(key, out var paint))
            {
                paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                _fillPaintPool[key] = paint;
            }

            return paint;
        }

        public SKPaint GetStrokePaint(SKColor color, float strokeWidth)
        {
            var key = GetStrokeKey(color, strokeWidth);
            if (!_strokePaintPool.TryGetValue(key, out var paint))
            {
                paint = new SKPaint
                {
                    Color = color,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = strokeWidth,
                    IsAntialias = true,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round
                };
                _strokePaintPool[key] = paint;
            }

            return paint;
        }

        public SKPath GetPath()
        {
            if (_pathPool.Count > 0)
            {
                var path = _pathPool.Pop();
                path.Reset();
                return path;
            }

            return new SKPath();
        }

        public SKPaint GetSelectionPaint(float zoom)
        {
            var key = zoom;
            if (!_selectionPaintPool.TryGetValue(key, out var paint))
            {
                paint = new SKPaint
                {
                    Color = SKColors.DodgerBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1 / zoom,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4 / zoom, 4 / zoom }, 0)
                };
                _selectionPaintPool[key] = paint;
            }

            return paint;
        }

        public SKPaint GetHandleFillPaint()
        {
            if (_handleFillPaint == null)
            {
                _handleFillPaint = new SKPaint
                {
                    Color = SKColors.White,
                    Style = SKPaintStyle.Fill
                };
            }

            return _handleFillPaint;
        }

        public SKPaint GetHandleStrokePaint(float zoom)
        {
            if (!_handleStrokePaintPool.TryGetValue(zoom, out var paint))
            {
                paint = new SKPaint
                {
                    Color = SKColors.DodgerBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1 / zoom
                };
                _handleStrokePaintPool[zoom] = paint;
            }

            return paint;
        }

        // ✅ 新增：框选虚线画笔
        private readonly Dictionary<float, SKPaint> _boxSelectPaintPool = new Dictionary<float, SKPaint>();

        public SKPaint GetBoxSelectPaint(float zoom)
        {
            if (!_boxSelectPaintPool.TryGetValue(zoom, out var paint))
            {
                paint = new SKPaint
                {
                    Color = SKColors.DodgerBlue,
                    Style = SKPaintStyle.Stroke,
                    StrokeWidth = 1 / zoom,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 4 / zoom, 4 / zoom }, 0),
                    IsAntialias = true
                };
                _boxSelectPaintPool[zoom] = paint;
            }

            return paint;
        }

        // 创建虚线效果的 SKPaint
        public SKPaint GetPreviewPaint(float strokeWidth)
        {
            return new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                Color = new SKColor(0x80, 0x80, 0x80, 0x80),
                PathEffect = SKPathEffect.CreateDash(new float[] { 2f, 1f }, 0f),
            };
        }


        public void ReturnFillPaint(SKPaint paint)
        {
            /* 归还到池，无需操作 */
        }

        public void ReturnStrokePaint(SKPaint paint)
        {
            /* 归还到池，无需操作 */
        }

        public void ReturnPath(SKPath path) => _pathPool.Push(path);

        public void ReturnSelectionPaint(SKPaint paint)
        {
            /* 归还到池，无需操作 */
        }

        public void ReturnHandleFillPaint(SKPaint paint)
        {
            /* 归还到池，无需操作 */
        }

        public void ReturnHandleStrokePaint(SKPaint paint)
        {
            /* 归还到池，无需操作 */
        }

        public void ReturnBoxSelectPaint(SKPaint paint)
        {
            /* 归还到池，无需操作 */
        }

        public void Dispose()
        {
            foreach (var paint in _fillPaintPool.Values) paint.Dispose();
            foreach (var paint in _strokePaintPool.Values) paint.Dispose();
            foreach (var path in _pathPool) path.Dispose();
            foreach (var paint in _selectionPaintPool.Values) paint.Dispose();
            _handleFillPaint?.Dispose();
            foreach (var paint in _handleStrokePaintPool.Values) paint.Dispose();
            foreach (var paint in _boxSelectPaintPool.Values) paint.Dispose();

            _fillPaintPool.Clear();
            _strokePaintPool.Clear();
            _pathPool.Clear();
            _selectionPaintPool.Clear();
            _handleStrokePaintPool.Clear();
            _boxSelectPaintPool.Clear();
        }
    }

    public class GridPaintCache : IDisposable
    {
        private readonly Dictionary<float, SKPaint> _gridPaintPool = new Dictionary<float, SKPaint>();
        private readonly Dictionary<float, SKPaint> _axisPaintPool = new Dictionary<float, SKPaint>();

        public SKPaint GetGridPaint(float zoom)
        {
            var key = zoom;
            if (!_gridPaintPool.TryGetValue(key, out var paint))
            {
                paint = new SKPaint
                {
                    Color = new SKColor(230, 230, 230),
                    StrokeWidth = 1 / zoom
                };
                _gridPaintPool[key] = paint;
            }

            return paint;
        }

        public SKPaint GetAxisPaint(float zoom)
        {
            var key = zoom;
            if (!_axisPaintPool.TryGetValue(key, out var paint))
            {
                paint = new SKPaint
                {
                    Color = new SKColor(200, 200, 200),
                    StrokeWidth = 2 / zoom
                };
                _axisPaintPool[key] = paint;
            }

            return paint;
        }

        public void Dispose()
        {
            foreach (var paint in _gridPaintPool.Values) paint.Dispose();
            foreach (var paint in _axisPaintPool.Values) paint.Dispose();
            _gridPaintPool.Clear();
            _axisPaintPool.Clear();
        }
    }
}
