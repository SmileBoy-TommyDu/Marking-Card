using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.Views.EditMenu;

namespace DrSoft.MarkCard.UI.ViewModes.EditMenu
{
    public partial class SkyWritingPopupViewModel : BaseParamViewModel<SkyWritingSettingsModel>
    {
        [ObservableProperty]
        private List<uint> _skyWritingModels = new List<uint> {0,1,2,3};

        [ObservableProperty]
        private bool _isLoading;

        /// <summary>
        /// 无参构造函数，用于 ShowDialog 泛型约束
        /// </summary>
        public SkyWritingPopupViewModel()
        {
            Title = "Sky Writing";
            WindowHeight = 298;
            Content = new SkyWritingPopupView();
        }

        /// <summary>
        /// 套用逻辑：如果选中对象是容器（群组/组合/填充），
        /// 需同时绑定到容器（显示用）和末级图形（加工用）
        /// </summary>
        protected override async Task ExecuteApplyAsync()
        {
            if (_service != null && RuntimeContext.Selections != null)
            {
                var leafIds = CollectLeafEntityIds();
                // 同时绑定到容器（显示用）和末级图形（加工用）
                var allIds = RuntimeContext.Selections.Union(leafIds).ToList();
                await _service.BindParametersAsync(
                    RuntimeContext.ActiveCanvasId,
                    allIds,
                    new List<ParameterBase> { Model }).ConfigureAwait(false);
            }
        }

        protected override SkyWritingSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override SkyWritingSettingsModel? GetConfirmResult()
        {
            base.ApplyAsync().ConfigureAwait(false);
            return null;
        }

        [RelayCommand]
        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                await base.LoadParameterAsync();
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
