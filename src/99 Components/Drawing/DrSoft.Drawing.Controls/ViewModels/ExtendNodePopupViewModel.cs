using CommunityToolkit.Mvvm.ComponentModel;

namespace DrSoft.Drawing.Controls.ViewModels;

/// <summary>
/// 延伸节点坐标输入对话框 ViewModel。
/// </summary>
public partial class ExtendNodePopupViewModel : DialogViewModelBase<ExtendNodeDialogResult>
{
    [ObservableProperty]
    private float _inputX;

    [ObservableProperty]
    private float _inputY;

    [ObservableProperty]
    private bool _isRelativeToPrevious;

    public ExtendNodePopupViewModel()
    {
        var view = new Views.ExtendNodePopupView
        {
            DataContext = this
        };
        Content = view;
    }

    protected internal override void OnPrepareForDialog()
    {
        InputX = 0;
        InputY = 0;
        IsRelativeToPrevious = true;
    }

    protected override ExtendNodeDialogResult? GetConfirmResult()
    {
        var result = new ExtendNodeDialogResult
        {
            X = InputX,
            Y = InputY,
            IsRelativeToPrevious = IsRelativeToPrevious,
            Confirmed = true
        };
        return result;
    }

    protected override ExtendNodeDialogResult? GetCancelResult()
    {
        var result = new ExtendNodeDialogResult
        {
            X = 0,
            Y = 0,
            IsRelativeToPrevious = false,
            Confirmed = false
        };
        return result;
    }
}
