using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.Views.EditMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.UI.ViewModes.EditMenu
{
    public partial class ExtendHeadTailPopupViewModel : BaseParamViewModel<ExtendHeadTailSettingsModel>
    {
        [ObservableProperty]
        private bool _isLoading;
        public ExtendHeadTailPopupViewModel()
        {
            Title = "头尾点延伸设置";
            WindowHeight = 260;
            Content = new ExtendHeadTailPopupView();
        }

        protected override ExtendHeadTailSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override ExtendHeadTailSettingsModel? GetConfirmResult()
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
