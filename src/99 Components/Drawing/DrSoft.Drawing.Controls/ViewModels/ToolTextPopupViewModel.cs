using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.Popup;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace DrSoft.Drawing.Controls.ViewModels
{
    public partial class ToolTextPopupViewModel : DialogViewModelBase<string>
    {
        [ObservableProperty] private string _inputText = "";

        public TextModel Result { get; set; }

        // 构造时将自定义的 View（UserControl）赋值给 Content
        public ToolTextPopupViewModel()
        {
            Content = new ToolTextPopupView() { DataContext = this };
        }

        protected override string? GetConfirmResult()
        {
            string result = InputText;
            if (string.IsNullOrEmpty(result))
            {
                return null;
            }
            return result;
        }

        protected override string? GetCancelResult()
        {
            return null;
        }
    }
}
