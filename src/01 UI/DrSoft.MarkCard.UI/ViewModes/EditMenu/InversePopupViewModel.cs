using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
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
    public partial class InversePopupViewModel : DialogViewModelBase<InverseSettingsModel>
    {
        /// <summary>
        /// 重入守卫：避免 IsInverse 与 IsClockwise 互相赋值时陷入无限循环。
        /// </summary>
        private bool _updatingFlag;
    
        [ObservableProperty] private bool _isInverse;
    
        [ObservableProperty] private bool _isClockwise = true; // 激光加工方向：true=顺时针，false=逆时针
    
        partial void OnIsInverseChanged(bool value)
        {
            if (_updatingFlag) return;
            _updatingFlag = true;
            try
            {
                // IsInverse=true ⇒ 反转 ⇒ IsClockwise 取反
                IsClockwise = !value;
            }
            finally { _updatingFlag = false; }
        }
    
        partial void OnIsClockwiseChanged(bool value)
        {
            if (_updatingFlag) return;
            _updatingFlag = true;
            try
            {
                // IsClockwise=false ⇒ 反转 ⇒ IsInverse 取反
                IsInverse = !value;
            }
            finally { _updatingFlag = false; }
        }
    
        public InversePopupViewModel()
        {
            Title = "激光雕刻反转设置";
            WindowHeight = 264;
            Content = new InversePopupView() { DataContext = this };
        }

        protected override InverseSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override void OnPrepareForDialog()
        {
            

            var selectShapes = DocumentContext.Instance.ActiveCanvas?.Selection;
            if (selectShapes != null && selectShapes.Count() == 1)
            {
                IsClockwise = (selectShapes.ToArray()[0] as DrawObject).IsClockwise;
            }
            IsInverse = false;
        }

        protected override InverseSettingsModel? GetConfirmResult()
        {
            var draw = Content;
            return new InverseSettingsModel
            {
                IsInverse = IsInverse
            };
        }
    }
}
