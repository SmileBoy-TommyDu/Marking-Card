using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.Models;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Windows; // added for focus helpers
using System.Windows.Input;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.ViewModes.EditMenu
{
    public partial class EditMenuViewModel : ObservableObject
    {
        public List<MenuToolCommand<EditMenuCommandType, bool, GraphicResult>> Commands { get; set; }

        public Dictionary<EditMenuCommandType, MenuToolCommand<EditMenuCommandType, bool, GraphicResult>> CommandMap => Commands?.ToDictionary(c => c.Command);

        private readonly IEventBus _eventBus;
        private static IDialogService _dialogService;
        private readonly IDrawingService _drawingService;

        public ICommand unDoCommand { get; }
        public ICommand reDoCommand { get; }

        public ICommand cutCommand { get; }
        public ICommand copyCommand { get; }
        public ICommand pasteCommand { get; }
        public ICommand deleteCommand { get; }
        public ICommand chooseAllCommand { get; }
        public ICommand chooseNoSelectCommand { get; }
        public ICommand displaceCommand { get; }
        public ICommand combineCommand { get; }
        public ICommand unCombineCommand { get; }
        public ICommand unCombineFillCommand { get; }
        public ICommand groupCommand { get; }
        public ICommand unGroupCommand { get; }
        public ICommand vectorCombinationCommand { get; }
        public ICommand moveToNewLayerCommand { get; }
        public ICommand inverseCommand { get; }
        public ICommand horizontalMirrorReflectionCommand { get; }
        public ICommand verticalMirrorReflectionCommand { get; }
        public ICommand materialCenterCommand { get; }
        public ICommand convertToPointOrCircleCommand { get; }
        public ICommand convertToExtendNodeCommand { get; }
        public ICommand jumpPointCommand { get; }
        public ICommand alignCommand { get; }
        public ICommand distributionCommand { get; }
        public ICommand extendHeadAndTailCommand { get; }
        public ICommand skyWritingCommand { get; }
        public ICommand partitionCommand { get; }
        public ICommand lockCommand { get; }

        [ObservableProperty]
        private string _lockHeaderName = "";
        
        public EditMenuViewModel(IDialogService dialogService, IDrawingService drawingService)
        {
            _eventBus = EventBus.Instance;
            _dialogService = dialogService;
            _drawingService = drawingService;

            unDoCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            reDoCommand = new RelayCommand<string>(TriggerCommand, CanExcute);

            cutCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            copyCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            pasteCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            deleteCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            chooseAllCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            chooseNoSelectCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            displaceCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            combineCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            unCombineCommand = new RelayCommand<string>(TriggerCommand, CanExcute);

            unCombineFillCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            groupCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            unGroupCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            vectorCombinationCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            moveToNewLayerCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            inverseCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            horizontalMirrorReflectionCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            verticalMirrorReflectionCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            materialCenterCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            convertToPointOrCircleCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            convertToExtendNodeCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            jumpPointCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            alignCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            distributionCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            extendHeadAndTailCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            skyWritingCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            partitionCommand = new RelayCommand<string>(TriggerCommand, CanExcute);
            lockCommand = new RelayCommand<string>(TriggerCommand, CanExcute);

            Commands = new List<MenuToolCommand<EditMenuCommandType, bool, GraphicResult>>
            {
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Undo, UICommand = unDoCommand, Active = false, IsDialogConfirmed = false, Action = new UniversalAction(() =>
                {
                    _drawingService.Shapes.Undo();
                    CommandMap[EditMenuCommandType.Redo].Active = true;
                    if (CommandMap[EditMenuCommandType.Redo].UICommand is RelayCommand<string> relayCommand)
                    {
                        relayCommand.NotifyCanExecuteChanged();
                    }
                    WeakReferenceMessenger.Default.Send("EditMenuCommandStateUpdate");
                    }) },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Redo, UICommand = reDoCommand, Active = false, IsDialogConfirmed = false, Action = new UniversalAction(() =>
                {
                    _drawingService.Shapes.Redo();
                    CommandMap[EditMenuCommandType.Undo].Active = true;
                    if (CommandMap[EditMenuCommandType.Undo].UICommand is RelayCommand<string> relayCommand)
                    {
                        relayCommand.NotifyCanExecuteChanged();
                    }
                    WeakReferenceMessenger.Default.Send("EditMenuCommandStateUpdate");
                }) },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Cut, UICommand = cutCommand, FuncResult = new Func<GraphicResult>(() => _drawingService.Shapes.Cut()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Copy, UICommand = copyCommand, FuncResult = new Func<GraphicResult>(() => _drawingService.Shapes.Copy()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Paste, UICommand = pasteCommand, Action = new UniversalAction(() => _drawingService.Shapes.Paste()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Delete, UICommand = deleteCommand, Action = new UniversalAction(() => _drawingService.Shapes.Delete()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.SelectAll, UICommand = chooseAllCommand, Action = new UniversalAction(() => _drawingService.Shapes.SelectAll()), Active = true, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.InverseSelect, UICommand = chooseNoSelectCommand, Action = new UniversalAction(() => _drawingService.Shapes.SelectInvert()), Active = true, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Replace, UICommand = displaceCommand, Action = new UniversalAction(() => _drawingService.Shapes.Replace()), Active = true, IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Combine, UICommand = combineCommand, Action = new UniversalAction(() => _drawingService.Shapes.Combine()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Break, UICommand = unCombineCommand, Action = new UniversalAction(() => _drawingService.Shapes.Break()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.BreakFill, UICommand = unCombineFillCommand, Action = new UniversalAction(() => _drawingService.Shapes.BreakFill()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Group, UICommand = groupCommand, Action = new UniversalAction(() => _drawingService.Shapes.Group()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Ungroup, UICommand = unGroupCommand, Action = new UniversalAction(() => _drawingService.Shapes.Ungroup()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.VectorCombine, UICommand = vectorCombinationCommand, Action = new UniversalAction(() => _drawingService.Shapes.VectorCombine()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.MoveToNewLayer, UICommand = moveToNewLayerCommand, Action = new UniversalAction(() => _drawingService.Shapes.MoveToNewLayer()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Inverse, UICommand = inverseCommand, Action = new UniversalAction(obj => { if (obj is InverseSettingsModel model) _drawingService.Shapes.Reverse(model.IsInverse); }), Active = false, IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.HorizontalMirrorReflection, UICommand = horizontalMirrorReflectionCommand, Action = new UniversalAction(() => _drawingService.Shapes.HorizontalMirror()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.VerticalMirrorReflection, UICommand = verticalMirrorReflectionCommand, Action = new UniversalAction(() => _drawingService.Shapes.VerticalMirror()), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.MaterialCenter, UICommand = materialCenterCommand, Action = new UniversalAction(() => _drawingService.Shapes.SetCenter(0,0)), Active = false, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.ConvertToPointOrCircle, UICommand = convertToPointOrCircleCommand, Action = new UniversalAction(obj => { if (obj is ConvertToPointCircleSettingsModel model) { _drawingService.Shapes.ConvertToDot(new ConvertToDotSettingsDto { Gap = model.Gap, Diameter = model.Diameter, IsCircleType = model.SelectedShapeType == ShapeType.Circle, NeedPointAtCorner = model.NeedPointAtCornner, IncludedAngle = model.IncludedAngle }); } }), Active = true, IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.ConvertToExtendNode, UICommand = convertToExtendNodeCommand, AutoCancel = true, Action = new UniversalAction(() => _drawingService.Shapes.ConvertToCurve()), Active = true, IsDialogConfirmed = false },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.JumpPoint, UICommand = jumpPointCommand, Active = false, Action = new UniversalAction(obj => { if (obj is JumpSettingsModel model) { _drawingService.Shapes.SetJumpPoint(new JumpSettingsDto { JumpSize = model.JumpSize }); } }), IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Align, UICommand = alignCommand, Active = true, Action = new UniversalAction(obj => { if (obj is AlignSettingsModel model) { _drawingService.Shapes.Align(AlignPopupViewModel.ToDto(model)); } }), IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Distribution, UICommand = distributionCommand, Active = true, Action = new UniversalAction(obj => { if (obj is DistributionSettingsModel model) { _drawingService.Shapes.Distribute(DistributionPopupViewModel.ToDto(model)); } }), IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.ExtendHeadAndTail, UICommand = extendHeadAndTailCommand, Action = new UniversalAction(() => _drawingService.Shapes.ExtendHeadAndTail()), Active = true, IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.SkyWriting, UICommand = skyWritingCommand, Action = new UniversalAction(() => _drawingService.Shapes.SetSkyWriting(null)), Active = false, IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Partition, UICommand = partitionCommand, Action = new UniversalAction(obj => { if (obj is PartitionSettingsModel model) { _drawingService.Shapes.Partition(model.Length, model.Width, model.OverlapX, model.OverlapY); } }), Active = true, IsDialogConfirmed = true },
                new MenuToolCommand<EditMenuCommandType, bool, GraphicResult> { Command = EditMenuCommandType.Lock, UICommand = lockCommand, FuncResult = new  Func<GraphicResult>(() => _drawingService.Shapes.Lock()), Active = false, IsDialogConfirmed = false },
            };

            // 订阅相关事件以更新命令状态
            _eventBus.Subscribe<CommandCapabilityChangedEvent>(data =>
            {
                UpdateCommandStates(data);
            });

            WeakReferenceMessenger.Default.Register<string>(this, (r, m) => LockCommandStateChanged(m));
        }

        private void LockCommandStateChanged(string m)
        {
            if (m.Contains("锁定"))
            {
                LockHeaderName = "锁定";
            }
            else if (m.Contains("解锁"))
            {
                LockHeaderName = "解锁";
            }
        }

        private void UpdateCommandStates(CommandCapabilityChangedEvent eventData)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // 根据选择变化的事件数据，更新命令状态
                // 这里可以根据具体的事件数据结构来判断哪些命令应该被启用或禁用
                // 例如，如果 eventData 包含选中对象的类型和数量，可以根据这些信息来更新命令状态
                if (eventData != null && eventData.Capabilities != null)
                {
                    var cap = eventData.Capabilities;
                    CommandMap[EditMenuCommandType.Undo].Active = cap.CanUndo;
                    CommandMap[EditMenuCommandType.Redo].Active = cap.CanRedo;
                    CommandMap[EditMenuCommandType.Cut].Active = cap.CanCut;
                    CommandMap[EditMenuCommandType.Copy].Active = cap.CanCopy;
                    CommandMap[EditMenuCommandType.Paste].Active = cap.CanPaste;
                    CommandMap[EditMenuCommandType.Delete].Active = cap.CanDelete;
                    //CommandMap[EditMenuCommandType.SelectAll].Active = cap.CanSelectAll;
                    CommandMap[EditMenuCommandType.SelectAll].Active = true;
                    CommandMap[EditMenuCommandType.InverseSelect].Active = cap.CanInverseSelect;
                    CommandMap[EditMenuCommandType.Replace].Active = cap.CanReplace;
                    CommandMap[EditMenuCommandType.Combine].Active = cap.CanCombine;
                    CommandMap[EditMenuCommandType.Break].Active = cap.CanBreak;
                    CommandMap[EditMenuCommandType.BreakFill].Active = cap.CanBreakFill;
                    CommandMap[EditMenuCommandType.Group].Active = cap.CanGroup;
                    CommandMap[EditMenuCommandType.Ungroup].Active = cap.CanUngroup;
                    CommandMap[EditMenuCommandType.VectorCombine].Active = cap.CanVectorCombine;
                    CommandMap[EditMenuCommandType.MoveToNewLayer].Active = cap.CanMoveToNewLayer;
                    CommandMap[EditMenuCommandType.Inverse].Active = cap.CanInverse;
                    CommandMap[EditMenuCommandType.HorizontalMirrorReflection].Active = cap.CanHorizontalMirrorReflection;
                    CommandMap[EditMenuCommandType.VerticalMirrorReflection].Active = cap.CanVerticalMirrorReflection;
                    CommandMap[EditMenuCommandType.MaterialCenter].Active = cap.CanMaterialCenter;
                    CommandMap[EditMenuCommandType.ConvertToPointOrCircle].Active = cap.CanConvertToPointOrCircle;
                    CommandMap[EditMenuCommandType.JumpPoint].Active = cap.CanJumpPoint;
                    CommandMap[EditMenuCommandType.Align].Active = cap.CanAlign;
                    CommandMap[EditMenuCommandType.Distribution].Active = cap.CanDistribution;
                    CommandMap[EditMenuCommandType.ExtendHeadAndTail].Active = cap.CanExtendHeadAndTail;
                    CommandMap[EditMenuCommandType.SkyWriting].Active = cap.CanSkyWriting;
                    CommandMap[EditMenuCommandType.Partition].Active = cap.CanPartition;
                    CommandMap[EditMenuCommandType.Lock].Active = cap.CanLock;
                    CommandMap[EditMenuCommandType.ConvertToExtendNode].Active = cap.CanExtendNode;

                    if (!cap.IsLocked)
                    {
                        LockHeaderName = "锁定";
                    }
                    else
                    {
                        LockHeaderName = "解锁";
                    }

                }
                else
                {
                    // 没有选中任何对象时，禁用所有命令
                    foreach (var cmd in Commands)
                    {
                        if (cmd.Command != EditMenuCommandType.Paste)
                            cmd.Active = false;
                    }
                }

                //Debug.WriteLine($"收到SelectionChangedEvent");
                // 其他命令的状态更新逻辑...
                // 最后通知UI更新命令状态
                foreach (var cmd in CommandMap.Values)
                {
                    if (cmd.UICommand is RelayCommand<string> relayCommand)
                    {
                        relayCommand.NotifyCanExecuteChanged();
                    }
                    //Debug.WriteLine($"命令: {cmd.Command}, 可用: {cmd.Active}");
                }
            });
        }

        public bool CanExcute(string cmdName)
        {
            // 如果是 Delete 命令，且焦点在数值输入控件内，则禁用窗口级删除
            if (string.Equals(cmdName, "Delete", StringComparison.OrdinalIgnoreCase))
            {
                if (IsFocusInsideNumericInput())
                    return false;
            }

            // 根据业务逻辑返回 true/false
            bool canExcute = false;
            if (Enum.TryParse<EditMenuCommandType>(cmdName, true, out var menuChooseCommand))
            {
                MenuToolCommand<EditMenuCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(menuChooseCommand);
                canExcute = cmd.Active;
            }
            return canExcute;
        }

        private bool IsFocusInsideNumericInput()
        {
            var focused = Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (focused is NumericUpDownControl || focused is NumberDataExpressionTextBox)
                    return true;

                // try visual parent first
                DependencyObject parent = VisualTreeHelper.GetParent(focused);
                if (parent == null)
                    parent = LogicalTreeHelper.GetParent(focused);
                focused = parent;
            }
            return false;
        }

        public void TriggerCommand(string cmdName)
        {
            if (Enum.TryParse<EditMenuCommandType>(cmdName, true, out var menuChooseCommand))
            {
                MenuToolCommand<EditMenuCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(menuChooseCommand);
                if (!cmd.Active) return;
                // 在这里处理命令逻辑，例如发布事件或调用服务
                //Console.WriteLine($"执行命令: {cmd.Command}");

                // 命令触发需要对话框确认的处理逻辑
                if (cmd.IsDialogConfirmed)
                {
                    TriggerCommandNeedDialogConfirm(cmd);
                }
                else // 其他命令的处理逻辑
                {
                    if (cmdName == "Copy" || cmdName == "Cut")
                    {
                        cmd.Execute();
                        var result = cmd.FuncResult?.Invoke();

                        var pasteCmd = CommandMap[EditMenuCommandType.Paste];
                        pasteCmd.Active = result.IsSuccess;
                        if (pasteCmd.UICommand is RelayCommand<string> relayCommand)
                            relayCommand.NotifyCanExecuteChanged();

                        WeakReferenceMessenger.Default.Send("EditMenuCommandStateUpdate");
                    }
                    else if (cmdName == "Lock")
                    {
                        cmd.Execute();
                        var result = cmd.FuncResult?.Invoke();
                        // 切换锁定状态后，更新 Lock 命令的显示文本
                        if (result.IsSuccess)
                        {
                            LockHeaderName = LockHeaderName.Contains("锁定") ? "解锁" : "锁定";
                        }

                        WeakReferenceMessenger.Default.Send<string>(LockHeaderName);
                    }
                    else
                        cmd.Execute();
                }
            }
        }

        [RelayCommand]
        public void CommandExcute(string cmdName)
        {
            if (Enum.TryParse<EditMenuCommandType>(cmdName, true, out var menuChooseCommand))
            {
                MenuToolCommand<EditMenuCommandType, bool, GraphicResult> cmd = CommandMap.GetValueOrDefault(menuChooseCommand);
                if (!cmd.Active) return;
                // 在这里处理命令逻辑，例如发布事件或调用服务
                //Console.WriteLine($"执行命令: {cmd.Command}");

                // 命令触发需要对话框确认的处理逻辑
                if (cmd.IsDialogConfirmed)
                {
                    TriggerCommandNeedDialogConfirm(cmd);
                }
                else // 其他命令的处理逻辑
                {
                    if (cmdName == "Copy" || cmdName == "Cut")
                    {
                        cmd.Execute();
                        var result = cmd.FuncResult?.Invoke();
                        var pasteCmd = CommandMap[EditMenuCommandType.Paste];
                        pasteCmd.Active = result?.IsSuccess ?? false;
                        if (pasteCmd.UICommand is RelayCommand<string> relayCommand)
                            relayCommand.NotifyCanExecuteChanged();

                        WeakReferenceMessenger.Default.Send("EditMenuCommandStateUpdate");
                    }
                    else if (cmdName == "Lock")
                    {
                        cmd.Execute();
                        var result = cmd.FuncResult?.Invoke();
                        // 切换锁定状态后，更新 Lock 命令的显示文本
                        if (result.IsSuccess)
                        {
                            LockHeaderName = LockHeaderName.Contains("锁定") ? "解锁" : "锁定";
                        }

                        WeakReferenceMessenger.Default.Send<string>(LockHeaderName);
                    }
                    else
                        cmd.Execute();
                }
            }
        }

        private async Task TriggerCommandNeedDialogConfirm(MenuToolCommand<EditMenuCommandType, bool, GraphicResult> cmd)
        {
            if (!cmd.Active) return;

            if (_dialogActions.TryGetValue(cmd.Command, out var action))
            {
                var result = await action();
                if (result != null)
                    cmd.Execute(result);
            }
        }

        // 创建辅助方法简化调用
        private static async Task<TResult> ShowDialog<TViewModel, TResult>()
            where TViewModel : DialogViewModelBase<TResult>, new()
        {
            return await _dialogService.ShowDialogAsync<TViewModel, TResult>(vm =>
            {
                vm.ConfirmText = "确定";
                vm.CancelText = "取消";
            });
        }

        // 然后使用委托字典
        private Dictionary<EditMenuCommandType, Func<Task<object>>> _dialogActions = new()
        {
            [EditMenuCommandType.Inverse] = async () => await ShowDialog<InversePopupViewModel, InverseSettingsModel>(),
            [EditMenuCommandType.ConvertToPointOrCircle] = async () => await ShowDialog<ConvertToPointCirclePopupViewModel, ConvertToPointCircleSettingsModel>(),
            [EditMenuCommandType.Align] = async () => await ShowDialog<AlignPopupViewModel, AlignSettingsModel>(),
            [EditMenuCommandType.Distribution] = async () => await ShowDialog<DistributionPopupViewModel, DistributionSettingsModel>(),
            [EditMenuCommandType.ExtendHeadAndTail] = async () => await ShowDialog<ExtendHeadTailPopupViewModel, ExtendHeadTailSettingsModel>(),
            [EditMenuCommandType.JumpPoint] = async () => await ShowDialog<JumpPointPopupViewModel, JumpSettingsModel>(),
            [EditMenuCommandType.SkyWriting] = async () => await ShowDialog<SkyWritingPopupViewModel, SkyWritingSettingsModel>(),
            [EditMenuCommandType.Partition] = async () => await ShowDialog<PartitionPopupViewModel, PartitionSettingsModel>(),
        };
    }
}
