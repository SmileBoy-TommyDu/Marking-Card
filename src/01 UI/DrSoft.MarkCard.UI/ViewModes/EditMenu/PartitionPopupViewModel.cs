using CommunityToolkit.Mvvm.ComponentModel;
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
    public partial class PartitionPopupViewModel : DialogViewModelBase<PartitionSettingsModel>
    {
        [ObservableProperty] private float _width;
        [ObservableProperty] private float _length;
        [ObservableProperty] private float _overlapX;
        [ObservableProperty] private float _overlapY;
        public PartitionPopupViewModel()
        {
            Title = "依分区打断物件设置";
            WindowHeight = 280;
            Content = new PartitionPopupView() { DataContext = this };
        }
        protected override PartitionSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override PartitionSettingsModel? GetConfirmResult()
        {
            return new PartitionSettingsModel() 
            {
                Width = Width,
                Length = Length,
                OverlapX = OverlapX,
                OverlapY = OverlapY
            };
        }
    }
}
