using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Event.Tool;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.UI.Models;
using System.Diagnostics;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.ViewModes.Tools
{
    /// <summary>
    /// 节点编辑工具栏状态机。
    /// 负责在画布选区、节点编辑模式和工具栏按钮之间同步互斥关系与可用状态。
    /// </summary>
    public partial class EditPathNodesToolViewModel : ObservableObject
    {
        private readonly IEventBus _eventBus;

        private readonly IShapeService _shapeService;
        private bool _canEditSelection;
        // _isNodeEditing 已移除，直接使用 _shapeService.IsNodeEditing（即 DocumentContext.IsNodeEditing）
        // 作为节点编辑模式的唯一来源，避免 VM 侧维护一份可能与画布侧不一致的副本。
        private bool _hasSelectedMoveNode;
        private bool _canExtendSelectedPathNodes;
        private bool _canConnectSelectedPathNodes;
        private NodeEditSubMode _currentSubMode;
        // 防止 EditNodes → PublishSelectChanged → UpdateCommandStates → EditNodes 的重入循环
        private bool _isEditingNodesInProgress;


        public ICommand editNodeCommand { get; }
        public ICommand addNodeCommand { get; }
        public ICommand deleteNodeCommand { get; }
        public ICommand separateNodeCommand { get; }
        public ICommand moveNodeCommand { get; }
        public ICommand extendNodeCommand { get; }
        public ICommand connectNodeCommand { get; }
        public ICommand selectNodeCommand { get; }

        public List<MenuToolCommand<EditNodeCommandType, bool, GraphicResult>> Commands { get; set; }

        public Dictionary<EditNodeCommandType, MenuToolCommand<EditNodeCommandType, bool, GraphicResult>> CommandMap => Commands?.ToDictionary(c => c.Command);

        /// <summary>
        /// 由 ToolbarViewModel 注入：刷新 UI 按钮（curveToolGroup）的 CanExecute 状态。
        /// </summary>
        public Action RefreshUICommands { get; set; }

        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> editCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> addCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> deleteCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> moveCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> separateCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> extendCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> connectCmd;
        private MenuToolCommand<EditNodeCommandType, bool, GraphicResult> selectCmd;

        /// <summary>
        /// 初始化节点编辑命令，并订阅画布/工具事件以保持工具栏状态同步。
        /// </summary>
        public EditPathNodesToolViewModel(IShapeService shapeService)
        {
            try
            {
                _eventBus = EventBus.Instance;

                _shapeService = shapeService;

                editNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                addNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                deleteNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                separateNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                moveNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                extendNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                connectNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);
                selectNodeCommand = new RelayCommand<string>(CommandExcute, CanExcute);

                Commands = new List<MenuToolCommand<EditNodeCommandType, bool, GraphicResult>>
                {
                    new MenuToolCommand<EditNodeCommandType,bool, GraphicResult> { Command = EditNodeCommandType.Edit, UICommand = editNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.EditNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Add, UICommand = addNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.AddNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Delete, UICommand = deleteNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.DeleteNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Separate, UICommand = separateNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.SeparateNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Move, UICommand = moveNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.MoveNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Extend, UICommand = extendNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.ExtendNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Connect, UICommand = connectNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.ConnectNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                    new MenuToolCommand<EditNodeCommandType, bool, GraphicResult> { Command = EditNodeCommandType.Select, UICommand = selectNodeCommand, ParmAction = new Action<bool>((isChecked) => _shapeService.SelectNodes(isChecked)), Active = false, IsDialogConfirmed = false },
                };

                // 获取其他相关命令
                editCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Edit)!;
                addCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Add)!;
                deleteCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Delete)!;
                moveCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Move)!;
                separateCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Separate)!;
                extendCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Extend)!;
                connectCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Connect)!;
                selectCmd = Commands.FirstOrDefault(c => c.Command == EditNodeCommandType.Select)!;
                // 订阅相关事件以更新命令状态
                _eventBus.Subscribe<CanvasChangedEvent>(data =>
                {
                    if (data.ChangeType == CanvasChangeType.SelectChanged)
                    {
                        UpdateCommandStates(data.Data as Dictionary<ShapeType, SelectChangedInfo>);
                    }
                    else if (data.ChangeType == CanvasChangeType.Created || data.ChangeType == CanvasChangeType.Switched)
                    {
                        if (data.Data is null)
                        {
                            Dictionary<ShapeType, SelectChangedInfo>? selectedObjects = null;
                            UpdateCommandStates(selectedObjects);
                        }
                    }
                });

                _eventBus.Subscribe<CommandCapabilityChangedEvent>(data =>
                {
                    _canEditSelection = data.Capabilities.CanEnterNodeEdit;
                    ApplyToolbarState();
                });

                // 订阅节点编辑模式变更事件。
                // EditNodesModeChangedEvent 由 DrawingCanvas.Services 的 EditNodes / SetNodeEditSubMode
                // 通过 PathNodeEditSession.PublishNodeEditStateChanged 发布，是节点编辑状态的唯一权威来源。
                // 收到事件后同步本地辅助字段（SubMode、HasSelectedMoveNode、_canEditSelection），
                // 节点编辑模式本身直接读取 _shapeService.IsNodeEditing，不再本地缓存。
                _eventBus.Subscribe<EditNodesModeChangedEvent>(data =>
                {
                    if (data.IsEditing)
                    {
                        // 进入节点编辑模式时，强制同步 _canEditSelection，
                        // 防止滞后的 SelectChanged 用旧值 false 覆盖。
                        _canEditSelection = true;
                    }
                    _currentSubMode = data.IsEditing ? data.SubMode : NodeEditSubMode.None;
                    _hasSelectedMoveNode = data.IsEditing && data.HasSelectedMoveNode;
                    _canExtendSelectedPathNodes = data.IsEditing && data.CanExtendSelectedPathNodes;
                    _canConnectSelectedPathNodes = data.IsEditing && data.CanConnectSelectedPathNodes;
                    ApplyToolbarState();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        /// <summary>
        /// 接受画布的选中数据，更新编辑菜单下各命令的状态（可用/不可用）
        /// </summary>
        /// <param name="selectedObjects"></param>
        private void UpdateCommandStates(Dictionary<ShapeType, SelectChangedInfo>? selectedObjects)
        {
            if (selectedObjects != null)
            {
                if (_canEditSelection)
                {
                    var obj = selectedObjects.FirstOrDefault().Value;

                    // 当选中图形已处于 PathEditing 状态，但全局 IsNodeEditing 未同步时，
                    // 自动进入节点编辑模式以保持状态一致。
                    // 用 _isEditingNodesInProgress 防止 EditNodes 内部 PublishSelectChanged
                    // 再次回调到此处形成重入循环。
                    if (obj.AllPathEditing && !_shapeService.IsNodeEditing && !_isEditingNodesInProgress)
                    {
                        _isEditingNodesInProgress = true;
                        try
                        {
                            _shapeService.EditNodes(true);
                        }
                        finally
                        {
                            _isEditingNodesInProgress = false;
                        }
                        // EditNodes 会发布 EditNodesModeChangedEvent，其订阅回调会同步
                        // _currentSubMode、_hasSelectedMoveNode 并调用 ApplyToolbarState，
                        // 因此这里直接返回，避免滞后状态覆盖。
                        return;
                    }

                    // 节点命中后的工具栏激活以 EditNodesModeChangedEvent（_shapeService.IsNodeEditing）为准。
                    // SelectChanged 可能因后续选区刷新而滞后到达，不能用旧的 false 覆盖已进入的节点编辑态。
                    bool isNodeEditing = _shapeService.IsNodeEditing || obj.AllPathEditing;
                    if (!isNodeEditing)
                    {
                        _hasSelectedMoveNode = false;
                        _canExtendSelectedPathNodes = false;
                        _canConnectSelectedPathNodes = false;
                        _currentSubMode = NodeEditSubMode.None;
                    }
                    else
                    {
                        _hasSelectedMoveNode = _hasSelectedMoveNode || obj.IsSelectedMoveNode;
                    }
                }
                else
                {
                    _hasSelectedMoveNode = false;
                    _canExtendSelectedPathNodes = false;
                    _canConnectSelectedPathNodes = false;
                    _currentSubMode = NodeEditSubMode.None;
                }
            }
            else
            {
                _canEditSelection = false;
                _hasSelectedMoveNode = false;
                _canExtendSelectedPathNodes = false;
                _canConnectSelectedPathNodes = false;
                _currentSubMode = NodeEditSubMode.None;
            }

            ApplyToolbarState();
        }

        /// <summary>
        /// 通知 ToolbarViewModel 里真正绑定到 UI 按钮的 RelayCommand 刷新 CanExecute 状态。
        /// </summary>
        private void NotifyAllCanExecuteChanged()
        {
            RefreshUICommands?.Invoke();
        }

        public bool CanExcute(string parameter)
        {
            // 根据业务逻辑返回 true/false
            bool canExcute = false;
            if (Enum.TryParse<EditNodeCommandType>(parameter, true, out var nodeCommand))
            {
                MenuToolCommand<EditNodeCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(nodeCommand);
                canExcute = cmd.Active;
            }
            return canExcute;
        }

        [RelayCommand]
        public void CommandExcute(string parameter)
        {
            if (Enum.TryParse<EditNodeCommandType>(parameter, true, out var menuChooseCommand))
            {
                MenuToolCommand<EditNodeCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(menuChooseCommand);
                if (!cmd.Active) return;

                // Move 命令是单次执行按钮：直接调用，不保持 toggle 状态
                if (menuChooseCommand == EditNodeCommandType.Move
                    || menuChooseCommand == EditNodeCommandType.Extend
                    || menuChooseCommand == EditNodeCommandType.Connect)
                {
                    cmd.ParmAction?.Invoke(true);
                    ApplyToolbarState();
                    return;
                }

                if (menuChooseCommand == EditNodeCommandType.Edit)
                {
                    // 直接调用 service 进入/退出节点编辑，不再通过 EditCommandToggledEvent 绕道
                    // CanvasViewModel。EditNodes 内部会发布 EditNodesModeChangedEvent，
                    // 本 ViewModel 的订阅回调负责同步辅助字段和按钮状态。
                    bool enterEdit = !_shapeService.IsNodeEditing;
                    _shapeService.EditNodes(enterEdit);
                    return;
                }

                bool shouldCheck = !cmd.IsChecked;
                cmd.ParmAction?.Invoke(shouldCheck);
            }
        }

        private void ApplyToolbarState()
        {
            bool isNodeEditing = _shapeService.IsNodeEditing;
            if (editCmd != null)
            {
                editCmd.Active = _canEditSelection;
                editCmd.IsChecked = isNodeEditing;
            }

            bool nodeActionsActive = _canEditSelection && isNodeEditing;
            if (addCmd != null)
            {
                addCmd.Active = nodeActionsActive;
                addCmd.IsChecked = _currentSubMode == NodeEditSubMode.Add;
            }

            if (deleteCmd != null)
            {
                deleteCmd.Active = nodeActionsActive;
                deleteCmd.IsChecked = _currentSubMode == NodeEditSubMode.Delete;
            }

            if (separateCmd != null)
            {
                separateCmd.Active = nodeActionsActive;
                separateCmd.IsChecked = _currentSubMode == NodeEditSubMode.Separate;
            }

            if (extendCmd != null)
            {
                extendCmd.Active = nodeActionsActive && _canExtendSelectedPathNodes;
                extendCmd.IsChecked = false;
            }

            if (connectCmd != null)
            {
                connectCmd.Active = nodeActionsActive && _canConnectSelectedPathNodes;
                connectCmd.IsChecked = false;
            }

            if (selectCmd != null)
            {
                selectCmd.Active = nodeActionsActive;
                selectCmd.IsChecked = _currentSubMode == NodeEditSubMode.Select;
            }

            if (moveCmd != null)
            {
                moveCmd.Active = nodeActionsActive && _currentSubMode == NodeEditSubMode.None && _hasSelectedMoveNode;
                moveCmd.IsChecked = false;
            }

            foreach (var cmd in Commands.Where(c =>
                c.Command != EditNodeCommandType.Edit &&
                c.Command != EditNodeCommandType.Add &&
                c.Command != EditNodeCommandType.Delete &&
                c.Command != EditNodeCommandType.Separate &&
                c.Command != EditNodeCommandType.Extend &&
                c.Command != EditNodeCommandType.Connect &&
                c.Command != EditNodeCommandType.Select &&
                c.Command != EditNodeCommandType.Move))
            {
                cmd.IsChecked = false;
            }

            NotifyAllCanExecuteChanged();
        }
    }
}
