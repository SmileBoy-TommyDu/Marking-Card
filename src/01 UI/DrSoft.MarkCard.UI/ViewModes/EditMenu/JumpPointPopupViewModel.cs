using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.Service;
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
    public partial class JumpPointPopupViewModel : DialogViewModelBase<JumpSettingsModel>
    {
        [ObservableProperty]
        private double _jumpSize = 0;

        partial void OnJumpSizeChanged(double value)
        {
            if (value < 0)
                JumpSize = 0;
        }

        private readonly IDrawingService _drawingService;

        public JumpPointPopupViewModel()
        {
            Title = "跳点设置";
            WindowHeight = 260;
            Content = new JumpPointPopupView() { DataContext = this };
        }

        protected override void OnPrepareForDialog() {

            JumpSize = 0;
        }

        public JumpPointPopupViewModel(IDrawingService drawingService)
        {
            Title = "跳点设置";
            WindowHeight = 260;
            _drawingService = drawingService;
            Content = new JumpPointPopupView() { DataContext = this };
        }
        protected override JumpSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override JumpSettingsModel? GetConfirmResult()
        {
            return new JumpSettingsModel() { JumpSize = JumpSize };
        }
    }
}
