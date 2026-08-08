using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.MarkCard.UI.Views.EditMenu;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.UI.ViewModes.EditMenu
{
    public partial class ConvertToPointCirclePopupViewModel : DialogViewModelBase<ConvertToPointCircleSettingsModel>
    {
        [ObservableProperty] private List<string> _shapeTypeList = new List<string>() { "点", "圆" };
        [ObservableProperty] private string _shapeTypeSelected = "点";

        partial void OnShapeTypeSelectedChanged(string value)
        {
            IsDiameterEnabled = value == "圆";
        }

        [ObservableProperty] private bool _isDiameterEnabled = false;
        #region 间距

        public float Gap
        {
            get => _gap;
            set
            {
                _gap = Math.Max(0.01f, value);
                OnPropertyChanged();
            }
        }
       

        private float _gap = 0.01f;
        #endregion
        #region 直径
        public float Diameter
        {
            get => _diameter;
            set
            {
                _diameter = Math.Max(0.01f, value);
                OnPropertyChanged();
            }
        }
        

     
        private float _diameter = 0.01f;
        #endregion
        [ObservableProperty] private bool _needPointAtCornner = true;
        #region 夹角
        public float IncludedAngle
        {
            get => _includedAngle;
            set
            {
                _includedAngle = Math.Clamp(value, 0f, 180f);
                OnPropertyChanged();
            }
        }
   

        
        private float _includedAngle =  0;
        #endregion
      

        public ConvertToPointCirclePopupViewModel()
        {
            Title = "转成点/圆设置";
            WindowHeight = 280;
            Content = new ConvertToPointCirclePopupView() { DataContext = this };
        }

       

        protected override ConvertToPointCircleSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override ConvertToPointCircleSettingsModel? GetConfirmResult()
        {
         
            ConvertToPointCircleSettingsModel model = new ConvertToPointCircleSettingsModel()
            {
                SelectedShapeType = ShapeTypeSelected == "点" ? ShapeType.Point : ShapeType.Circle,
                Gap = _gap,
                Diameter = _diameter,
                NeedPointAtCornner = NeedPointAtCornner,
                IncludedAngle = _includedAngle
            };
            return model;
        }
    }
}
