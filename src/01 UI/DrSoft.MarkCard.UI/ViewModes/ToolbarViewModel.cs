using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.ViewModes.EditMenu;
using DrSoft.MarkCard.UI.ViewModes.Tools;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class ToolbarViewModel : ObservableObject
    {
        // ── 所有组（按 Order 排序后展示）────────────────────────────────
        public ObservableCollection<ToolbarGroup> Groups { get; } = new();

        // ── 可见组（供工具栏实际渲染，始终保持和 Groups 同步）──────────
        public ObservableCollection<ToolbarGroup> VisibleGroups { get; } = new();

        public TextToolViewModel TextToolVM { get; }

        public EditMenuViewModel EditMenuVM { get; }
        public FileViewModel FileVM { get; }

        public EditPathNodesToolViewModel EditPathNodesToolVM { get; }

        public VectorToolViewModel VectorToolVM { get; }

        private readonly IEventBus _eventBus;

        public ToolbarViewModel(EditMenuViewModel editMenuVM, FileViewModel fileVM, TextToolViewModel textToolVM, EditPathNodesToolViewModel editPathNodesToolVM, VectorToolViewModel vectorToolVM)
        {
            _eventBus = EventBus.Instance;
            EditMenuVM = editMenuVM;
            FileVM = fileVM;
            TextToolVM = textToolVM;
            EditPathNodesToolVM = editPathNodesToolVM;
            VectorToolVM = vectorToolVM;
            BuildDefaultGroups();
            Groups.CollectionChanged += (_, _) => RefreshVisible();
            RefreshVisible();

            WeakReferenceMessenger.Default.Register<string>(this, (r, m) => EditMenuCommandStateChanged(m));

            TextToolVM.EnableTextToolButton(false);

            _eventBus.Subscribe<CanvasChangedEvent>(data =>
            {
                switch (data.ChangeType)
                {
                    case CanvasChangeType.SelectSharps:
                    case CanvasChangeType.TransformChanged:
                        var sCurData = data.Data as SelectedSharpsDto;
                        var textDto = sCurData?.EditingObject as DrawTextDto;
                        var hasText = textDto != null;
                        TextToolVM.EnableTextToolButton(hasText);
                        if (hasText)
                        {
                            if (textDto != null)
                            {
                                TextToolVM.UpdateTextFontSettings(textDto);
                            }
                        }
                        break;

                }
            });

            _eventBus.Subscribe<CommandCapabilityChangedEvent>(data =>
            {
                UpdateToolBarCommandStates(data);
            });
        }

        private void UpdateToolBarCommandStates(CommandCapabilityChangedEvent data)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (data == null) return;

                //Debug.WriteLine($"收到CanvasChangedEvent");
                foreach (var g in Groups)
                {
                    foreach (var cmd in g.Buttons)
                    {
                        if (cmd.Content is Button)
                        {
                            var button = cmd.Content as Button;

                            if (button.Command is RelayCommand<string> relayCmd)
                                relayCmd.NotifyCanExecuteChanged();
                        }
                    }
                }

                var hasText = data.Capabilities.CanText
                    || data.Capabilities.SelectedShapeData.Any(shape => shape.Type == ShapeType.Text);
                TextToolVM.EnableTextToolButton(hasText);
            });
        }

        private void EditMenuCommandStateChanged(string m)
        {
            switch (m)
            {
                case "EditMenuCommandStateUpdate":
                    foreach (var g in Groups)
                    {
                        foreach (var cmd in g.Buttons)
                        {
                            if (cmd.Content is Button)
                            {
                                var button = cmd.Content as Button;
                                if (button.Command is RelayCommand<string> relayCmd)
                                    relayCmd.NotifyCanExecuteChanged();
                            }
                        }
                    }
                    break;
                default:
                    break;
            }
            ;
        }

        // ── 切换组可见性 ─────────────────────────────────────────────────
        [RelayCommand]
        public void ToggleGroupVisibility(ToolbarGroup group)
        {
            group.IsVisible = !group.IsVisible;
            RefreshVisible();
        }

        // ── 拖拽重排：将 from 插入到 to 之前 ────────────────────────────
        public void ReorderGroup(ToolbarGroup from, ToolbarGroup to)
        {
            if (from == to) return;

            int fromIdx = Groups.IndexOf(from);
            int toIdx = Groups.IndexOf(to);
            if (fromIdx < 0 || toIdx < 0) return;

            Groups.Move(fromIdx, toIdx);

            // 更新 Order 属性，和列表位置保持一致
            for (int i = 0; i < Groups.Count; i++)
                Groups[i].Order = i;

            RefreshVisible();
        }

        // ── 让 from 排在 to 之后 ─────────────────────────────────────────
        public void ReorderGroupAfter(ToolbarGroup from, ToolbarGroup to)
        {
            if (from == to) return;
            int fromIdx = Groups.IndexOf(from);
            int toIdx = Groups.IndexOf(to);
            if (fromIdx < 0 || toIdx < 0) return;

            int targetIdx = toIdx > fromIdx ? toIdx : toIdx + 1;
            if (targetIdx >= Groups.Count) targetIdx = Groups.Count - 1;

            Groups.Move(fromIdx, targetIdx);
            for (int i = 0; i < Groups.Count; i++)
                Groups[i].Order = i;

            RefreshVisible();
        }

        // ── 同步可见列表 ─────────────────────────────────────────────────
        private void RefreshVisible()
        {
            VisibleGroups.Clear();
            foreach (var g in Groups)
            {
                g.PropertyChanged -= OnGroupPropertyChanged;
                g.PropertyChanged += OnGroupPropertyChanged;
                if (g.IsVisible)
                    VisibleGroups.Add(g);
            }
        }

        private void OnGroupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToolbarGroup.IsVisible))
                RefreshVisible();
        }

        // ── 初始数据 ─────────────────────────────────────────────────────
        private void BuildDefaultGroups()
        {
            #region 标准工具
            var standardToolGroup = new ToolbarGroup { Title = "标准工具栏", Order = 0 };
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "新建",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/NewEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/NewDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(FileMenuCommandExcecute),
                    CommandParameter = "新建",
                    ToolTip = "新建",
                    IsEnabled = true,
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "打开",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/OpenEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/OpenDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(FileMenuCommandExcecute),
                    CommandParameter = "打开",
                    ToolTip = "打开",
                    IsEnabled = true,
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "保存",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/SaveEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/SaveDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(FileMenuCommandExcecute),
                    CommandParameter = "保存",
                    ToolTip = "保存",
                    IsEnabled = true
                }
            });

            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });

            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "导入DXF",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/ImportDxfEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/ImportDxfDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(FileMenuCommandExcecute),
                    CommandParameter = "导入DXF",
                    ToolTip = "导入DXF",
                    IsEnabled = true
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "导出DXF",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/ExportDxfEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/ExportDxfDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(FileMenuCommandExcecute),
                    CommandParameter = "导出DXF",
                    ToolTip = "导出DXF",
                    IsEnabled = true
                }
            });

            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "撤销",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/UnDoEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/UnDoDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Undo",
                    ToolTip = "撤销"
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "重做",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/ReDoEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/ReDoDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Redo",
                    ToolTip = "重做"
                }
            });

            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });

            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "剪切",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/CutEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/CutDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Cut",
                    ToolTip = "剪切"
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "复制",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/CopyEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/CopyDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Copy",
                    ToolTip = "复制"
                }
            });
            standardToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "粘贴",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/File/PasteEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/File/PasteDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Paste",
                    ToolTip = "粘贴"
                }
            });
            #endregion

            #region 文本工具
            var textToolGroup = new ToolbarGroup { Title = "文本工具栏", Order = 1 };

            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "字体",
                Content = TextToolVM.FontFlamilyComBox
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "字号",
                Content = TextToolVM.FontSizeNumericUpDownControl
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "倾斜",
                Content = TextToolVM.ItalicButton
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "加粗",
                Content = TextToolVM.BoldButton
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "下划线",
                Content = TextToolVM.UnderlineButton
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "水平排列",
                Content = TextToolVM.HorizontalAlignButton
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "垂直排列",
                Content = TextToolVM.VerticalAlignButton
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "对齐",
                Content = TextToolVM.AlignButton
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "行距",
                Content = TextToolVM.LineHeightNumericUpDownControl
            });
            textToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "字距",
                Content = TextToolVM.CharacterSpacingNumericUpDownControl
            });
            #endregion

            #region 向量工具
            var vectorToolGroup = new ToolbarGroup { Title = "向量工具栏", Order = 2 };
            vectorToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "联集",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Vector/UnionEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Vector/UnionDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(VectorCommandExcecute, VectorCommandCanExcecute),
                    CommandParameter = "Union",
                    ToolTip = "联集",
                }
            });
            vectorToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "交集",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Vector/IntersectEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Vector/IntersectDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(VectorCommandExcecute, VectorCommandCanExcecute),
                    CommandParameter = "Intersect",
                    ToolTip = "交集",
                }
            });
            vectorToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "修剪",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Vector/TrimEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Vector/TrimDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(VectorCommandExcecute, VectorCommandCanExcecute),
                    CommandParameter = "Trim",
                    ToolTip = "修剪",
                }
            });
            vectorToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "主物件保留",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Vector/KeepMainEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Vector/KeepMainDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(VectorCommandExcecute, VectorCommandCanExcecute),
                    CommandParameter = "KeepMain",
                    ToolTip = "主物件保留",
                }
            });
            BuildMap(vectorToolGroup);
            #endregion

            #region 曲线工具
            var curveToolGroup = new ToolbarGroup { Title = "曲线工具栏", Order = 3 };
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "扩展节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/ConvertToExtendNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/ConvertToExtendNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "ConvertToExtendNode",
                    ToolTip = "扩展节点"
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "编辑节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/EditNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/EditNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Edit",
                    ToolTip = "编辑节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "新增节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/AddNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/AddNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Add",
                    ToolTip = "新增节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "删除节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/DeleteNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/DeleteNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Delete",
                    ToolTip = "删除节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分离节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/SeparateNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/SeparateNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Separate",
                    ToolTip = "分离节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "移动节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/MoveNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/MoveNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Move",
                    ToolTip = "移动节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "延伸节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/ExtendNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/ExtendNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Extend",
                    ToolTip = "延伸节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "连接节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/ConnectNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/ConnectNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Connect",
                    ToolTip = "连接节点",
                }
            });
            curveToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "框选节点",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Curve/SelectNodeEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Curve/SelectNodeDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditPathNodeCommandExcecute, EditPathNodeCommandCanExcecute),
                    CommandParameter = "Select",
                    ToolTip = "框选节点",
                }
            });
            BuildMap(curveToolGroup);

            // 注入回调：Active 变更后刷新 curveToolGroup 所有按钮的 CanExecute，使 IsEnabled 同步更新
            EditPathNodesToolVM.RefreshUICommands = () =>
            {
                foreach (var btn in curveToolGroup.Buttons)
                {
                    if (btn.Content is Button button && button.Command is RelayCommand<string> relay)
                        relay.NotifyCanExecuteChanged();
                }
            };
            #endregion

            #region 编辑工具
            var editToolGroup = new ToolbarGroup { Title = "编辑工具栏", Order = 4 };
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "组合对象",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/CombineEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/CombineDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Combine",
                    ToolTip = "组合"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "打散对象",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/UnCombineEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/UnCombineDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Break",
                    ToolTip = "打散"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "群组",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/GroupEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/GroupDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Group",
                    ToolTip = "群组"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "解散群组",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/UnGroupEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/UnGroupDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "Ungroup",
                    ToolTip = "解散群组"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
       
            //editToolGroup.Buttons.Add(new Models.ToolButton
            //{
            //    Tooltip = "分割线",
            //    Content = new Separator
            //    {
            //        Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
            //        Width = 1,
            //        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
            //    }
            //});
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "水平镜像",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/HorizontalMirrorEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/HorizontalMirrorDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "HorizontalMirrorReflection",
                    ToolTip = "水平镜像"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "垂直镜像",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/VerticalMirrorEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/VerticalMirrorDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "VerticalMirrorReflection",
                    ToolTip = "垂直镜像"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "物件置中",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/MaterialCenterEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/MaterialCenterDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "MaterialCenter",
                    ToolTip = "物件置中"
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "分割线",
                Content = new Separator
                {
                    Style = (Style)Application.Current.FindResource(ToolBar.SeparatorStyleKey),  // 注意类型转换,
                    Width = 1,
                    Background = new SolidColorBrush(Color.FromArgb(0xFF, 0xCC, 0xCC, 0xCC))
                }
            });
            editToolGroup.Buttons.Add(new Models.ToolButton
            {
                Tooltip = "转点圆",
                Content = new StateButton
                {
                    NormalIcon = new BitmapImage(new Uri("/Resource/image/Edit/ConvertToPointOrCircleEnable.png", UriKind.Relative)),
                    DisabledIcon = new BitmapImage(new Uri("/Resource/image/Edit/ConvertToPointOrCircleDisable.png", UriKind.Relative)),
                    Command = new RelayCommand<string>(EditMenuCommandExcecute, EditMenuCommandCanExcecute),
                    CommandParameter = "ConvertToPointOrCircle",
                    ToolTip = "转点圆"
                }
            });

            #endregion

            Groups.Add(standardToolGroup);
            Groups.Add(textToolGroup);
            Groups.Add(vectorToolGroup);
            Groups.Add(curveToolGroup);
            Groups.Add(editToolGroup);
        }

        private void BuildMap(ToolbarGroup group)
        {
            if (group == null) return;

            if (group.Buttons != null && group.Buttons.Count > 0)
            {
                foreach (var btn in group.Buttons)
                {
                    if (btn.Content is StateButton stateButton)
                    {
                        string commandParam = stateButton.CommandParameter as string;

                        if (!string.IsNullOrEmpty(commandParam))
                        {
                            TryBindIsChecked<EditNodeCommandType>(stateButton, commandParam, EditPathNodesToolVM.CommandMap);
                            TryBindIsChecked<VectorCommandType>(stateButton, commandParam, VectorToolVM.CommandMap);
                        }
                    }
                }
            }
        }

        private void TryBindIsChecked<TEnum>(StateButton stateButton, string commandParam, Dictionary<TEnum, MenuToolCommand<TEnum, bool, GraphicResult>> commandMap) where TEnum : struct, Enum
        {
            if (Enum.TryParse<TEnum>(commandParam, true, out var enumValue) &&
                commandMap.TryGetValue(enumValue, out var cmd))
            {
                stateButton.SetBinding(StateButton.IsCheckedProperty,
                    new Binding(nameof(cmd.IsChecked))
                    {
                        Source = cmd,                           // 数据源
                        Mode = BindingMode.OneWay               // 单向绑定，命令状态改变时自动更新按钮可用性
                    });
            }
        }

        private async void FileMenuCommandExcecute(string parameter)
        {
            if (FileVM == null) return;
            switch (parameter)
            {
                case "新建":
                    FileVM.OnNewFile();
                    break;
                case "打开":
                    FileVM.OnOpenFile();
                    break;
                case "保存":
                    FileVM.OnSaveFile();
                    break;
                case "导入DXF":
                    await FileVM.OnImportDxf();
                    break;
                case "导出DXF":
                    await FileVM.OnExportDxf();
                    break;
                default:
                    break;
            }
            Console.WriteLine($"执行了 {parameter}");
        }

        private void EditMenuCommandExcecute(string parameter)
        {
            if (EditMenuVM == null) return;
            EditMenuVM.CommandExcute(parameter);
            Console.WriteLine($"执行了 {parameter}");
        }

        private bool EditMenuCommandCanExcecute(string parameter)
        {
            if (EditMenuVM == null) return false;
            //IsStandardSeparatorEnabled = EditMenuVM.CanExcute(parameter);
            return EditMenuVM.CanExcute(parameter);
        }

        private void EditPathNodeCommandExcecute(string parameter)
        {
            if (EditPathNodesToolVM == null) return;
            EditPathNodesToolVM.CommandExcute(parameter);
            Console.WriteLine($"执行了 {parameter}");
        }

        private bool EditPathNodeCommandCanExcecute(string parameter)
        {
            if (EditPathNodesToolVM == null) return false;
            return EditPathNodesToolVM.CanExcute(parameter);
        }

        private void VectorCommandExcecute(string parameter)
        {
            if (VectorToolVM == null) return;
            VectorToolVM.CommandExcute(parameter);
            Console.WriteLine($"执行了 {parameter}");
        }

        private bool VectorCommandCanExcecute(string parameter)
        {
            if (VectorToolVM == null) return false;
            return VectorToolVM.CanExcute(parameter);
        }

    }

}
