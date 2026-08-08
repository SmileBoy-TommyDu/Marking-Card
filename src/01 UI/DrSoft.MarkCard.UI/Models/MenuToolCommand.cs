using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.DTO;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.ViewModes.EditMenu;
using DrSoft.MarkCard.UI.Views.EditMenu;
using System.ComponentModel;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.Models
{
    public class MenuToolCommand<T, TA, TR> : INotifyPropertyChanged
    {
        public T Command { get; set; }
        public ICommand UICommand { get; set; }
        public UniversalAction? Action { get; set; }

        public Action<TA>? ParmAction { get; set; }

        public Func<TR> FuncResult { get; set; }

        public bool IsDialogConfirmed { get; set; } // 用于指示是否需要显示确认对话框，例如删除操作可能需要确认
        public bool Active { get; set; }
    
        /// <summary>
        /// 默认取消画布绘制
        /// </summary>
        public bool AutoCancel { get; set; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        } // 用于指示当前命令是否处于选中状态，例如选择工具可能需要显示为选中

        public void Execute(object? parameter = null)
        {
            if (!Active) return;

            if (AutoCancel)
            {
                var ctx = DocumentContext.Instance;
                if (ctx.ActiveTool?.ToolType != ToolType.Select)
                {
                    ctx.ActiveTool.OnMouseRightDown();
                    ctx.ActiveTool = ctx.SelectTool;
                }
            }

            Action?.Invoke(parameter);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class RedoUndoFlagResluts
    {
        public bool UndoFlag { get; set; }
        public bool RedoFlag { get; set; }
    }

    public enum EditMenuCommandType
    {
        None,
        Undo,
        Redo,
        Cut,
        Copy,
        Paste,
        Delete,
        SelectAll,
        InverseSelect,
        Replace,
        Combine,
        Break,
        BreakFill,
        Group,
        Ungroup,
        VectorCombine,
        MoveToNewLayer,
        [RequiresConfirmDialog(typeof(InversePopupViewModel), typeof(InverseSettingsModel))]
        Inverse,
        HorizontalMirrorReflection,
        VerticalMirrorReflection,
        MaterialCenter,
        [RequiresConfirmDialog(typeof(ConvertToPointCirclePopupViewModel), typeof(ConvertToPointCircleSettingsModel))]
        ConvertToPointOrCircle,
        ConvertToExtendNode,
        [RequiresConfirmDialog(typeof(JumpPointPopupViewModel), typeof(JumpSettingsModel))]
        JumpPoint,
        [RequiresConfirmDialog(typeof(AlignPopupViewModel), typeof(AlignSettingsModel))]
        Align,
        [RequiresConfirmDialog(typeof(DistributionPopupViewModel), typeof(DistributionSettingsModel))]
        Distribution,
        [RequiresConfirmDialog(typeof(ExtendHeadTailPopupViewModel), typeof(ExtendHeadTailSettingsModel))]
        ExtendHeadAndTail,
        [RequiresConfirmDialog(typeof(SkyWritingPopupViewModel), typeof(SkyWritingSettingsModel))]
        SkyWriting,
        [RequiresConfirmDialog(typeof(PartitionPopupViewModel), typeof(PartitionSettingsModel))]
        Partition,
        Lock
    }

    /// <summary>
    /// 编辑节点工具的命令类型枚举，包含编辑、添加、删除、分离、移动、延伸、连接和选择等操作
    /// </summary>
    public enum EditNodeCommandType
    {
        None,
        Edit,
        Add,
        Delete,
        Separate,
        Move,
        Extend,
        Connect,
        Select
    }

    /// <summary>
    /// 向量工具的命令类型枚举，包含联合、交集、修剪和保留主图形等操作
    /// </summary>
    public enum VectorCommandType
    {
        None,
        Union,
        Intersect,
        Trim,
        KeepMain
    }

    // 自定义特性
    [AttributeUsage(AttributeTargets.Field)]
    public class RequiresConfirmDialogAttribute : Attribute
    {
        public Type ViewModelType { get; }
        public Type SettingsModelType { get; }

        public RequiresConfirmDialogAttribute(Type viewModelType, Type settingsModelType)
        {
            ViewModelType = viewModelType;
            SettingsModelType = settingsModelType;
        }
    }

    public class UniversalAction
    {
        private readonly Action<object?> _delegate;

        // 构造函数：接受带参数的委托
        public UniversalAction(Action<object?> action) => _delegate = action;

        // 构造函数：接受不带参数的委托，内部包装一层并忽略参数
        public UniversalAction(Action action) => _delegate = _ => action();

        // 无参触发此转换
        public static implicit operator UniversalAction(Action action)
            => new UniversalAction(action);

        // 有参触发此转换
        public static implicit operator UniversalAction(Action<object?> action)
            => new UniversalAction(action);

        // 执行方法
        public void Invoke(object? parameter = null) => _delegate(parameter);
    }
}
