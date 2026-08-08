using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event.Tool;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;

namespace DrSoft.Drawing.Controls.Tools
{
    public enum ControlPointType
    {
        None = -1,
        TopLeft = 0,
        TopCenter = 1,
        TopRight = 2,
        MiddleLeft = 3,
        MiddleRight = 4,
        BottomLeft = 5,
        BottomCenter = 6,
        BottomRight = 7
    }

    public class ControlPoint
    {
        public ControlPointType ControlPointType { get; set; }

        public SKPoint SKPoint { get; set; } = SKPoint.Empty;
    }

    public class ToolSelect : ToolBase
    {
        private const double ControlPointSize = 4.0;

        private readonly StickyControlPointSession _stickyControlPointSession;
        private readonly RotationCenterDragSession _rotationCenterDragSession;
        private readonly ControlPointResizeSession _controlPointResizeSession;
        private readonly ControlPointScaleSession _controlPointScaleSession;
        private readonly ControlPointRotationSession _controlPointRotationSession;
        private readonly ControlPointSkewSession _controlPointSkewSession;
        private readonly PathNodeEditSession _pathNodeEditSession;
        private readonly ShapeDragSession _shapeDragSession;
        private readonly BoxSelectionSession _boxSelectionSession;
        private readonly SelectionHitService _selectionHitService;
        private readonly SelectionStateService _selectionStateService;
        private readonly SelectionMouseDownService _selectionMouseDownService;
        private readonly SelectionMouseMoveService _selectionMouseMoveService;
        private readonly SelectionControlPointService _selectionControlPointService;
        private readonly List<IToolSelectSession> _sessions;

        private IToolSelectSession? _activeSession;
        private bool _needRedrawOnDown;
        private bool _needRedrawOnUp;
        private bool _wasAnyDrag;
        private string _lastSessionMessage = "尚未路由到任何会话";

        protected DocumentContext context = DocumentContext.Instance;

        public ToolSelect()
        {
            _pathNodeEditSession = new PathNodeEditSession(context, ControlPointSize);
            _boxSelectionSession = new BoxSelectionSession(context);
            _selectionHitService = new SelectionHitService(context);
            _selectionStateService = new SelectionStateService(context);
            _selectionMouseDownService = new SelectionMouseDownService(
                context,
                _selectionHitService,
                _selectionStateService);
            _selectionControlPointService = new SelectionControlPointService(context);
            _controlPointResizeSession = new ControlPointResizeSession(context);
            _controlPointScaleSession = new ControlPointScaleSession(context);
            _controlPointRotationSession = new ControlPointRotationSession(context);
            _controlPointSkewSession = new ControlPointSkewSession(context);
            _rotationCenterDragSession = new RotationCenterDragSession(context, ControlPointSize);
            _shapeDragSession = new ShapeDragSession(
                context,
                _selectionMouseDownService,
                _selectionStateService,
                _pathNodeEditSession,
                _boxSelectionSession,
                NotifyMenuEvent);
            _selectionMouseMoveService = new SelectionMouseMoveService(
                context,
                _selectionControlPointService,
                _pathNodeEditSession,
                _shapeDragSession,
                _selectionStateService);
            _stickyControlPointSession = new StickyControlPointSession(context, StartStickyControlPointDragging);

            _sessions = new List<IToolSelectSession>
            {
                _stickyControlPointSession,
                _rotationCenterDragSession,
                _controlPointResizeSession,
                _controlPointScaleSession,
                _controlPointRotationSession,
                _controlPointSkewSession,
                _pathNodeEditSession,
                _shapeDragSession,
                _boxSelectionSession
            };
        }

        public override ToolType ToolType => ToolType.Select;

        public override string Name => "选择";

        public override string Icon => "↖";

        public override bool NeedRedrawOnMove =>
            _activeSession?.IsActive == true || _sessions.Any(session => session.IsActive);

        public override bool NeedRedrawOnDown => _needRedrawOnDown;

        public override bool NeedRedrawOnUp => _needRedrawOnUp;

        public override bool OnMouseDown(SKPoint point)
        {
            _needRedrawOnDown = false;

            if (context.IsApplyingDeferredDragCommit)
            {
                return true;
            }

            if (context.ActiveCanvas == null)
            {
                return false;
            }

            // 先让当前活跃会话续吃本次按下，避免拖拽尚未完全收尾时被其他会话抢走。
            IToolSelectSession? handledSession = null;
            if (_activeSession != null
                && _activeSession.TryMouseDown(point, out string activeSessionMessage))
            {
                handledSession = _activeSession;
                RecordSessionMessage(handledSession, activeSessionMessage);
            }
            else
            {
                foreach (var session in _sessions)
                {
                    bool handled = session.TryMouseDown(point, out string sessionMessage);
                    if (!handled)
                    {
                        continue;
                    }

                    handledSession = session;
                    RecordSessionMessage(session, sessionMessage);
                    break;
                }
            }

            if (handledSession == null)
            {
                _lastSessionMessage = "鼠标按下未命中任何会话";
                return false;
            }

            // 各会话的“按下是否需要重绘”语义不同，ToolSelect 在这里做统一收口。
            bool needRedrawOnDown = false;
            if (handledSession is RotationCenterDragSession)
            {
                needRedrawOnDown = true;
            }
            else if (handledSession is PathNodeEditSession pathNodeEditSession)
            {
                needRedrawOnDown = pathNodeEditSession.LastMouseDownNeedRedraw;
            }
            else if (handledSession is ShapeDragSession shapeDragSession)
            {
                needRedrawOnDown = shapeDragSession.LastMouseDownNeedRedraw;
            }

            _needRedrawOnDown = needRedrawOnDown;
            _activeSession = ResolveActiveSession();
            ApplyCursorIfNeeded(handledSession);

            bool activeSessionChanged = !ReferenceEquals(_activeSession, handledSession);
            if (activeSessionChanged)
            {
                ApplyCursorIfNeeded(_activeSession);
            }

            return true;
        }

        public override void OnMouseMove(SKPoint point)
        {
            if (context.ActiveCanvas == null || context.IsApplyingDeferredDragCommit)
            {
                return;
            }

            if (_activeSession?.IsActive == true)
            {
                bool handledByActiveSession = _activeSession.TryMouseMove(point, out string activeSessionMessage);
                if (handledByActiveSession)
                {
                    RecordSessionMessage(_activeSession, activeSessionMessage);
                    _activeSession = ResolveActiveSession();
                    ApplyCursorIfNeeded(_activeSession);
                    return;
                }
            }

            foreach (var session in _sessions)
            {
                bool handled = session.TryMouseMove(point, out string sessionMessage);
                if (!handled)
                {
                    continue;
                }

                RecordSessionMessage(session, sessionMessage);
                _activeSession = ResolveActiveSession();
                ApplyCursorIfNeeded(session);
                if (!ReferenceEquals(_activeSession, session))
                {
                    ApplyCursorIfNeeded(_activeSession);
                }
                return;
            }

            _lastSessionMessage = "鼠标移动未命中任何会话，进入悬停光标分支";
            ApplyFallbackHoverCursor(point);
        }

        public override bool OnMouseRightDown()
        {
            if (context.IsApplyingDeferredDragCommit)
            {
                return true;
            }

            SKPoint point = SKPoint.Empty;
            if (_activeSession != null
                && _activeSession.TryRightMouseDown(point, out string activeSessionMessage))
            {
                RecordSessionMessage(_activeSession, activeSessionMessage);
                _activeSession = ResolveActiveSession();
                CancelUnexpectedActiveSessionsAfterMouseUp();
                return true;
            }

            foreach (var session in _sessions)
            {
                bool handled = session.TryRightMouseDown(point, out string sessionMessage);
                if (!handled)
                {
                    continue;
                }

                RecordSessionMessage(session, sessionMessage);
                _activeSession = ResolveActiveSession();
                CancelUnexpectedActiveSessionsAfterMouseUp();
                return true;
            }

            CancelUnexpectedActiveSessionsAfterMouseUp();
            ClearSelection();
            _activeSession = null;
            _lastSessionMessage = "右键未命中会话，执行取消选择";
            context.ReportStatus("取消选择");
            return true;
        }

        public override bool OnMouseUp(SKPoint point)
        {
            _needRedrawOnUp = false;

            if (context.IsApplyingDeferredDragCommit)
            {
                return true;
            }

            if (context.ActiveCanvas == null)
            {
                return false;
            }

            IToolSelectSession? handledSession = null;
            IToolSelectSession? sessionBeforeMouseUp = _activeSession;
            // ShapeDragSession 在“只点击未真正拖动”时也会消费 MouseUp。
            // 这里先记住抬起前是否已经进入真实拖拽，避免误把点击当成一次 drag completion。
            bool shapeDragWasDraggingBeforeMouseUp = sessionBeforeMouseUp is ShapeDragSession activeShapeDragSession
                && activeShapeDragSession.IsDragging;
            if (sessionBeforeMouseUp?.IsActive == true)
            {
                bool handledByActiveSession = sessionBeforeMouseUp.TryMouseUp(point, out string activeSessionMessage);
                if (handledByActiveSession)
                {
                    handledSession = sessionBeforeMouseUp;
                    RecordSessionMessage(handledSession, activeSessionMessage);
                }
            }

            if (handledSession == null)
            {
                foreach (var session in _sessions)
                {
                    bool handled = session.TryMouseUp(point, out string sessionMessage);
                    if (!handled)
                    {
                        continue;
                    }

                    handledSession = session;
                    RecordSessionMessage(session, sessionMessage);
                    break;
                }
            }

            if (handledSession != null)
            {
                bool needRedrawOnUp = false;
                if (handledSession is RotationCenterDragSession)
                {
                    needRedrawOnUp = true;
                }
                else if (handledSession is BoxSelectionSession boxSelectionSession)
                {
                    needRedrawOnUp = boxSelectionSession.LastMouseUpNeedRedraw;
                }
                else if (handledSession is PathNodeEditSession pathNodeEditSession)
                {
                    needRedrawOnUp = pathNodeEditSession.LastMouseUpNeedRedraw;
                }

                _needRedrawOnUp = needRedrawOnUp;

                ControlPointType? completedControlPoint = handledSession.CompletedControlPoint;
                if (completedControlPoint.HasValue)
                {
                    // 控制点拖拽结束后，允许用户在同一手柄附近再次按下继续拖，
                    // 不需要重新精确命中控制点。
                    Type sessionType = handledSession.GetType();
                    _stickyControlPointSession.Arm(completedControlPoint.Value, point, sessionType);
                }
                else
                {
                    bool shouldClearStickyControlPoint = handledSession is ShapeDragSession
                        or PathNodeEditSession
                        or RotationCenterDragSession;
                    if (shouldClearStickyControlPoint)
                    {
                        _stickyControlPointSession.Clear();
                    }
                }

                bool isPathNodeDragCompletion = handledSession is PathNodeEditSession completedPathNodeEditSession
                    && !completedPathNodeEditSession.LastMouseUpNeedRedraw;
                bool isShapeDragCompletion = handledSession is ShapeDragSession
                    && shapeDragWasDraggingBeforeMouseUp;
                bool isDragCompletionSession = handledSession is ControlPointResizeSession
                    or ControlPointScaleSession
                    or ControlPointRotationSession
                    or ControlPointSkewSession
                    or RotationCenterDragSession
                    || isShapeDragCompletion
                    || isPathNodeDragCompletion;
                if (isDragCompletionSession)
                {
                    _wasAnyDrag = true;
                }
            }
            else
            {
                _lastSessionMessage = "鼠标抬起未命中任何会话";
            }

            CancelUnexpectedActiveSessionsAfterMouseUp();
            _activeSession = ResolveActiveSession();
            context.IsDrawing = false;

            bool hasActiveControlPointSession = _sessions.Any(session =>
                session.IsActive
                && (session is ControlPointResizeSession
                    or ControlPointScaleSession
                    or ControlPointRotationSession
                    or ControlPointSkewSession));
            context.IsDragControlPoint = hasActiveControlPointSession;
            if (_activeSession?.SuggestedCursor != null)
            {
                ApplyCursorIfNeeded(_activeSession);
            }
            else
            {
                ApplyFallbackHoverCursor(point);
            }

            context.CachedDragPreviewBounds = null;
            context.CachedDragPreviewCorners = null;
            _pathNodeEditSession.ResetTransientState();
            return true;
        }

        public override bool OnLeftMounseUp(SKPoint point)
        {
            if (context.ActiveCanvas == null)
            {
                return false;
            }

            if (context.ActiveCanvas.SelectedShapeCount > 0)
            {
                bool isPathEditing = context.ActiveCanvas.Selection.FirstOrDefault()?.IsPathEditing == true;
                bool wasAnyDrag = _wasAnyDrag || isPathEditing;

                // 纯点击用于在 First/Second/ThirdSelected 三种框态间轮转。
                // 真实拖拽完成后则不推进框态，避免一次 resize/rotate 后又被点击逻辑切走。
                if (wasAnyDrag)
                {
                    bool keepCurrentState = context.SelectState == SelectState.ThirdSelected
                        || context.SelectState == SelectState.SecondSelected;
                    if (!keepCurrentState)
                    {
                        context.SelectState = SelectState.FirstSelected;
                    }
                }
                else
                {
                    int lastStateIndex = SelectState.GetValues<SelectState>().Count() - 1;
                    bool reachedLastState = (int)context.SelectState == lastStateIndex;
                    if (reachedLastState)
                    {
                        context.SelectState = SelectState.FirstSelected;
                    }
                    else
                    {
                        bool isShiftPressed = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                        if (!isShiftPressed)
                        {
                            context.SelectState = context.SelectState + 1;
                        }
                    }
                }

                _wasAnyDrag = false;
                _needRedrawOnUp = true;
            }

            return true;
        }

        public override void OnCancel()
        {
            base.OnCancel();

            if (context.IsApplyingDeferredDragCommit)
            {
                return;
            }

            foreach (var session in _sessions)
            {
                session.Cancel();
            }

            _activeSession = null;
            _needRedrawOnDown = false;
            _needRedrawOnUp = false;
            _wasAnyDrag = false;
            context.CachedDragPreviewBounds = null;
            context.CachedDragPreviewCorners = null;
            ClearSelection();
        }

        public void SetDeleteNodesMode(bool turnOn) => _pathNodeEditSession.SetDeleteNodesMode(turnOn);

        public void SetAddNodesMode(bool turnOn) => _pathNodeEditSession.SetAddNodesMode(turnOn);

        public void SetSeparateNodesMode(bool turnOn) => _pathNodeEditSession.SetSeparateNodesMode(turnOn);

        public void EnterNodeEditMode() => _pathNodeEditSession.EnterNodeEditMode();

        public void ExitNodeEditMode() => _pathNodeEditSession.ExitNodeEditMode();

        public void SetNodeEditSubMode(NodeEditSubMode subMode) => _pathNodeEditSession.SetNodeEditSubMode(subMode);

        public bool IsNodeEditing => _pathNodeEditSession.IsNodeEditing;

        public NodeEditSubMode CurrentNodeEditSubMode => _pathNodeEditSession.NodeEditSubMode;

        public void SetMoveNodeSelection(DrawCombination combo, DrawObject child, int pointIndex) =>
            _pathNodeEditSession.SetMoveNodeSelection(combo, child, pointIndex);

        public void ClearSelectedMoveNode() => _pathNodeEditSession.ClearSelectedMoveNode();

        public (DrawCombination combo, DrawObject child, int pointIndex, SKPoint currentWorldPos)? GetSelectedMoveNodeInfo() =>
            _pathNodeEditSession.GetSelectedMoveNodeInfo();

        public bool HasSelectedMoveNode() => _pathNodeEditSession.HasSelectedMoveNode();

        public bool HasSelectedPathNodes() => _pathNodeEditSession.HasSelectedPathNodes();

        public int GetSelectedPathNodeCount() => _pathNodeEditSession.SelectedPathNodeCount;

        public bool ExtendSelectedPathNodes() => _pathNodeEditSession.ExtendSelectedPathNodes();

        public bool ConnectSelectedPathNodes() => _pathNodeEditSession.ConnectSelectedPathNodes();

        public void RefreshPathNodeSelectionVisualState() => _pathNodeEditSession.RefreshSelectionVisualState();

        private IToolSelectSession? ResolveActiveSession()
        {
            IToolSelectSession? activeSession = _sessions.FirstOrDefault(session => session.IsActive);
            return activeSession;
        }

        private void StartStickyControlPointDragging(Type sessionType, ControlPointType controlPointType, SKPoint point)
        {
            _stickyControlPointSession.Clear();

            // Sticky restart 要沿用上一次结束时的会话类型，
            // 不能再仅靠当前 SelectState 推断，否则会把 rotation/skew/scale 重启成 resize。
            if (sessionType == typeof(ControlPointScaleSession))
            {
                SKRect scaleMergedBounds = context.CalculateMergedBounds();
                _controlPointScaleSession.Start(controlPointType, point, scaleMergedBounds);
                return;
            }

            if (sessionType == typeof(ControlPointRotationSession))
            {
                SKRect rotationMergedBounds = context.CalculateMergedBounds();
                _controlPointRotationSession.Start(controlPointType, point, rotationMergedBounds);
                return;
            }

            if (sessionType == typeof(ControlPointSkewSession))
            {
                SKRect skewMergedBounds = context.CalculateMergedBounds();
                _controlPointSkewSession.Start(controlPointType, point, skewMergedBounds);
                return;
            }

            SKRect resizeMergedBounds = context.CalculateMergedBounds();
            _controlPointResizeSession.Start(controlPointType, point, resizeMergedBounds);
        }

        private void ApplyFallbackHoverCursor(SKPoint point)
        {
            int selectedShapeCount = context.ActiveCanvas?.SelectedShapeCount ?? 0;
            if (selectedShapeCount > 0)
            {
                // Second/ThirdSelected 的手柄布局基于 AABB 语义，
                // hover 命中和光标方向都需要走 RS 专用分支。
                if (context.SelectState == SelectState.SecondSelected
                    || context.SelectState == SelectState.ThirdSelected)
                {
                    SelectionHoverResult hoverResult = _selectionMouseMoveService.GetHoverCursorRS(point);
                    if (hoverResult.Kind == SelectionHoverCursorKind.ControlPoint)
                    {
                        SetCursorForControlPointRS(hoverResult.ControlPointType);
                        return;
                    }
                }
                else
                {
                    SelectionHoverResult hoverResult = _selectionMouseMoveService.GetHoverCursor(point);
                    if (hoverResult.Kind == SelectionHoverCursorKind.ControlPoint)
                    {
                        SetCursorForControlPoint(hoverResult.ControlPointType);
                        return;
                    }

                    if (hoverResult.Kind == SelectionHoverCursorKind.Custom
                        && hoverResult.CustomCursorName != null)
                    {
                        try
                        {
                            Cursor customCursor = CanvasCursorFactory.GetCursor(
                                hoverResult.CustomCursorName,
                                Cursors.Arrow);
                            context.SetCursor(customCursor);
                        }
                        catch
                        {
                        }
                        return;
                    }
                }

                _selectionStateService.UpdateHoverState(point, _selectionHitService);
                if (_selectionStateService.IsOverSelectedShape)
                {
                    return;
                }
            }

            Cursor pointerCursor = CanvasCursorFactory.GetCursor("pointer", Cursors.Arrow);
            context.SetCursor(pointerCursor);
        }

        private void ApplyCursorIfNeeded(IToolSelectSession? session)
        {
            Cursor? suggestedCursor = session?.SuggestedCursor;
            if (suggestedCursor == null)
            {
                return;
            }

            context.SetCursor(suggestedCursor);
        }

        private void RecordSessionMessage(IToolSelectSession session, string message)
        {
            _lastSessionMessage = $"{session.Name}: {message}";
        }

        private void CancelUnexpectedActiveSessionsAfterMouseUp()
        {
            CancelSessionIfActive(_rotationCenterDragSession);
            CancelSessionIfActive(_controlPointResizeSession);
            CancelSessionIfActive(_controlPointScaleSession);
            CancelSessionIfActive(_controlPointRotationSession);
            CancelSessionIfActive(_controlPointSkewSession);
            CancelSessionIfActive(_shapeDragSession);
            CancelSessionIfActive(_boxSelectionSession);
            CancelSessionIfActive(_pathNodeEditSession);
        }

        private static void CancelSessionIfActive(IToolSelectSession session)
        {
            if (!session.IsActive)
            {
                return;
            }

            session.Cancel();
        }

        private void ClearSelection()
        {
            _selectionStateService.ClearSelection(() =>
            {
                _pathNodeEditSession.ClearSelectedMoveNode();
                _pathNodeEditSession.ClearSelectedPathNodes();
            });
        }

        private void SetCursorForControlPoint(ControlPointType controlPointType)
        {
            if (context.ActiveCanvas == null)
            {
                return;
            }

            bool allLocked = context.ActiveCanvas.Selection.All(item => item.IsLocked);
            if (allLocked)
            {
                return;
            }

            if (context.ActiveCanvas.SelectedShapeCount == 1
            && context.ActiveCanvas.Selection.FirstOrDefault() is DrawObject drawObject)
            {
                Cursor cursor = _selectionControlPointService.GetCursorForControlPoint(drawObject, controlPointType);
                context.SetCursor(cursor);
                return;
            }

            SKRect mergedBounds = context.CalculateMergedBounds();
            Cursor mergedCursor = _selectionControlPointService.GetCursorForMergedBounds(
                mergedBounds,
                controlPointType);
            context.SetCursor(mergedCursor);
        }

        private void SetCursorForControlPointRS(ControlPointType controlPointType)
        {
            if (context.ActiveCanvas == null)
            {
                return;
            }

            bool allLocked = context.ActiveCanvas.Selection.All(item => item.IsLocked);
            if (allLocked)
            {
                return;
            }

            if (context.SelectState == SelectState.ThirdSelected)
            {
                SetCursorForThirdSelectedControlPoint(controlPointType);
                return;
            }

            if (context.SelectState == SelectState.SecondSelected)
            {
                SetCursorForSecondSelectedControlPoint(controlPointType);
                return;
            }

            if (context.ActiveCanvas.SelectedShapeCount == 1
                && context.ActiveCanvas.Selection.FirstOrDefault() is DrawObject drawObject)
            {
                Cursor cursor = _selectionControlPointService.GetCursorForControlPoint(drawObject, controlPointType);
                context.SetCursor(cursor);
                return;
            }

            SKRect mergedBounds = context.CalculateMergedBounds();
            Cursor mergedCursor = _selectionControlPointService.GetCursorForMergedBounds(
                mergedBounds,
                controlPointType);
            context.SetCursor(mergedCursor);
        }

        private void SetCursorForThirdSelectedControlPoint(ControlPointType controlPointType)
        {
            Cursor cursor = controlPointType switch
            {
                ControlPointType.TopLeft or ControlPointType.BottomRight => Cursors.Hand,
                ControlPointType.TopRight or ControlPointType.BottomLeft => Cursors.Hand,
                ControlPointType.TopCenter or ControlPointType.BottomCenter => Cursors.SizeWE,
                ControlPointType.MiddleLeft or ControlPointType.MiddleRight => Cursors.SizeNS,
                _ => Cursors.Arrow
            };
            context.SetCursor(cursor);
        }

        private void SetCursorForSecondSelectedControlPoint(ControlPointType controlPointType)
        {
            Cursor cursor = controlPointType switch
            {
                ControlPointType.TopLeft or ControlPointType.BottomRight => Cursors.SizeNWSE,
                ControlPointType.TopRight or ControlPointType.BottomLeft => Cursors.SizeNESW,
                ControlPointType.TopCenter or ControlPointType.BottomCenter => Cursors.SizeNS,
                ControlPointType.MiddleLeft or ControlPointType.MiddleRight => Cursors.SizeWE,
                _ => Cursors.Arrow
            };
            context.SetCursor(cursor);
        }
    }
}
