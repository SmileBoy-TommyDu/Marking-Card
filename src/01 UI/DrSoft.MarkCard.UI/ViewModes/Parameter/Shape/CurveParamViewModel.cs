using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class CurveParamViewModel : BaseParamViewModel<CurveParameter>
    {
        private readonly IDialogService _dialogService;
        private readonly DashSettingsViewModel _dashSettingsViewModel;

        [ObservableProperty]
        private bool _isClosedPath = false;

        public CurveParamViewModel(IDialogService dialogService, DashSettingsViewModel dashSettingsViewModel)
        {
            _dialogService = dialogService;
            _dashSettingsViewModel = dashSettingsViewModel;
            EventBus.Instance.Subscribe<CanvasChangedEvent>(data =>
            {
                if (data.ChangeType == CanvasChangeType.Command)
                    _ = LoadParameterAsync();
            });
        }

        /// <summary>
        /// 从 Model 加载初始状态（如从已有参数恢复）
        /// </summary>
        /// 

        protected override Task ExecuteApplyAsync()
        {
            bool wasClosedPath = Model.IsClosedPath;
            Model.IsClosedPath = IsClosedPath;

            if (!wasClosedPath && IsClosedPath)
            {
                _drawingService.Shapes.ClosePath();
            }

            return base.ExecuteApplyAsync();
        }

        public override Task<CurveParameter> LoadParameterAsync()
        {
            var result = _drawingService.Shapes.GetSelections();
            if (result.IsSuccess && result.Value != null && result.Value.Count > 0)
            {
                var shapeData = result.Value[0];

                if (shapeData is IClosable shape)
                {
                    IsClosedPath = Model.IsClosedPath = shape.IsClosed;
                    OnPropertyChanged(nameof(IsClosedPath));
                }
            }

            return Task.FromResult(Model);
        }


        [RelayCommand]
        private async Task ShowDialog()
        {
            var settings = await _dialogService.ShowDialogAsync<DashSettingsViewModel, DashSettingParameter>(vm =>
            {
                vm.Title = "虚线设定";
                vm.ConfirmText = "确定";
                vm.CancelText = "取消";
                vm.WindowHeight = 600;
            });

            if (settings != null)
            {
                _dashSettingsViewModel.ApplyCommand.Execute(settings);
            }
        }

        //public override async Task<CurveParameter> LoadParameterAsync()
        //{
        //    var result = await base.LoadParameterAsync();

        //    // 从 Model 初始化组数和组号
        //    SelectedGroupCount = Model.GroupCount;
        //    EnsureDashGroupsCount(Model.GroupCount);

        //    if (Model.SelectedGroupIndex >= 0 && Model.SelectedGroupIndex < Model.GroupCount)
        //    {
        //        SelectedGroupIndex = Model.SelectedGroupIndex;
        //    }

        //    _previousGroupIndex = SelectedGroupIndex;

        //    _isSwitchingGroup = true;
        //    LoadGroupData(SelectedGroupIndex);
        //    _isSwitchingGroup = false;

        //    return result;
        //}
    }
}
