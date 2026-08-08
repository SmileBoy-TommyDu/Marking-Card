using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Clipboard;
using DrSoft.Drawing.Controls.Commands;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Event.Tool;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Utility;
using SkiaSharp;
using System.Diagnostics;

namespace DrSoft.Drawing.Controls.Models
{
    public partial class DrawingCanvas
    {
        public Dictionary<string, bool> EditCommandsStatus = new();

        #region 检测
        /// <summary>
        /// 检查选中图形中是否存在未锁定的图形可操作。
        /// 仅当所有选中图形都锁定时才返回失败，允许对未锁定图形执行变换。
        /// </summary>
        private GraphicResult CheckHasUnlockedShapes()
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;
            return Context.ActiveCanvas!.Selection.All(s => s.IsLocked)
                ? GraphicResult.Fail(GraphicErrorCode.ShapeLocked)
                : GraphicResult.Ok();
        }

        /// <summary>
        /// 兼容旧调用：行为与 CheckHasUnlockedShapes 一致。
        /// </summary>
        private GraphicResult CheckNoLockedShapes() => CheckHasUnlockedShapes();

        private GraphicResult CheckCanvas() =>
        Context.ActiveCanvas == null
        ? GraphicResult.Fail(GraphicErrorCode.CanvasNotFound)
        : GraphicResult.Ok();

        private GraphicResult CheckSelectionNotEmpty()
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;
            return !Context.ActiveCanvas!.Selection.Any()
                ? GraphicResult.Fail(GraphicErrorCode.NothingSelected)
                : GraphicResult.Ok();
        }

        /// <summary>组合前置校验：画布可用 + 有选中 + 有未锁定 + 数量足够</summary>
        private GraphicResult CheckForMultiShapeOp(int minCount = 2)
        {
            var check = CheckHasUnlockedShapes();
            if (!check.IsSuccess) return check;
            int unlockedCount = Context.ActiveCanvas!.Selection.Count(s => !s.IsLocked);
            return unlockedCount < minCount
                ? GraphicResult.Fail(GraphicErrorCode.InsufficientSelection,
                    $"至少需要 {minCount} 个未锁定对象")
                : GraphicResult.Ok();
        }

        #endregion

        #region 编辑相关命令实现

        internal GraphicResult Undo()
        {
            Context.ActiveCanvas?.CommandManager.Undo();
            RefreshNodeSelectionVisualStateAfterHistoryReplay();
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult Redo()
        {
            if (Context.ShowJumpLine) Context.IsPartialRender = false;
            Context.ActiveCanvas?.CommandManager.Redo();
            RefreshNodeSelectionVisualStateAfterHistoryReplay();
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        /// <summary>
        /// TODO：实现妥协，节点选中可视状态还没有被建模成一个完整、统一的历史回放后状态
        /// </summary>
        private void RefreshNodeSelectionVisualStateAfterHistoryReplay()
        {
            var activeTool = Context.ActiveTool;
            if (activeTool is not ToolSelect toolSelect)
            {
                return;
            }

            toolSelect.RefreshPathNodeSelectionVisualState();
        }

        internal GraphicResult Copy()
        {
            var canvas = Context.ActiveCanvas!;
            var copyTargets = canvas.Selection.CollectCopyTargets();
            if (copyTargets.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeLocked);

            DrawingClipboard.Instance.Set(copyTargets);
            EditCommandsStatus["Paste"] = DrawingClipboard.Instance.HasContent;

            return GraphicResult.Ok();
        }

        internal GraphicResult Cut()
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var selectedShapes = Context.ActiveCanvas!.Selection
                .CollectUnlockedShapes();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            // 1. 写入剪贴板
            DrawingClipboard.Instance.Set(selectedShapes);
            EditCommandsStatus["Paste"] = true;

            // 2. 从画布移除（支持撤销）
            var canvas = Context.ActiveCanvas! as DrawingCanvas;
            canvas.ExecuteSelectionRemoval(
                selectedShapes,
                resetPartialRenderWhenJumpLineVisible: true,
                requestRedraw: true,
                publishSelectChanged: true);

            return GraphicResult.Ok();
        }

        internal GraphicResult Paste(bool useMousePosition = true, bool suppressSelectionPublish = false)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            if (!DrawingClipboard.Instance.HasContent)
                return GraphicResult.Fail(GraphicErrorCode.ClipboardNotAvailable);

            var copies = DrawingClipboard.Instance.Paste(useMousePosition);
            EditCommandsStatus["Paste"] = false;

            ((DrawingCanvas)Context.ActiveCanvas).ExecuteLayerAdd(
                ((DrawingCanvas)Context.ActiveCanvas).ActiveLayer,
                copies,
                resetPartialRenderWhenJumpLineVisible: true,
                invokeRedraw: true,
                suppressSelectionPublish: suppressSelectionPublish);

            return GraphicResult.Ok();
        }

        internal GraphicResult Delete()
        {
            var check = CheckCanvas().IsSuccess && CheckSelectionNotEmpty().IsSuccess;
            if (!check) return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);
            var shapesToDelete = Context.ActiveCanvas.Selection
                .CollectUnlockedShapes();

            if (shapesToDelete != null && shapesToDelete.Count() > 0)
            {
                var canvas = (DrawingCanvas)Context.ActiveCanvas;
                canvas.ExecuteSelectionRemoval(
                    shapesToDelete,
                    resetPartialRenderWhenJumpLineVisible: true,
                    requestRedraw: true,
                    publishSelectChanged: true);
            }
            return GraphicResult.Ok();
        }

        internal GraphicResult Replace()
        {
            throw new NotImplementedException();
        }

        internal GraphicResult ChangeSelectedState(int state)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            Context.ChangeSelectedState(state);
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult UpdateRotateCenterIcon(double x, double y, bool isShow)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;
            if (Context.ActiveCanvas.Selection.Count > 0)
            {
                Context.IsAnchorPositionShow = isShow;
                if (Context.ActiveCanvas.Selection.Count > 1)
                {
                    Context.MergedRotationCenter = new SKPoint((float)x, (float)y);
                }
                else
                    Context.ActiveCanvas.Selection.FirstOrDefault()?.SetRotationCenter(new SKPoint((float)x, (float)y));

                Context.RequestRedraw();
            }
            else
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "未选中任何图形");
            return GraphicResult.Ok();
        }

        internal GraphicResult UpdateSkewCenterIcon(double x, double y, bool isShow)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;
            if (Context.ActiveCanvas.Selection.Count > 0)
            {
                //if (Context.ActiveCanvas.SelectedShapes.Count > 1)
                //{
                //    Context.AnchorPosition = new SKPoint((float)x, (float)y);
                //}
                //else
                //    Context.ActiveCanvas.SelectedShapes.FirstOrDefault()?.SkewCenter = new SKPoint((float)x, (float)y);

                Context.AnchorPosition = new SKPoint((float)x, (float)y);

                Context.IsAnchorPositionShow = isShow;

                Context.RequestRedraw();
            }
            else
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "未选中任何图形");
            return GraphicResult.Ok();
        }



        //internal GraphicResult SetMachineBounds(float width, float height)
        //{
        //    if (Context.ActiveCanvas != null && width > 0 && height > 0)
        //    {
        //        Context.ActiveCanvas.MachineBounds = new Rect2D(-width / 2, -height / 2, width, height);
        //        Context.DefaultMachineBounds = new Rect2D(-width / 2, -height / 2, width, height);
        //    }
        //    Context.RequestRedraw(); 
        //    return GraphicResult.Ok();
        //}
        //internal GraphicResult SetGridSize(float width, float height)
        //{
        //    if (width >= 0f)
        //    {
        //        DocumentContext.Instance.GridSizeX = width;
        //    }
        //    if (height >= 0f)
        //    {
        //        DocumentContext.Instance.GridSizeY = height;
        //    }
        //    return GraphicResult.Ok();
        //}

        //internal GraphicResult SetMicroMove(float MicroMoveX, float MicroMoveY)
        //{
        //    if (MicroMoveX > 0f)
        //    {
        //        DocumentContext.Instance.KeysMoveSharpsStepX = MicroMoveX;
        //    }
        //    if (MicroMoveY > 0f)
        //    {
        //        DocumentContext.Instance.KeysMoveSharpsStepY = MicroMoveY;
        //    }
        //    return GraphicResult.Ok();
        //}
        internal GraphicResult SelectAll()
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;
            foreach (var item in Context.ActiveCanvas.AllShapes)
            {
                item.IsSelected = true;
            }
            Context.ActiveCanvas!.SetSelectedShapes();
            Context.SelectState = SelectState.FirstSelected;
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult SelectInvert()
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;
            foreach (var item in Context.ActiveCanvas.AllShapes)
            {
                item.IsSelected = !item.IsSelected;
            }
            Context.ActiveCanvas!.SetSelectedShapes();
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult ClearSelection()
        {
            throw new NotImplementedException();
        }


        #endregion

        #region 节点编辑方法
        internal GraphicResult EditNodes(bool turnOn)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            if (turnOn)
            {
                check = CheckSelectionNotEmpty();
                if (!check.IsSuccess) return check;
            }

            var toolSelect = Context.ActiveTool as ToolSelect;
            if (turnOn)
            {
                toolSelect?.EnterNodeEditMode();
                // EnterNodeEditMode 内部已设置 _context.IsNodeEditing = true
                // 并调用 PublishNodeEditStateChanged() 发布 EditNodesModeChangedEvent。
                // 不再重复设置 Context.IsNodeEditing 和调用 PublishNodeEditModeChanged()，
                // 否则会发布第二个 EditNodesModeChangedEvent，触发 CanvasViewModel
                // 再次执行 SelectTool("Node")，形成事件风暴。
            }
            else
            {
                toolSelect?.ExitNodeEditMode();
                // ExitNodeEditMode 内部已设置 _context.IsNodeEditing = false
                // 并调用 PublishNodeEditStateChanged() 发布 EditNodesModeChangedEvent。
                Context.SelectedSeparateNodeWorldPosition = null;
                Context.SelectedMoveNodeWorldPosition = null;
            }

            foreach (var item in Context.ActiveCanvas!.Selection)
            {
                item.IsPathEditing = turnOn;
            }

            // IsPathEditing 变更后发布 SelectChanged，让 EditPathNodesToolViewModel
            // 通过 AllPathEditing 同步 _isNodeEditing 状态。
            // 放在 EnterNodeEditMode/ExitNodeEditMode 之后，确保 SelectChanged
            // 携带的 AllPathEditing 值与 EditNodesModeChangedEvent 一致。
            Context.PublishSelectChanged();
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult AddNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;
            toolSelect?.SetNodeEditSubMode(turnOn ? NodeEditSubMode.Add : NodeEditSubMode.None);
            // 不再直接设置 Context.NodeEditSubMode 和调用 PublishNodeEditModeChanged()，
            // 因为 PathNodeEditSession.SetNodeEditSubMode 已经内部完成状态设置和事件发布。
            // 重复发布会导致 CanvasViewModel.OnEditNodesModeChanged 再次触发 SelectTool("Node")，
            // 引发 SelectChanged 事件风暴，最终覆盖 EditPathNodesToolViewModel 的按钮状态。
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }
        internal GraphicResult DeleteNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;
            toolSelect?.SetNodeEditSubMode(turnOn ? NodeEditSubMode.Delete : NodeEditSubMode.None);
            // 同 AddNodes，不再重复设置 Context 和发布事件，PathNodeEditSession 已内部完成。
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult SeparateNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;

            if (turnOn)
            {
                // 弹出对话框获取分离距离
                var dialogResult = Context.RequestSeparateNodeDialog();
                if (dialogResult == null || !dialogResult.Confirmed)
                {
                    // 对话框取消时，PathNodeEditSession 未被调用，需手动发布一次事件同步 UI
                    PublishNodeEditModeChanged();
                    Context.RequestRedraw();
                    return GraphicResult.Ok();
                }

                Context.SeparateNodeDistance = dialogResult.Distance;
                toolSelect?.SetNodeEditSubMode(NodeEditSubMode.Separate);
                // 同 AddNodes，PathNodeEditSession 已内部完成状态设置和事件发布。
            }
            else
            {
                Context.SeparateNodeDistance = 2.0f;
                toolSelect?.SetNodeEditSubMode(NodeEditSubMode.None);
                // PathNodeEditSession 已内部发布事件，不再重复。
                Context.SelectedSeparateNodeWorldPosition = null;
            }

            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 移动选中的节点到指定坐标。
        /// 点击 Move 按钮时调用：检查是否有节点被选中，弹出坐标输入对话框，确认后移动。
        /// </summary>
        internal GraphicResult MoveNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;
            if (toolSelect == null || !toolSelect.HasSelectedMoveNode())
            {
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "未选中任何节点");
            }

            var nodeInfo = toolSelect.GetSelectedMoveNodeInfo();
            if (nodeInfo == null)
            {
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "无法获取选中节点信息");
            }

            var (combo, child, pointIndex, currentWorldPos) = nodeInfo.Value;

            // 调用对话框回调，让用户输入新坐标
            var dialogResult = Context.RequestMoveNodeDialog(currentWorldPos.X, currentWorldPos.Y);
            if (dialogResult == null || !dialogResult.Confirmed)
            {
                Context.RequestRedraw();
                return GraphicResult.Ok();
            }

            SKPoint newWorldPos = new SKPoint(dialogResult.NewX, dialogResult.NewY);

            // 通过 CommandHistory 执行移动，支持撤销/重做
            var cmd = new CommandMoveNode(combo, child, pointIndex, currentWorldPos, newWorldPos);
            CommandHistory.Execute(cmd);

            // 移动完成后保持选中状态（红色高亮），更新到新位置
            toolSelect.SetMoveNodeSelection(combo, child, pointIndex);
            Context.PublishSelectChanged();
            Context.ReportStatus($"节点已移动到 ({dialogResult.NewX:F2}, {dialogResult.NewY:F2})");
            return GraphicResult.Ok();
        }

        internal GraphicResult ExtendNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;
            if (toolSelect == null || !toolSelect.HasSelectedPathNodes())
            {
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "未选中任何节点");
            }

            bool extended = toolSelect.ExtendSelectedPathNodes();
            if (!extended)
            {
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch, "当前选中节点无法延伸");
            }

            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult ConnectNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;
            if (toolSelect == null || toolSelect.GetSelectedPathNodeCount() != 2)
            {
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "需要选中 2 个节点");
            }

            bool connected = toolSelect.ConnectSelectedPathNodes();
            if (!connected)
            {
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch, "当前选中节点无法连接");
            }

            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult SelectNodes(bool turnOn)
        {
            var check = CheckSelectionNotEmpty();
            if (!check.IsSuccess) return check;

            var toolSelect = Context.ActiveTool as ToolSelect;
            var targetMode = turnOn ? NodeEditSubMode.Select : NodeEditSubMode.None;
            toolSelect?.SetNodeEditSubMode(targetMode);
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        internal GraphicResult SetNodes(bool turnOn)
        {
            return GraphicResult.Ok();
        }
        #endregion

        #region 图形转换
        internal GraphicResult ConvertCurveToLine()
        {
            return GraphicResult.Ok();
        }
        internal GraphicResult ConvertLineToCurve()
        {
            return GraphicResult.Ok();
        }
        internal GraphicResult ConvertArcToCurve()
        {
            return GraphicResult.Ok();
        }
        internal GraphicResult SetSharpCorner()
        {
            return GraphicResult.Ok();
        }
        internal GraphicResult SetSmooth()
        {
            return GraphicResult.Ok();
        }
        internal GraphicResult SetSymmetry()
        {
            return GraphicResult.Ok();
        }

        #endregion

        #region 画布几何变换
        public GraphicResult SetCenter(double targetX, double targetY)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            // 变换前先标记旧位置脏区，避免移动后旧位置像素残留
            Context.MarkSelectedDirty();

            float dx = 0;
            float dy = 0;
            if (selectedShapes.Count == 1)
            {
                var obbInfo = selectedShapes.FirstOrDefault<DrawObject>().GetOBB();
                dx = (float)targetX - obbInfo.Center.X;
                dy = (float)targetY - obbInfo.Center.Y;
            }
            else
            {
                var bounds = selectedShapes.GetUnionAABB();
                dx = (float)targetX - bounds.MidX;
                dy = (float)targetY - bounds.MidY;
            }

            canvas.ExecuteTransformCommand(
                selectedShapes,
                "设置中心",
                () => selectedShapes.ApplyTranslate(dx, dy));

            return GraphicResult.Ok();
        }

        public GraphicResult SetDimension(double width, double height)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            // 防止缩到过小
            width = Math.Max(DrawObject.MinDimension, width);
            height = Math.Max(DrawObject.MinDimension, height);

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var mergedBounds = Context.CalculateMergedBounds();
            var constraints = SelectionResizeConstraintResolver.ResolveForSelection(canvas.Selection);
            var requiresUniformScale = constraints.HasFlag(SelectionResizeConstraint.RequireUniformScale);
            if (requiresUniformScale && !mergedBounds.IsEmpty)
            {
                // 工具栏输入属于独立于控制点拖拽的第二条缩放入口，
                // 这里必须做同样的约束收口，避免 UI 关闭锁比后绕过交互规则。
                var resolvedDimensions = UniformScaleHelper.ResolveUniformDimensions(
                    mergedBounds.Width,
                    mergedBounds.Height,
                    (float)width,
                    (float)height,
                    DrawObject.MinDimension);
                width = resolvedDimensions.Width;
                height = resolvedDimensions.Height;
            }

            var selectedShapes = canvas.Selection.CollectDimensionTargets();
            // 收集被 CollectDimensionTargets 过滤的点，多选缩放时也需要移动它们的位置
            var selectedPoints = canvas.Selection
                .Where(s => !s.IsLocked && s is DrawObject d && s.Type == ShapeType.Point)
                .Cast<DrawObject>()
                .ToList();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            var applySucceeded = true;
            canvas.ExecuteTransformCommand(
                selectedShapes,
                "设置尺寸",
                () =>
                {
                    var obbLine1 = SKPoint.Distance(selectedShapes.GetUnionOBB().Corners[0], selectedShapes.GetUnionOBB().Corners[1]);
                    var obbLine2 = SKPoint.Distance(selectedShapes.GetUnionOBB().Corners[0], selectedShapes.GetUnionOBB().Corners[3]);
                    var scaleCenter = new SKPoint(mergedBounds.Left + mergedBounds.Width / 2, mergedBounds.Top + mergedBounds.Height / 2);
                    selectedShapes.ApplyScale((float)width / obbLine1, (float)height / obbLine2, scaleCenter);
                });

            return GraphicResult.Ok();
        }

        public GraphicResult SetTranslate(double cx, double cy)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            canvas.ExecuteTransformCommand(
                selectedShapes,
                "设置偏移",
                () => selectedShapes.ApplyTranslate(
                    (float)cx, (float)cy));

            return GraphicResult.Ok();
        }

        /// <summary>
        /// 以指定锚点为中心，对选中图形应用绝对缩放。
        /// 支持旋转/剪切图形：通过线性矩阵补偿 SharpCenter，使锚点在世界坐标中固定不动。
        /// </summary>
        /// <param name="cx">锚点世界 X 坐标。</param>
        /// <param name="cy">锚点世界 Y 坐标。</param>
        /// <param name="scaleX">X 方向绝对缩放倍数。</param>
        /// <param name="scaleY">Y 方向绝对缩放倍数。</param>
        public GraphicResult SetScale(double cx, double cy, double scaleX, double scaleY)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            canvas.ExecuteTransformCommand(
                selectedShapes,
                "设置缩放",
                () => selectedShapes.ApplyScale((float)scaleX, (float)scaleY, new SKPoint((float)cx, (float)cy)));

            return GraphicResult.Ok();
        }

        public GraphicResult SetAbsoluteRotation(double cx, double cy, double angle)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            Context.IsAnchorPositionShow = false;

            canvas.ExecuteTransformCommand(
                selectedShapes,
                "设置旋转",
                () => selectedShapes.ApplyAbsoluteRotation(
                    new SKPoint((float)cx, (float)cy),
                    (float)angle));

            return GraphicResult.Ok();
        }

        public GraphicResult SetRotation(double cx, double cy, double angle)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            Context.IsAnchorPositionShow = false;

            canvas.ExecuteTransformCommand(
                selectedShapes,
                "设置旋转",
                () => selectedShapes.ApplyRotation((float)angle, new SKPoint((float)cx, (float)cy)));

            return GraphicResult.Ok();
        }

        public GraphicResult SetSkew(double angleX, double angleY)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            var skewTargets = selectedShapes
                .Where(item => item.Type != ShapeType.Circle
                    && item.Type != ShapeType.Arc
                    && item.Type != ShapeType.Text
                    && item.Type != ShapeType.Point
                    && !(item is DrawRectangle rect && rect.IsCornerRadiusRectangle()))
                .ToList();
            if (skewTargets.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            canvas.ExecuteTransformCommand(
                skewTargets,
                "设置倾斜",
                () => skewTargets.ApplySkew(
                    (float)angleX,
                    (float)angleY,
                    () =>
                    {
                        var bounds = Context.CalculateMergedBounds();
                        return new SKPoint(
                            (float)(bounds.Left + bounds.Width / 2),
                            (float)(bounds.Top + bounds.Height / 2));
                    }));

            return GraphicResult.Ok();
        }

        public GraphicResult SetSkew(double cx, double cy, double angleX, double angleY)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            var skewTargets = selectedShapes
                .Where(item => item.Type != ShapeType.Circle
                    && item.Type != ShapeType.Arc
                    && item.Type != ShapeType.Text
                    && item.Type != ShapeType.Point
                    && !(item is DrawRectangle rect && (rect.CornerRadiusTopLeft > 0 || rect.CornerRadiusTopRight > 0 || rect.CornerRadiusBottomLeft > 0 || rect.CornerRadiusBottomRight > 0)))
                .ToList();
            if (skewTargets.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            canvas.ExecuteTransformCommand(
                skewTargets,
                "设置倾斜",
                () =>
                {
                    var anchor = new SKPoint((float)cx, (float)cy);
                    Context.IsAnchorPositionShow = true;
                    Context.AnchorPosition = anchor;
                    foreach (var shape in skewTargets)
                    {
                        float newTanX = MathF.Tan((float)angleX * MathF.PI / 180f);
                        float newTanY = MathF.Tan((float)angleY * MathF.PI / 180f);
                        shape.Skew(newTanX, newTanY, anchor, true);
                    }
                });

            return GraphicResult.Ok();
        }

        public GraphicResult SetAbsoluteSkew(double cx, double cy, double angleX, double angleY)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectCenterTargets();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            var skewTargets = selectedShapes;
            if (skewTargets.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch);

            Context.MarkSelectedDirty();

            canvas.ExecuteTransformCommand(
                skewTargets,
                "设置倾斜",
                () =>
                {
                    var anchor = new SKPoint((float)cx, (float)cy);
                    Context.IsAnchorPositionShow = true;
                    Context.AnchorPosition = anchor;
                    foreach (var shape in skewTargets)
                    {
                        float totalTanX = MathF.Tan((float)angleX * MathF.PI / 180f);
                        float totalTanY = MathF.Tan((float)angleY * MathF.PI / 180f);

                        float currentTanX = MathF.Tan((float)shape.SkewX * MathF.PI / 180f);
                        float currentTanY = MathF.Tan((float)shape.SkewY * MathF.PI / 180f);

                        float deltaAngleX = totalTanX - currentTanX;
                        float deltaAngleY = totalTanY - currentTanY;

                        shape.Skew(deltaAngleX, deltaAngleY, anchor, true);
                    }
                });

            return GraphicResult.Ok();
        }

        public GraphicResult HorizontalMirror()
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectUnlockedDrawObjects();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var bounds = Context.CalculateMergedBounds();
            canvas.ExecuteMirrorCommand(
                selectedShapes,
                () => selectedShapes.ApplyMirror(isHorizontal: true,new SKPoint(bounds.MidX, bounds.MidY),true),
                resetPartialRenderWhenJumpLineVisible: true);
            return GraphicResult.Ok();
        }

        public GraphicResult VerticalMirror()
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.CollectUnlockedDrawObjects();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var bounds = Context.CalculateMergedBounds();
            canvas.ExecuteMirrorCommand(
                selectedShapes,
                () => selectedShapes.ApplyMirror(isHorizontal: false,new SKPoint(bounds.MidX, bounds.MidY), true),
                resetPartialRenderWhenJumpLineVisible: true);
            return GraphicResult.Ok();
        }
        #endregion

        #region 填满

        /// <summary>
        /// 填充
        /// </summary>
        internal GraphicResult<int> Fill(HatchParamDto hatchParam, List<IHatchable>? hatchables = null)
        {
            var prepared = DrawingHatch.PrepareFillCreation(
                hatchParam,
                Context.ActiveCanvas?.Selection,
                hatchables);
            if (!prepared.IsSuccess)
                return GraphicResult<int>.Fail(prepared.ErrorCode, prepared.Message);

            var hatch = prepared.Value!;

            if (ActiveLayer != null)
            {
                // 记录当前填充目标（外框图形），以便填充完成后保持其选中
                var fillTargets = Selection;

                this.ExecuteLayerAdd(
                    ActiveLayer,
                    new List<IShape> { hatch },
                    requestRedraw: true);

                // 填充完成后不自动选中填充物，恢复外框图形的选中状态。
                // CommandAdd 会取消所有原有选中并选中 hatch，这里在命令执行后修正选中状态。
                hatch.IsSelected = false;
                foreach (var target in fillTargets)
                {
                    if (target is DrawObject obj)
                        obj.IsSelected = true;
                }
                SetSelectedShapes();
            }
            else
            {
                Context.RequestRedraw();
            }

            return GraphicResult<int>.Ok(hatch.UId);
        }

        /// <summary>
        /// 获取选中填充对象的填充参数。
        /// 仅当选中 DrawingHatch 且其 HatchParamInfo 不为 null 时返回。
        /// </summary>
        internal GraphicResult<HatchParamDto?> GetHatchParam()
        {
            var selectedHatches = DrawingHatch.CollectSelectedHatches(Selection);
            if (selectedHatches.Count == 0)
                return GraphicResult<HatchParamDto?>.Fail(GraphicErrorCode.NothingSelected, "未选中填充对象");

            // 返回第一个选中填充对象的参数
            var param = selectedHatches[0].HatchParamInfo;
            return GraphicResult<HatchParamDto?>.Ok(param);
        }

        /// <summary>
        /// 重新填充：保留原有的 DrawingHatch 对象，仅更新其内部填充线。
        /// 若选中了多个填充对象，则逐个修改。
        /// </summary>
        internal GraphicResult<List<int>> Refill(HatchParamDto hatchParam)
        {
            var selectedHatches = DrawingHatch.CollectSelectedHatches(Selection);
            if (selectedHatches.Count == 0) return null;

            foreach (var target in selectedHatches)
            {
                if (target.Boundaries == null || target.Boundaries.Count == 0)
                {
                    return GraphicResult<List<int>>.Fail(GraphicErrorCode.ShapeTypeMismatch, "选中的填充对象没有边界，无法重新填充。");
                }
            }

            var cmd = new CommandRefill(selectedHatches, hatchParam);
            CommandHistory.Execute(cmd);

            var lst = selectedHatches.Select(h => h.UId).ToList();
            Context.RequestRedraw();

            return GraphicResult<List<int>>.Ok(lst);
        }

        /// <summary>
        /// 判断一批图形的变换是否可能影响填充结果。
        /// </summary>
        public bool RequiresHatchRegeneration(IEnumerable<IShape>? shapes = null)
        {
            var candidates = shapes ?? Selection;
            return candidates != null && DrawingHatch.RequiresRegeneration(candidates);
        }

        /// <summary>
        /// 当 <paramref name="shapes"/> 中的图形发生变换（平移/旋转/缩放/剪切）后，
        /// 基于各图形当前已有的 HatchParamInfo 重新生成其所属 DrawingHatch 的填充线。
        /// 传入 null 时回退到使用当前选中的图形集合。
        /// </summary>
        public void RegenerateHatchForShapes(IEnumerable<IShape>? shapes = null)
        {
            var targetShapes = shapes as IList<IShape>
                ?? (shapes?.ToList())
                ?? (Selection as IList<IShape>)
                ?? Selection.ToList();
            if (targetShapes.Count == 0) return;
            if (!RequiresHatchRegeneration(targetShapes)) return;

            var targetIds = DrawingHatch.CollectRegenerationTargetIds(targetShapes);
            var affectedHatches = DrawingHatch.CollectAffectedHatches(Layers, targetIds);
            bool anyUpdated = DrawingHatch.RebuildAffectedHatches(
                affectedHatches,
                rect => Context?.MarkDirty(rect));

            if (anyUpdated)
            {
                Context.RequestRedraw();
            }
        }

        /// <summary>
        /// 打散填充物件
        /// </summary>
        internal GraphicResult BreakFill()
        {
            var prepared = DrawingHatch.PrepareBreakFillResult(Selection);
            if (!prepared.IsSuccess)
                return GraphicResult.Fail(prepared.ErrorCode, prepared.Message);

            var hatchs = prepared.Value!.Hatches;
            var group = prepared.Value.Group;

            var targetLayer = ActiveLayer;
            if (targetLayer != null)
            {
                this.ExecuteSelectionReplacement(
                    hatchs,
                    new ActiveLayerResultPreparation(targetLayer, [group]),
                    "打散填满物件",
                    requestRedraw: true);
            }
            else
            {
                Context.RequestRedraw();
            }

            return GraphicResult.Ok();
        }
        #endregion

        #region 群组
        /// <summary>
        /// 群组
        /// </summary>
        internal GraphicResult Group()
        {
            if (Selection.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var selectedHatches = Selection.OfType<DrawingHatch>().Where(h => h.Boundaries.Count > 0).ToList();
            if (selectedHatches.Count > 0)
            {
                var msgResult = System.Windows.MessageBox.Show("一个或多个填充形状与所选形状关联。为了将它们分组，必须先解除与边界的关系。\n\n移除关联并继续？", "警告",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (msgResult != System.Windows.MessageBoxResult.Yes)
                    return GraphicResult.Ok();
            }

            // 检查选中的图形是否都在同一个父群组内
            var commonParent = FindCommonParentGroup(Selection);

            if (commonParent != null)
            {
                // 子群组场景：选中的图形都在同一个父群组内，
                // 新群组作为父群组的成员插入
                var layer = LayerViewModels
                    .FirstOrDefault(l => l.Contains(Selection[0])) as LayerViewModel;
                if (layer == null)
                    return GraphicResult.Fail(GraphicErrorCode.CanvasNotFound, "找不到目标图层");

                var parentChildren = commonParent.Children;
                int minIndex = int.MaxValue;
                foreach (var shape in Selection)
                {
                    int idx = parentChildren.IndexOf(shape);
                    if (idx >= 0 && idx < minIndex)
                        minIndex = idx;
                }
                if (minIndex == int.MaxValue)
                    minIndex = 0;

                var group = new DrawingGroup(Selection.ToList());

                // 为新群组分配名称（常规路径由 AddNodes 处理，子群组路径需手动设置）
                if (string.IsNullOrEmpty(group.Name))
                    group.Name = SerialNumber.NextId().ToString();

                var cmds = new List<IDrawingCommand>();
                if (selectedHatches.Count > 0)
                    cmds.Add(new CommandUnassociateHatch(selectedHatches));

                cmds.Add(new CommandGroupInContainer(layer, commonParent, Selection.ToList(), group, minIndex, "群组"));
                CommandManager.Execute(new CompositeCommand("群组", cmds));

                SetSelectedShapes([group]);
                return GraphicResult.Ok();
            }

            // 常规场景：选中的图形不在同一个父群组内，创建顶层群组
            var preparation = LayerViewModels.CreateSelectionContainerPreparation(
                Selection,
                shapes => new DrawingGroup(shapes.ToList()));
            if (!preparation.IsSuccess)
                return GraphicResult.Fail(preparation.ErrorCode, preparation.Message);

            var targetLayer = preparation.Value!.TargetLayer;
            var topGroup = preparation.Value.ContainerShape;

            // 解除填充关联 + 群组替换，一次撤销可完整还原
            var topCmds = new List<IDrawingCommand>();
            if (selectedHatches.Count > 0)
            {
                topCmds.Add(new CommandUnassociateHatch(selectedHatches));
            }
            topCmds.AddRange(LayerViewModels
                .CreateContainerReplacementCommand(Selection, targetLayer, topGroup, "群组")
                is CompositeCommand cc
                    ? cc.Commands
                    : [LayerViewModels.CreateContainerReplacementCommand(Selection, targetLayer, topGroup, "群组")]);

            CommandManager.Execute(new CompositeCommand("群组", topCmds));

            SetSelectedShapes([topGroup]);
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 查找选中图形的公共父群组。当所有选中图形都直接位于同一个 DrawingGroup 内时返回该群组；
        /// 否则返回 null。使用实例的 Layers 而非 DocumentContext.Instance，确保多画布场景正确。
        /// </summary>
        private DrawingGroup? FindCommonParentGroup(IReadOnlyList<IShape> shapes)
        {
            if (shapes == null || shapes.Count < 1) return null;

            DrawingGroup? common = null;
            foreach (var shape in shapes)
            {
                var parent = FindDirectParentGroup(shape, shapes[0]);
                if (parent == null) return null;

                if (common == null)
                    common = parent;
                else if (!ReferenceEquals(common, parent))
                    return null;
            }
            return common;
        }

        private DrawingGroup? FindDirectParentGroup(IShape target, IShape anyShapeForLayerHint)
        {
            var layer = FindLayerContaining(anyShapeForLayerHint);
            if (layer == null) return null;

            foreach (var topShape in layer.AllShapesInternal)
            {
                if (topShape is DrawingGroup dg)
                {
                    if (dg.Children.Contains(target))
                        return dg;

                    var nested = FindParentGroupRecursive(dg, target);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private static DrawingGroup? FindParentGroupRecursive(IContainer current, IShape target)
        {
            foreach (var child in current.Children)
            {
                if (ReferenceEquals(child, target) || child.UId == target.UId)
                    return current as DrawingGroup;

                if (child is IContainer subContainer)
                {
                    var found = FindParentGroupRecursive(subContainer, target);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private DrawingLayer? FindLayerContaining(IShape shape)
        {
            foreach (var layer in Layers)
            {
                foreach (var s in layer.AllShapesInternal)
                {
                    if (ContainsRecursive(s, shape))
                        return layer;
                }
            }
            return null;
        }

        private static bool ContainsRecursive(IShape current, IShape target)
        {
            if (ReferenceEquals(current, target) || current.UId == target.UId)
                return true;
            if (current is IContainer container)
            {
                foreach (var child in container.Children)
                {
                    if (ContainsRecursive(child, target))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 解散群组
        /// </summary>
        internal GraphicResult Ungroup()
        {
            var groups = Selection.CollectSelectedGroups();
            if (groups.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var preparations = LayerViewModels.CreateContainerReleasePreparations(
                groups,
                group => group.CreateUngroupedChildren());
            if (preparations.Count > 0)
                this.ExecuteContainerRelease(preparations, "解散群组");

            return GraphicResult.Ok();
        }
        #endregion

        #region 组合
        /// <summary>
        /// 组合图形
        /// </summary>
        internal GraphicResult Combine()
        {
            if (Selection.Count < 2)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var selectedHatches = Selection.OfType<DrawingHatch>().Where(h => h.Boundaries.Count > 0).ToList();
            if (selectedHatches.Count > 0)
            {
                var msgResult = System.Windows.MessageBox.Show(
                    "一个或多个填充形状与所选形状关联。为了将它们组合，必须先解除与边界的关系。\n\n移除关联并继续？",
                    "警告",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (msgResult != System.Windows.MessageBoxResult.Yes)
                    return GraphicResult.Ok();
            }

            // 检查选中的图形是否都在同一个父群组内
            var commonParent = FindCommonParentGroup(Selection);

            if (commonParent != null)
            {
                // 子组合场景：选中的图形都在同一个父群组内，
                // 新组合作为父群组的成员插入
                var layer = LayerViewModels
                    .FirstOrDefault(l => l.Contains(Selection[0])) as LayerViewModel;
                if (layer == null)
                    return GraphicResult.Fail(GraphicErrorCode.CanvasNotFound, "找不到目标图层");

                var parentChildren = commonParent.Children;
                int minIndex = int.MaxValue;
                foreach (var shape in Selection)
                {
                    int idx = parentChildren.IndexOf(shape);
                    if (idx >= 0 && idx < minIndex)
                        minIndex = idx;
                }
                if (minIndex == int.MaxValue)
                    minIndex = 0;

                var combination = new DrawCombination(Selection.ToList());

                // 为新组合分配名称
                if (string.IsNullOrEmpty(combination.Name))
                    combination.Name = SerialNumber.NextId().ToString();

                var cmds = new List<IDrawingCommand>();
                if (selectedHatches.Count > 0)
                    cmds.Add(new CommandUnassociateHatch(selectedHatches));

                cmds.Add(new CommandGroupInContainer(layer, commonParent, Selection.ToList(), combination, minIndex, "组合"));
                CommandManager.Execute(new CompositeCommand("组合", cmds));

                SetSelectedShapes([combination]);
                return GraphicResult.Ok();
            }

            // 常规场景：选中的图形不在同一个父群组内，创建顶层组合
            var preparation = LayerViewModels.CreateSelectionContainerPreparation(
                Selection,
                shapes => new DrawCombination(shapes.ToList()));
            if (!preparation.IsSuccess)
                return GraphicResult.Fail(preparation.ErrorCode, preparation.Message);

            var targetLayer = preparation.Value!.TargetLayer;
            var topCombination = preparation.Value.ContainerShape;

            // 解除填充关联 + 组合替换，一次撤销可完整还原
            var cmds2 = new List<IDrawingCommand>();
            if (selectedHatches.Count > 0)
            {
                cmds2.Add(new CommandUnassociateHatch(selectedHatches));
            }
            cmds2.AddRange(LayerViewModels
                .CreateContainerReplacementCommand(Selection, targetLayer, topCombination, "组合")
                is CompositeCommand cc
                    ? cc.Commands
                    : [LayerViewModels.CreateContainerReplacementCommand(Selection, targetLayer, topCombination, "组合")]);

            CommandManager.Execute(new CompositeCommand("组合", cmds2));

            SetSelectedShapes([topCombination]);
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 打散组合
        /// </summary>
        internal GraphicResult Separate()
        {
            var combinations = Selection.CollectSelectedCombinations();
            if (combinations.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var preparations = LayerViewModels.CreateContainerReleasePreparations(
                combinations,
                combination => combination.CreateSeparatedChildren());

            if (preparations.Count > 0)
                this.ExecuteContainerRelease(preparations, "打散组合");

            return GraphicResult.Ok();
        }



        internal GraphicResult Reverse()
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var draws = canvas.Selection.CollectUnlockedDrawObjects();
            if (draws.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                draws,
                "反转方向",
                () =>
                {
                    foreach (var d in draws)
                        d.ReverseDirection();
                },
                requestRedraw: true);
            return GraphicResult.Ok();
        }

        internal GraphicResult Lock()
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var shapes = canvas.Selection;
            if (shapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            bool lockAll = !shapes.All(s => s.IsLocked);   // 有任意未锁 → 全部锁定
            var drawObjects = shapes.OfType<DrawObject>().ToList();
            if (drawObjects.Count == 0)
                return GraphicResult.Ok();

            var cmd = new CommandLock(drawObjects, lockAll);
            canvas.CommandHistory.Execute(cmd);

            return GraphicResult.Ok();
        }

        internal GraphicResult MoveToNewLayer()
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = Context.ActiveCanvas as DrawingCanvas;
            if (canvas == null)
                return GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);

            var selectedShapes = canvas.Selection
                .CollectUnlockedDrawObjects()
                .Cast<IShape>()
                .ToList();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);
            canvas.LayerViewViewModel.AddLayerCommand.Execute(null);
            var newLayer = canvas.LayerViewViewModel.LayerViewModels.LastOrDefault();
            if (newLayer == null)
                return GraphicResult.Fail(GraphicErrorCode.CanvasNotFound, "创建新图层失败");

            canvas.ExecuteSelectionReplacement(
                selectedShapes,
                new ActiveLayerResultPreparation(newLayer, selectedShapes),
                "移动到新图层",
                requestRedraw: true);
            canvas.InvalidateVisibleCache();
            return GraphicResult.Ok();
        }

        internal GraphicResult SameRadius()
        {
            return GraphicResult.Ok();
        }

        internal GraphicResult SetCircleRadius()
        {
            return GraphicResult.Ok();
        }

        internal GraphicResult ConvertToCurve()
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            // 转曲线前退出节点编辑模式，复位编辑节点按钮为未选中
            if (Context.IsNodeEditing)
                EditNodes(false);

            var canvas = Context.ActiveCanvas!;
            var curveSources = canvas.Selection.CollectCurveConversionSources();
            if (curveSources.Count == 0)
                return GraphicResult.Ok();

            var conversion = curveSources.CreateCurveConversionResult();
            var combinations = conversion.Combinations;
            var convertedSources = conversion.ConvertedSources;

            //////////////////////////////////////////////
            var affectIds = convertedSources.Select(o => o.UId).ToList();
            for (int i = 0; i < affectIds.Count(); i++)
            {
                var affectId = affectIds[i];
                var hatchShapes = Context.ActiveCanvas!.AllShapes.Where(o => o is DrawingHatch hatch
        && hatch.Boundaries.Any(s => s.UId == affectId));
                foreach (var item in hatchShapes)
                {
                    var hatchShape = item as DrawingHatch;
                    if (hatchShape == null) continue;
                    int index = hatchShape.Boundaries.FindIndex(o => o.UId == affectId);
                    if (index == -1) continue;
                    hatchShape.Boundaries.RemoveAt(index);
                    hatchShape.Boundaries.Add(combinations[i]);
                }
            }
            ////////////////////////////////////////////

            var resultPreparation = ((DrawingCanvas)canvas).ActiveLayer
                .CreateActiveLayerResultPreparation(
                    combinations.Cast<IShape>().ToList(),
                    "无法将选中图形转换为曲线");
            if (!resultPreparation.IsSuccess)
                return GraphicResult.Fail(resultPreparation.ErrorCode, resultPreparation.Message);

            ((DrawingCanvas)canvas).ExecuteSelectionReplacement(
                convertedSources,
                resultPreparation.Value!,
                "转换为曲线",
                requestRedraw: true);
            return GraphicResult.Ok();
        }

        internal GraphicResult ConvertToImage()
        {
            return GraphicResult.Ok();
        }




        internal GraphicResult ExtendHeadAndTail()
        {
            throw new NotImplementedException();
        }

        internal GraphicResult MatrixCopy()
        {
            throw new NotImplementedException();
        }



        public GraphicResult SetSkyWriting(SkyWritingSettingsDto s) => throw new NotImplementedException();


        internal GraphicResult AdjustRect(RoundMode mode, double lt, double rt, double rb, double lb)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var rects = canvas.Selection.CollectUnlockedRectangles();
            if (rects.Count == 0) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                rects.Cast<DrawObject>(),
                "调整圆角",
                () => rects.ApplyCornerRadius(mode, lt, rt, rb, lb));
            return GraphicResult.Ok();
        }
        /// <summary>
        /// 调整倒角：对选中的矩形图形调整其四个角的倒角大小。
        /// RoundMode 可选：绝对值（Unit）或相对值（Percent）。
        /// 参数：lt=左上，rt=右上，rb=右下，lb=左下。
        /// </summary>
        internal GraphicResult AdjustChamfer(RoundMode mode, double lt, double rt, double rb, double lb)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var rects = canvas.Selection.CollectUnlockedRectangles();
            if (rects.Count == 0) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                rects.Cast<DrawObject>(),
                "调整倒角",
                () => rects.ApplyChamfer(mode, lt, rt, rb, lb));
            return GraphicResult.Ok();
        }
        internal GraphicResult AdjustCircle(double cx, double cy, double rx, double ry)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var circles = canvas.Selection.CollectUnlockedCircles();
            if (circles.Count == 0) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                circles.Cast<DrawObject>(),
                "调整圆/椭圆",
                () => circles.ApplyGeometry((float)cx, (float)cy, (float)rx, (float)ry));
            return GraphicResult.Ok();
        }

        internal GraphicResult AdjustArc(double cx, double cy, double rx, double ry,
            double sAngle, double eAngle)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var arcs = canvas.Selection.CollectUnlockedArcs();
            if (arcs.Count == 0) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                arcs.Cast<DrawObject>(),
                "调整圆弧",
                () =>
                {
                    foreach (var arc in arcs)
                        arc.AdjustArc(cx, cy, rx, ry, sAngle, eAngle);
                });

            return GraphicResult.Ok();
        }
        internal GraphicResult AdjustArcThreePoint(
       float p0x, float p0y,
       float p1x, float p1y,
       float p2x, float p2y)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var arcs = canvas.Selection.CollectUnlockedArcs();
            if (arcs.Count == 0) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                arcs.Cast<DrawObject>(),
                "调整圆弧",
                () => arcs.ApplyThreePointArc(
                    new SKPoint(p0x, p0y),
                    new SKPoint(p1x, p1y),
                    new SKPoint(p2x, p2y)));
            return GraphicResult.Ok();
        }
        internal GraphicResult SetJumpPoint(JumpSettingsDto jumpSettings)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            if (Context.ActiveCanvas!.Selection.All(s => s.IsLocked))
                return GraphicResult.Fail(GraphicErrorCode.ShapeLocked);

            if (jumpSettings == null)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "jumpSettings 不能为 null");

            var draws = Context.ActiveCanvas.Selection
                .CollectSelectedDrawObjects();

            if (draws.Count < 1)
                return GraphicResult.Fail(GraphicErrorCode.InsufficientSelection, "跳点至少需要 1 个图形");

            // 在修改前捕获跳点状态，用于撤销
            var command = new CommandJumpPoint(draws);

            float skipRadius = Math.Max(0f, (float)jumpSettings.JumpSize / 2f);

            draws.ApplyJumpPointState(skipRadius, static (left, right) => left.ComputePathIntersections(right));

            // 捕获操作后状态，用于重做。
            // 这里业务修改已经真实生效，只需要入栈，不能再走 Execute() 二次写回。
            command.CaptureAfterState();
            Context.ActiveCanvas.CommandManager.PushExecutedCommand(command);

            Context.PublishTransformChange();
            return GraphicResult.Ok();
        }

        // ── 交点计算（仅 Adjust 使用，保留在本文件）──────────────

        #endregion

        #region 向量变形
        /// <summary>
        /// 向量布尔运算：对选中的多个图形执行并集（Union）运算，
        /// 将结果替换为新的组合图形。
        /// 闭合图形执行布尔运算；开口路径按当前操作保留完整路径或闭合区域内部片段。
        /// </summary>
        internal GraphicResult VectorCombine(SKPathOp kPathOp)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection
                .CollectUnlockedShapes();
            if (selectedShapes.Count < 2)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "向量合并至少需要选中两个未锁定图形");



            // 筛选有效的 DrawObject
            var drawObjects = canvas.Selection.CollectUnlockedDrawObjects();

            if (drawObjects.Count < 2)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch, "选中图形中有效的可运算图形不足两个");
            var lastSelectedShape = (canvas as DrawingCanvas)?.LastSelectedShape as DrawObject;
            var preparation = drawObjects.PrepareBooleanPathEntries(lastSelectedShape);
            var orderedShapes = preparation.OrderedShapes;
            var pathInfoList = preparation.Entries;
            try
            {
                var booleanResult = orderedShapes.CreateBooleanOperationShapeResult(
                    pathInfoList,
                    styleSource => pathInfoList.CreateVectorCombineResult(styleSource, kPathOp));
                if (!booleanResult.IsSuccess)
                    return GraphicResult.Fail(booleanResult.ErrorCode, booleanResult.Message);

                IShape newShape = booleanResult.Value!;

                // 检查选中的图形是否都在同一个父群组内
                var commonParent = FindCommonParentGroup(selectedShapes);
                if (commonParent != null)
                {
                    var layer = LayerViewModels
                        .FirstOrDefault(l => l.Contains(selectedShapes[0])) as LayerViewModel;
                    if (layer != null)
                    {
                        var parentChildren = commonParent.Children;
                        int minIndex = int.MaxValue;
                        foreach (var shape in selectedShapes)
                        {
                            int idx = parentChildren.IndexOf(shape);
                            if (idx >= 0 && idx < minIndex)
                                minIndex = idx;
                        }
                        if (minIndex == int.MaxValue)
                            minIndex = 0;

                        if (string.IsNullOrEmpty(newShape.Name))
                            newShape.Name = SerialNumber.NextId().ToString();

                        CommandManager.Execute(new CommandGroupInContainer(
                            layer, commonParent, selectedShapes, newShape, minIndex, "向量合并"));

                        SetSelectedShapes([newShape]);
                        DocumentContext.Instance?.RequestRedraw();
                        return GraphicResult.Ok();
                    }
                }

                // 常规场景：选中的图形不在同一个父群组内，在图层顶层替换
                var resultPreparation = ((DrawingCanvas)canvas).ActiveLayer
                    .CreateActiveLayerResultPreparation(
                        [newShape],
                        "向量合并后无法生成有效图形");
                if (!resultPreparation.IsSuccess)
                    return GraphicResult.Fail(resultPreparation.ErrorCode, resultPreparation.Message);

                ((DrawingCanvas)canvas).ExecuteSelectionReplacement(
                    selectedShapes,
                    resultPreparation.Value!,
                    "向量合并",
                    requestRedraw: true,
                    publishSelectChanged: true);
                return GraphicResult.Ok();
            }
            finally
            {
                foreach (var entry in pathInfoList) entry.WorldPath.Dispose();
            }
        }
        /// <summary>
        /// 保留主物件
        /// </summary>
        /// <returns></returns>
        internal GraphicResult KeepMain()
        {

            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = Context.ActiveCanvas!;
            var selectedShapes = canvas.Selection.ToList();
            if (selectedShapes.Count < 2)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "向量合并至少需要选中两个图形");

            // 筛选有效的 DrawObject
            var drawObjects = selectedShapes.CollectSelectedDrawObjects();
            if (drawObjects.Count < 2)
                return GraphicResult.Fail(GraphicErrorCode.ShapeTypeMismatch, "选中图形中有效的可运算图形不足两个");

            var lastSelectedShape = (canvas as DrawingCanvas)?.LastSelectedShape as DrawObject;
            var preparation = drawObjects.PrepareBooleanPathEntries(lastSelectedShape, reverseAfterLastSelected: true);
            var orderedShapes = preparation.OrderedShapes;
            var pathInfoList = preparation.Entries;
            try
            {
                var booleanResult = orderedShapes.CreateBooleanOperationShapeResult(
                    pathInfoList,
                    styleSource => GraphicResult<List<IShape>>.Ok(pathInfoList.CreateKeepMainResult(styleSource)));
                if (!booleanResult.IsSuccess)
                    return GraphicResult.Fail(booleanResult.ErrorCode, booleanResult.Message);

                IShape newShape = booleanResult.Value!;

                // 检查选中的图形是否都在同一个父群组内
                var commonParent = FindCommonParentGroup(selectedShapes);
                if (commonParent != null)
                {
                    var layer = LayerViewModels
                        .FirstOrDefault(l => l.Contains(selectedShapes[0])) as LayerViewModel;
                    if (layer != null)
                    {
                        var parentChildren = commonParent.Children;
                        int minIndex = int.MaxValue;
                        foreach (var shape in selectedShapes)
                        {
                            int idx = parentChildren.IndexOf(shape);
                            if (idx >= 0 && idx < minIndex)
                                minIndex = idx;
                        }
                        if (minIndex == int.MaxValue)
                            minIndex = 0;

                        if (string.IsNullOrEmpty(newShape.Name))
                            newShape.Name = SerialNumber.NextId().ToString();

                        CommandManager.Execute(new CommandGroupInContainer(
                            layer, commonParent, selectedShapes, newShape, minIndex, "保留主物件"));

                        SetSelectedShapes([newShape]);
                        DocumentContext.Instance?.RequestRedraw();
                        return GraphicResult.Ok();
                    }
                }

                // 常规场景：选中的图形不在同一个父群组内，在图层顶层替换
                var resultPreparation = ((DrawingCanvas)canvas).ActiveLayer
                    .CreateActiveLayerResultPreparation(
                        [newShape],
                        "向量合并后无法生成有效图形");
                if (!resultPreparation.IsSuccess)
                    return GraphicResult.Fail(resultPreparation.ErrorCode, resultPreparation.Message);

                ((DrawingCanvas)canvas).ExecuteSelectionReplacement(
                    selectedShapes,
                    resultPreparation.Value!,
                    "向量合并",
                    requestRedraw: true,
                    publishSelectChanged: true);
                return GraphicResult.Ok();
            }
            finally
            {
                foreach (var entry in pathInfoList) entry.WorldPath.Dispose();
            }
        }

        #endregion



        #region 依分区打断
        /// <summary>
        /// 依分区打断物件：使用 SKPath 布尔运算（Intersect）精确裁剪图形到各分区内，
        /// 保留原始图元几何特征（直线仍为直线，不会被采样为大量小线段），
        /// 曲线段按弧长适度细分，大幅减少加工指令数量。
        /// 支持撤销（通过 CompositeCommand 组合 CommandRemove + CommandAdd）。
        /// </summary>
        /// <param name="partWidth">分割区块长度（mm）</param>
        /// <param name="partHeight">分割区块宽度（mm）</param>
        /// <param name="overlapX">X方向重叠长度（mm）</param>
        /// <param name="overlapY">Y方向重叠长度（mm）</param>
        internal GraphicResult Partition(double partWidth, double partHeight, double overlapX, double overlapY)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = Context.ActiveCanvas as DrawingCanvas;
            if (canvas == null)
                return GraphicResult.Fail(GraphicErrorCode.CanvasNotFound);

            var selectedShapes = canvas.Selection.ToList();
            if (selectedShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            if (partWidth == 0 && partHeight == 0)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "分割区块长度和宽度必须大于等于0");

            var newShapes = new List<IShape>();

            // 以整体选择区域的包围盒作为统一网格基准（而非每个图形单独计算网格）
            var selectionBounds = Context.CachedSelectionBounds ?? Context.CalculateMergedBounds();
            if (selectionBounds.IsEmpty)
                return GraphicResult.Fail(GraphicErrorCode.EmptyResult, "选择区域为空");

            float startX = selectionBounds.Left;
            float startY = selectionBounds.Top;
            float endX = selectionBounds.Right;
            float endY = selectionBounds.Bottom;

            // 预处理：将所有选中图形（含 Hatch 子图形展开）平展为 (DrawObject, worldPath, bbox) 列表
            var allShapePaths = new List<(DrawObject draw, SKPath? worldPath, SKRect bbox)>();
            foreach (var shape in selectedShapes)
            {
                if (shape is not DrawObject draw) continue;

                // DrawingHatch 展开子图形
                if (draw is DrawingHatch hatch)
                {
                    foreach (var child in hatch.ExpandHatchObject())
                    {
                        if (child is not DrawObject childDraw) continue;
                        if (childDraw is DrawDot dot)
                        {
                            var dotBbox = new SKRect(dot.Points[0].X, dot.Points[0].Y, dot.Points[0].X, dot.Points[0].Y);
                            allShapePaths.Add((dot, null, dotBbox));
                            continue;
                        }
                        SKPath? localPath;
                        try { localPath = childDraw.GetPath(); }
                        catch (NotImplementedException) { continue; }
                        if (localPath == null || localPath.IsEmpty) { localPath?.Dispose(); continue; }
                        var wp = new SKPath(localPath);
                        wp.Transform(childDraw.GetTransformMatrix());
                        localPath.Dispose();
                        var childBbox = wp.TightBounds;
                        if (childBbox.IsEmpty) { wp.Dispose(); continue; }
                        allShapePaths.Add((childDraw, wp, childBbox));
                    }
                    continue;
                }

                // DrawCombination 展开子图形：每个子图形独立判断是否需要裁剪
                if (draw is DrawCombination combination)
                {
                    foreach (var child in combination.Children)
                    {
                        if (child is not DrawObject childDraw) continue;
                        if (childDraw is DrawDot dot)
                        {
                            var dotBbox = new SKRect(dot.Points[0].X, dot.Points[0].Y, dot.Points[0].X, dot.Points[0].Y);
                            allShapePaths.Add((dot, null, dotBbox));
                            continue;
                        }
                        SKPath? localPath;
                        try { localPath = childDraw.GetPath(); }
                        catch (NotImplementedException) { continue; }
                        if (localPath == null || localPath.IsEmpty) { localPath?.Dispose(); continue; }
                        var wp = new SKPath(localPath);
                        wp.Transform(childDraw.GetTransformMatrix());
                        localPath.Dispose();
                        var childBbox = wp.TightBounds;
                        if (childBbox.IsEmpty) { wp.Dispose(); continue; }
                        allShapePaths.Add((childDraw, wp, childBbox));
                    }
                    continue;
                }

                if (draw is DrawDot singleDot)
                {
                    var dotBbox = new SKRect(singleDot.Points[0].X, singleDot.Points[0].Y, singleDot.Points[0].X, singleDot.Points[0].Y);
                    allShapePaths.Add((singleDot, null, dotBbox));
                    continue;
                }

                // 普通图形：获取世界路径
                using var localP = draw.GetPath();
                if (localP == null || localP.IsEmpty) continue;
                var worldPath = new SKPath(localP);
                worldPath.Transform(draw.GetTransformMatrix());
                var bbox = worldPath.TightBounds;
                if (bbox.IsEmpty) { worldPath.Dispose(); continue; }
                allShapePaths.Add((draw, worldPath, bbox));
            }

            // 修正网格范围：确保覆盖所有展开后图形的实际 bbox，避免边缘图形被裁剪丢失
            foreach (var (_, _, b) in allShapePaths)
            {
                if (b.Left < startX) startX = b.Left;
                if (b.Top < startY) startY = b.Top;
                if (b.Right > endX) endX = b.Right;
                if (b.Bottom > endY) endY = b.Bottom;
            }

            // partWidth/partHeight 为 0 表示该方向不分割，使用实际范围作为分区尺寸
            if (partHeight == 0) partHeight = endY - startY;
            if (partWidth == 0) partWidth = endX - startX;

            float pw = (float)partWidth;
            float ph = (float)partHeight;
            float ox = (float)overlapX;
            float oy = (float)overlapY;

            // 步进 = 分割尺寸 - 重叠长度（保证重叠区域）
            float stepX = pw - ox;
            float stepY = ph - oy;
            if (stepX <= 0 || stepY <= 0)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "重叠长度不能大于等于分割尺寸");

            try
            {
                // 按分区遍历：同一分区内所有图形的裁剪结果合并为一个组合
                int partIndex = 0;
                for (float cx = startX; cx < endX; cx += stepX)
                {
                    for (float cy = startY; cy < endY; cy += stepY)
                    {
                        float left = cx;
                        float top = cy;
                        float right = cx + pw;
                        float bottom = cy + ph;

                        // 跳过面积太小的分区
                        if (right - left < 0.01f || bottom - top < 0.01f) continue;

                        var cellRect = new SKRect(left, top, right, bottom);
                        var cellShapes = new List<IShape>();

                        foreach (var (draw, worldPath, bbox) in allShapePaths)
                        {
                            // DrawDot：判断点是否在分区内
                            if (draw is DrawDot dot)
                            {
                                if (cellRect.Contains(dot.Points[0].X, dot.Points[0].Y))
                                    cellShapes.Add((DrawObject)dot.Clone());
                                continue;
                            }

                            if (worldPath == null || worldPath.IsEmpty) continue;

                            // 快速排除：图形包围盒与分区不相交（使用非严格不等式，允许边界相切）
                            if (cellRect.Left > bbox.Right || cellRect.Right < bbox.Left ||
                                cellRect.Top > bbox.Bottom || cellRect.Bottom < bbox.Top)
                                continue;

                            // 图形完全包含在分区内（含精度容差），不需要分割，直接放入该分区
                            // 容差处理：selectionBounds 与 worldPath.TightBounds 计算方式不同可能存在微小偏差
                            const float eps = 0.01f;
                            if (bbox.Left >= cellRect.Left - eps && bbox.Top >= cellRect.Top - eps &&
                                bbox.Right <= cellRect.Right + eps && bbox.Bottom <= cellRect.Bottom + eps)
                            {
                                cellShapes.Add(draw);
                                continue;
                            }

                            // 逐段裁剪路径轨迹
                            var clippedContours = ClipPathToRect(worldPath, cellRect);
                            foreach (var chain in clippedContours)
                            {
                                if (chain.Count < 2) continue;
                                partIndex++;
                                var polyLine = new DrawPolyLines(chain)
                                {
                                    Pen = new SKPaint
                                    {
                                        Color = draw.Pen.Color,
                                        Style = draw.Pen.Style,
                                        StrokeWidth = draw.Pen.StrokeWidth,
                                        IsAntialias = draw.Pen.IsAntialias
                                    },
                                    Name = $"{draw.Name}_{partIndex}",
                                    IsClockwise = draw.IsClockwise,
                                    LayerId = draw.LayerId
                                };
                                cellShapes.Add(polyLine);
                            }
                        }

                        // 同一分区内所有图形合并为一个组合图形
                        if (cellShapes.Count > 1)
                        {
                            var combo = new DrawCombination(cellShapes)
                            {
                                Pen = new SKPaint
                                {
                                    Color = cellShapes[0].Pen.Color,
                                    Style = SKPaintStyle.Stroke,
                                    StrokeWidth = cellShapes[0].Pen.StrokeWidth,
                                    IsAntialias = true
                                },
                                Name = $"{++partIndex}",
                                LayerId = selectedShapes[0].LayerId
                            };
                            newShapes.Add(combo);
                        }
                        else if (cellShapes.Count == 1)
                        {
                            newShapes.Add(cellShapes[0]);
                        }
                    }
                }
            }
            finally
            {
                // 释放预处理的世界路径
                foreach (var (_, worldPath, _) in allShapePaths)
                    worldPath?.Dispose();
            }

            if (newShapes.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.EmptyResult, "分割后没有产生新的图形");

            // 使用 CompositeCommand 确保撤销一致性：先删除原图形，再添加分割后的新图形
            var commands = new List<IDrawingCommand>();
            commands.Add(new CommandRemove(canvas.LayerViewModels, selectedShapes));

            // 按图层添加新图形
            var targetLayer = canvas.ActiveLayer;
            if (targetLayer != null)
                commands.Add(new CommandAdd(targetLayer, newShapes));

            canvas.CommandHistory.Execute(new CompositeCommand("依分区打断", commands));

            canvas.InvalidateVisibleCache();
            if (Context.ShowJumpLine) Context.IsPartialRender = false;
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 将世界路径按裁剪矩形进行轨迹裁剪（逐线段裁剪方式）。
        /// 与 SKPath.Op（面积交集）不同，此方法裁剪的是路径轨迹（描边），
        /// 不会引入矩形边界上的额外线段。
        /// 直线段使用 Liang-Barsky 精确裁剪；曲线段先适度细分再逐段裁剪。
        /// </summary>
        private List<List<SKPoint>> ClipPathToRect(SKPath worldPath, SKRect clipRect)
        {
            var contours = new List<List<SKPoint>>();
            List<SKPoint>? currentChain = null;
            SKPoint contourStart = SKPoint.Empty;
            SKPoint lastPoint = SKPoint.Empty;

            using var iter = worldPath.CreateRawIterator();
            var pts = new SKPoint[4];
            SKPathVerb verb;

            while ((verb = iter.Next(pts)) != SKPathVerb.Done)
            {
                switch (verb)
                {
                    case SKPathVerb.Move:
                        if (currentChain != null && currentChain.Count >= 2)
                            contours.Add(currentChain);
                        currentChain = null;
                        contourStart = pts[0];
                        lastPoint = pts[0];
                        break;

                    case SKPathVerb.Line:
                        ClipAndAppendLine(ref currentChain, contours, lastPoint, pts[1], clipRect);
                        lastPoint = pts[1];
                        break;

                    case SKPathVerb.Cubic:
                        ClipCubicSegment(ref currentChain, contours, lastPoint, pts[1], pts[2], pts[3], clipRect);
                        lastPoint = pts[3];
                        break;

                    case SKPathVerb.Conic:
                        ClipConicSegment(ref currentChain, contours, lastPoint, pts[1], pts[2], iter.ConicWeight(), clipRect);
                        lastPoint = pts[2];
                        break;

                    case SKPathVerb.Quad:
                        ClipQuadSegment(ref currentChain, contours, lastPoint, pts[1], pts[2], clipRect);
                        lastPoint = pts[2];
                        break;

                    case SKPathVerb.Close:
                        // 闭合路径：将收尾线段（当前点→轮廓起点）也作为普通线段裁剪
                        if (!IsPointClose(lastPoint, contourStart))
                            ClipAndAppendLine(ref currentChain, contours, lastPoint, contourStart, clipRect);
                        lastPoint = contourStart;
                        break;
                }
            }

            if (currentChain != null && currentChain.Count >= 2)
                contours.Add(currentChain);

            return contours;
        }

        /// <summary>
        /// 使用 Liang-Barsky 裁剪单条线段，并将结果追加到当前链中。
        /// 若线段可见，追加裁剪后的起止点；若不可见，断开当前链。
        /// </summary>
        private void ClipAndAppendLine(ref List<SKPoint>? currentChain, List<List<SKPoint>> contours,
            SKPoint p1, SKPoint p2, SKRect clipRect)
        {
            if (LiangBarskyClip(p1, p2, clipRect.Left, clipRect.Top, clipRect.Right, clipRect.Bottom,
                out var clipped1, out var clipped2))
            {
                // 裁剪后的线段起点与当前链末尾连续，则直接追加；否则开始新链
                if (currentChain == null || !IsPointClose(currentChain[currentChain.Count - 1], clipped1))
                {
                    if (currentChain != null && currentChain.Count >= 2)
                        contours.Add(currentChain);
                    currentChain = new List<SKPoint> { clipped1 };
                }
                currentChain.Add(clipped2);
            }
            else
            {
                // 线段完全在矩形外，断开当前链
                if (currentChain != null && currentChain.Count >= 2)
                    contours.Add(currentChain);
                currentChain = null;
            }
        }

        /// <summary>
        /// 裁剪三次贝塞尔曲线段：按 GlobalVariableManagement.Resolution 粒度细分为折线，再逐段 Liang-Barsky 裁剪。
        /// </summary>
        private void ClipCubicSegment(ref List<SKPoint>? currentChain, List<List<SKPoint>> contours,
            SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3, SKRect clipRect)
        {
            float stepMm = (float)GlobalVariableManagement.Resolution;
            float arcEstimate = SKPoint.Distance(p0, p1) + SKPoint.Distance(p1, p2) + SKPoint.Distance(p2, p3);
            int segments = Math.Max(2, (int)Math.Ceiling(arcEstimate / stepMm));

            SKPoint prev = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float u = 1f - t;
                float uu = u * u, uuu = uu * u;
                float tt = t * t, ttt = tt * t;
                float x = uuu * p0.X + 3f * uu * t * p1.X + 3f * u * tt * p2.X + ttt * p3.X;
                float y = uuu * p0.Y + 3f * uu * t * p1.Y + 3f * u * tt * p2.Y + ttt * p3.Y;
                var curr = new SKPoint(x, y);
                ClipAndAppendLine(ref currentChain, contours, prev, curr, clipRect);
                prev = curr;
            }
        }

        /// <summary>
        /// 裁剪圆锥曲线段（圆弧/椭圆弧）：按 GlobalVariableManagement.Resolution 粒度细分为折线，再逐段裁剪。
        /// Conic 用于精确表示圆弧（SkiaSharp 中圆的路径表示）。
        /// 有理贝塞尔公式：P(t) = [u²·P0 + 2ut·w·P1 + t²·P2] / [u² + 2ut·w + t²]
        /// </summary>
        private void ClipConicSegment(ref List<SKPoint>? currentChain, List<List<SKPoint>> contours,
            SKPoint p0, SKPoint p1, SKPoint p2, float w, SKRect clipRect)
        {
            float stepMm = (float)GlobalVariableManagement.Resolution;
            float arcEstimate = SKPoint.Distance(p0, p1) + SKPoint.Distance(p1, p2);
            int segments = Math.Max(2, (int)Math.Ceiling(arcEstimate / stepMm));

            SKPoint prev = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float u = 1f - t;
                float basis0 = u * u;
                float basis1 = 2f * u * t * w;
                float basis2 = t * t;
                float denom = basis0 + basis1 + basis2;
                float x = (basis0 * p0.X + basis1 * p1.X + basis2 * p2.X) / denom;
                float y = (basis0 * p0.Y + basis1 * p1.Y + basis2 * p2.Y) / denom;
                var curr = new SKPoint(x, y);
                ClipAndAppendLine(ref currentChain, contours, prev, curr, clipRect);
                prev = curr;
            }
        }

        /// <summary>
        /// 裁剪二次贝塞尔曲线段：按 GlobalVariableManagement.Resolution 粒度细分为折线，再逐段裁剪。
        /// </summary>
        private void ClipQuadSegment(ref List<SKPoint>? currentChain, List<List<SKPoint>> contours,
            SKPoint p0, SKPoint p1, SKPoint p2, SKRect clipRect)
        {
            float stepMm = (float)GlobalVariableManagement.Resolution;
            float arcEstimate = SKPoint.Distance(p0, p1) + SKPoint.Distance(p1, p2);
            int segments = Math.Max(2, (int)Math.Ceiling(arcEstimate / stepMm));

            SKPoint prev = p0;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float u = 1f - t;
                float x = u * u * p0.X + 2f * u * t * p1.X + t * t * p2.X;
                float y = u * u * p0.Y + 2f * u * t * p1.Y + t * t * p2.Y;
                var curr = new SKPoint(x, y);
                ClipAndAppendLine(ref currentChain, contours, prev, curr, clipRect);
                prev = curr;
            }
        }

        /// <summary>
        /// Liang-Barsky 线段裁剪算法。
        /// 将线段 (p1→p2) 裁剪到矩形区域 [xMin, yMin, xMax, yMax]。
        /// 返回 true 表示有可见部分，out 参数为裁剪后的起止点。
        /// </summary>
        private static bool LiangBarskyClip(SKPoint p1, SKPoint p2,
            float xMin, float yMin, float xMax, float yMax,
            out SKPoint clipped1, out SKPoint clipped2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float tMin = 0f, tMax = 1f;

            float[] p = { -dx, dx, -dy, dy };
            float[] q = { p1.X - xMin, xMax - p1.X, p1.Y - yMin, yMax - p1.Y };

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
                    float r = q[i] / p[i];
                    if (p[i] < 0)
                    {
                        if (r > tMax) { clipped1 = clipped2 = SKPoint.Empty; return false; }
                        if (r > tMin) tMin = r;
                    }
                    else
                    {
                        if (r < tMin) { clipped1 = clipped2 = SKPoint.Empty; return false; }
                        if (r < tMax) tMax = r;
                    }
                }
            }

            if (tMin > tMax)
            {
                clipped1 = clipped2 = SKPoint.Empty;
                return false;
            }

            clipped1 = new SKPoint(p1.X + tMin * dx, p1.Y + tMin * dy);
            clipped2 = new SKPoint(p1.X + tMax * dx, p1.Y + tMax * dy);
            return true;
        }

        /// <summary>
        /// 判断两点是否足够接近（距离平方 &lt; 阈值），用于连续性判断。
        /// </summary>
        private bool IsPointClose(SKPoint a, SKPoint b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy < 1e-4f;
        }
        #endregion


        /// <summary>
        /// 环状复制：将选中图形沿圆周复制 count 个实例（含原图），合并为一个 DrawCombination。
        /// radius: 圆心半径(mm)；startAngle: 起始角(°)；intervalAngle: 间隔角(°)。
        /// isAverageDistribute: 均匀铺满360°；isObjectRotate: 副本随角度旋转自身；isCounterClockwise: 逆时针。
        /// </summary>
        internal GraphicResult CircleCopy(double radius, int count, double startAngle, double intervalAngle,
            bool isAverageDistribute, bool isObjectRotate, bool isCounterClockwise)
        {
            var check = CheckSelectionNotEmpty().IsSuccess && CheckCanvas().IsSuccess;
            if (!check) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var selectedShapes = Context.ActiveCanvas!.Selection;
            if (selectedShapes.Count == 0)
                return GraphicResult.Ok();
            if (count < 1)
                return GraphicResult.Ok();

            var angles = DrawObjectExtensions.CreateCircleCopyAngles(
                count,
                startAngle,
                intervalAngle,
                isAverageDistribute,
                isCounterClockwise);

            var allShapes = selectedShapes.CreateCircleCopyResult(
                angles,
                (float)radius,
                isObjectRotate,
                isCounterClockwise);

            var copyPreparation = LayerViewModels.CreateCopyContainerPreparation(
                selectedShapes,
                allShapes);
            if (!copyPreparation.IsSuccess)
                return GraphicResult.Fail(copyPreparation.ErrorCode, copyPreparation.Message);

            var targetLayer = copyPreparation.Value!.TargetLayer;
            var combination = copyPreparation.Value.Combination;

            ((DrawingCanvas)Context.ActiveCanvas).ExecuteContainerReplacement(
                selectedShapes,
                targetLayer,
                combination,
                "环状复制",
                requestRedraw: true,
                publishSelectChanged: true);
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 矩阵复制：将选中图形按行列间距复制为阵列，并将原图与副本合并为一个组合图形。
        /// columnCount/rowCount 为总行列数（含原图），如 2×2 表示原图 + 3 个副本。
        /// </summary>
        internal GraphicResult MatrixCopy(int columnCount, double columnSpace, int rowCount, double rowSpace)
        {
            var check = CheckSelectionNotEmpty().IsSuccess && CheckCanvas().IsSuccess;
            if (!check) return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            var selectedShapes = Context.ActiveCanvas!.Selection.CollectUnlockedDrawObjects();
            if (selectedShapes.Count == 0)
                return GraphicResult.Ok();

            var preparation = DrawObjectExtensions.PrepareMatrixCopy(
                columnCount,
                columnSpace,
                rowCount,
                rowSpace);

            if (!preparation.RequiresCloneGeneration)
                return GraphicResult.Ok();

            var allShapes = selectedShapes.CreateMatrixCopyResult(
                preparation.ColumnCount,
                preparation.RowCount,
                preparation.HorizontalSpacing,
                preparation.VerticalSpacing);

            var copyPreparation = LayerViewModels.CreateCopyContainerPreparation(
                selectedShapes.Cast<IShape>().ToList(),
                allShapes);
            if (!copyPreparation.IsSuccess)
                return GraphicResult.Fail(copyPreparation.ErrorCode, copyPreparation.Message);

            var targetLayer = copyPreparation.Value!.TargetLayer;
            var combination = copyPreparation.Value.Combination;

            ((DrawingCanvas)Context.ActiveCanvas).ExecuteContainerReplacement(
                selectedShapes.Cast<IShape>().ToList(),
                targetLayer,
                combination,
                "矩阵复制",
                requestRedraw: true,
                publishSelectChanged: true);
            return GraphicResult.Ok();
        }
        internal GraphicResult SetMachineBounds(float width, float height)
        {
            if (Context.ActiveCanvas != null && width > 0 && height > 0)
            {
                Context.ActiveCanvas.MachineBounds = new Rect2D(-width / 2, -height / 2, width, height);
                Context.DefaultMachineBounds = new Rect2D(-width / 2, -height / 2, width, height);
            }
            Context.RequestRedraw();
            return GraphicResult.Ok();
        }
        internal GraphicResult SetGridSize(float width, float height)
        {
            if (width >= 0f)
            {
                DocumentContext.Instance.GridSizeX = width;
            }
            if (height >= 0f)
            {
                DocumentContext.Instance.GridSizeY = height;
            }
            return GraphicResult.Ok();
        }

        internal GraphicResult SetMicroMove(float MicroMoveX, float MicroMoveY)
        {
            if (MicroMoveX > 0f)
            {
                DocumentContext.Instance.KeysMoveSharpsStepX = MicroMoveX;
            }
            if (MicroMoveY > 0f)
            {
                DocumentContext.Instance.KeysMoveSharpsStepY = MicroMoveY;
            }
            return GraphicResult.Ok();
        }
        private void PublishNodeEditModeChanged()
        {
            EventBus.Instance.Publish(new EditNodesModeChangedEvent
            {
                IsEditing = Context.IsNodeEditing,
                SubMode = Context.IsNodeEditing ? Context.NodeEditSubMode : NodeEditSubMode.None,
                HasSelectedMoveNode = Context.SelectedMoveNodeWorldPosition.HasValue
            });
        }

        #region 对齐与分布

        /// <summary>
        /// 对齐选中的图形对象
        /// </summary>
        internal GraphicResult Align(AlignSettingsDto settings)
        {
            var check = CheckForMultiShapeOp(1); // 对齐至少需要1个图形
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var shapes = canvas.Selection.CollectCenterTargets();

            if (shapes.Count < 1)
                return GraphicResult.Fail(GraphicErrorCode.InsufficientSelection, "对齐至少需要1个对象");

            // ── 确定对齐基准边界 ──
            SKRect alignBounds;
            DrawObject? referenceShape = null;

            switch (settings.AlignStandard)
            {
                case AlignStandardDto.LastChooseOne:
                    // 使用画布记录的最后选中图形作为基准
                    var lastSelected = canvas.LastSelectedShape as DrawObject;
                    if (lastSelected != null && shapes.Contains(lastSelected))
                    {
                        referenceShape = lastSelected;
                    }
                    else
                    {
                        // 回退：如果 LastSelectedShape 无效，使用列表中最后一个
                        referenceShape = shapes.Last();
                    }
                    alignBounds = referenceShape.GetAABB();
                    break;

                case AlignStandardDto.PageEdge:
                case AlignStandardDto.CanvasArea:
                    // 页面边缘 / 画布区域：使用机台范围作为基准边界
                    var mb = canvas.MachineBounds;
                    alignBounds = new SKRect(mb.X, mb.Y, mb.X + mb.Width, mb.Y + mb.Height);
                    break;

                case AlignStandardDto.PageCenter:
                    // 页面中心：以机台范围的中心点为基准（退化为一个点）
                    var mbc = canvas.MachineBounds;
                    float centerX = mbc.X + mbc.Width / 2f;
                    float centerY = mbc.Y + mbc.Height / 2f;
                    alignBounds = new SKRect(centerX, centerY, centerX, centerY);
                    break;

                case AlignStandardDto.Baseline:
                    // 基线：以坐标原点 (0, 0) 为基准（退化为一个点）
                    alignBounds = new SKRect(0, 0, 0, 0);
                    break;

                default:
                    alignBounds = shapes.CalculateSharpsBounds();
                    break;
            }

            canvas.ExecuteTransformCommand(
                shapes,
                "对齐",
                () =>
                {
                    // ── 应用对齐：同时支持水平 + 垂直方向 ──
                    if (settings.HorizontalAlignType != AlignTypeDto.None)
                        shapes.ApplyAlignment(settings.HorizontalAlignType, alignBounds, referenceShape);

                    if (settings.VerticalAlignType != AlignTypeDto.None)
                        shapes.ApplyAlignment(settings.VerticalAlignType, alignBounds, referenceShape);

                    // 兼容旧代码：如果新属性均为 None，回退到 AlignType
                    if (settings.HorizontalAlignType == AlignTypeDto.None &&
                        settings.VerticalAlignType == AlignTypeDto.None &&
                        settings.AlignType != AlignTypeDto.None)
                        shapes.ApplyAlignment(settings.AlignType, alignBounds, referenceShape);
                },
                resetPartialRenderWhenJumpLineVisible: true);
            return GraphicResult.Ok();
        }



        internal GraphicResult Distribute(DistributeSettingsDto settings)
        {
            var check = CheckForMultiShapeOp(2); // 分布至少需要2个图形
            if (!check.IsSuccess) return check;

            Context.MarkSelectedDirty();

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var shapes = canvas.Selection.CollectCenterTargets();

            if (shapes.Count < 2)
                return GraphicResult.Fail(GraphicErrorCode.InsufficientSelection, "分布至少需要2个对象");

            // ── 解析分布类型：同时支持水平+垂直方向 ──
            var hType = settings.HorizontalDistributeType;
            var vType = settings.VerticalDistributeType;

            // 兼容旧代码：若新属性均为 None，从旧属性推断
            if (hType == DistributeTypeDto.None && vType == DistributeTypeDto.None)
            {
                var t = settings.DistributeType;
                if (t == DistributeTypeDto.AlignLeftDistribute ||
                    t == DistributeTypeDto.AlignCenterDistribute ||
                    t == DistributeTypeDto.AlignRightDistribute ||
                    t == DistributeTypeDto.AlignHorizontalSpaceDistribute)
                    hType = t;
                else
                    vType = t;
            }

            if (hType == DistributeTypeDto.None && vType == DistributeTypeDto.None)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "未指定分布类型");

            // ── 确定分布区域边界 ──
            SKRect areaBounds;
            if (settings.DistributeStandard == DistributeStandardDto.CanvasArea)
            {
                var mb = canvas.MachineBounds;
                areaBounds = new SKRect(mb.X, mb.Y, mb.X + mb.Width, mb.Y + mb.Height);
            }
            else // SelectArea
            {
                areaBounds = shapes.CalculateSharpsBounds();
            }

            canvas.ExecuteTransformCommand(
                shapes,
                "分布",
                () =>
                {
                    // ── 应用水平分布 ──
                    bool isCanvasArea = settings.DistributeStandard == DistributeStandardDto.CanvasArea;
                    if (hType != DistributeTypeDto.None)
                        shapes.ApplyDistribution(hType, areaBounds, isCanvasArea);

                    // ── 应用垂直分布 ──
                    if (vType != DistributeTypeDto.None)
                        shapes.ApplyDistribution(vType, areaBounds, isCanvasArea);
                },
                resetPartialRenderWhenJumpLineVisible: true);
            return GraphicResult.Ok();
        }

        #endregion


        #region 转成点圆

        internal GraphicResult ConvertToDot(ConvertToDotSettingsDto settings)
        {
            var check = CheckNoLockedShapes();
            if (!check.IsSuccess) return check;

            var canvas = Context.ActiveCanvas!;
            var preparation = canvas.Selection.PrepareDotConversionLeaves();
            if (preparation.Sources.Count == 0)
                return GraphicResult.Ok();

            if (settings == null)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "settings 不能为 null");

            float gap = settings.Gap;
            float diameter = settings.Diameter;
            float radius = diameter / 2f;
            bool isCircle = settings.IsCircleType;
            bool needCornerPoints = settings.NeedPointAtCorner;
            float cornerAngleThreshold = settings.IncludedAngle;

            if (gap <= 0)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "间距必须大于0");

            if (diameter <= 0)
                return GraphicResult.Fail(GraphicErrorCode.InvalidArgument, "直径必须大于0");

            var generation = preparation.Leaves.CreateDotGenerationResult(
                gap,
                radius,
                isCircle,
                needCornerPoints,
                cornerAngleThreshold);
            var newShapes = generation.NewShapes;

            var resultPreparation = ((DrawingCanvas)canvas).ActiveLayer
                .CreateActiveLayerResultPreparation(
                    newShapes,
                    "无法将选中图形转换为点/圆");
            if (!resultPreparation.IsSuccess)
                return GraphicResult.Fail(resultPreparation.ErrorCode, resultPreparation.Message);
            var group = new DrawingGroup(newShapes);

            ((DrawingCanvas)canvas).ExecuteSelectionReplacement(
                preparation.Sources.Cast<IShape>().ToList(),
                new ActiveLayerResultPreparation(
                    resultPreparation.Value!.TargetLayer,
                    [group]),
                "转换为点/圆",
                requestRedraw: true);
            return GraphicResult.Ok();
        }
        #endregion

        #region 移动图形
        /// <summary>
        /// 移动图形到指定图层（支持撤销）
        /// </summary>
        /// <param name="shapes"></param>
        /// <param name="targetLayer"></param>
        public void MoveObjectToTargetLayer(IList<IShape> shapes, ILayerViewModel targetLayer)
        {
            if (shapes.Count == 0 || targetLayer == null)
                return;

            var commands = new List<IDrawingCommand>
            {
                new CommandRemove(LayerViewModels, shapes, suppressSelectionPublish: true),
                new CommandAdd(targetLayer, shapes, suppressSelectionPublish: true)
            };
            CommandHistory.Execute(new CompositeCommand("移动到图层", commands));
        }
        #endregion

        /// <summary>
        /// 修改选中的 DrawPolygon 图形的边数/顶点数和类型（正多边形或五角星）。
        /// 保持当前图形的中心和外接圆半径不变，重新生成顶点并触发重绘。
        /// </summary>
        internal GraphicResult AdjustPolygon(int sideCount, PolygonType polygonType)
        {
            var check = CheckCanvas();
            if (!check.IsSuccess) return check;

            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            if (canvas.Selection.All(s => s.IsLocked))
                return GraphicResult.Fail(GraphicErrorCode.ShapeLocked);

            var polygons = canvas.Selection.CollectUnlockedPolygons();

            if (polygons.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected);

            canvas.ExecuteEditCommand(
                polygons.Cast<DrawObject>(),
                "调整多边形",
                () => polygons.ApplyPolygonShape(sideCount, polygonType),
                requestRedraw: true);
            return GraphicResult.Ok();
        }

        /// <summary>
        /// 将选中的 DrawPath 图形闭合（首尾相连形成封闭路径）
        /// </summary>
        /// <returns></returns>
        internal GraphicResult ClosePath()
        {
            var canvas = (DrawingCanvas)Context.ActiveCanvas!;
            var paths = canvas.Selection.CollectOpenClosableTargets();
            if (paths.Count == 0)
                return GraphicResult.Fail(GraphicErrorCode.NothingSelected, "请选择至少一个未闭合的路径");

            canvas.ExecuteEditCommand(
                paths,
                "闭合路径",
                () => paths.ApplyClosePath(),
                requestRedraw: true);

            return GraphicResult.Ok();
        }
    }
}
