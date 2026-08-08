using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.UserControls;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class TextParamViewModel: BaseParamViewModel<TextParameter>
    {
       
        [ObservableProperty]
        private ShareTextModel _shareTextModel;
        [ObservableProperty]
        private TextParameter _currentTextParameter = new();
        [ObservableProperty]
        private ObservableCollection<string> _fontFamilyList = new ObservableCollection<string>();
        [ObservableProperty]
        private ObservableCollection<string> _horizontalAlignList = new ObservableCollection<string>();

        public TextParamViewModel(ShareTextModel shareTextModel)
        {
            _shareTextModel = shareTextModel;
            // 订阅共享数据的变化
            _shareTextModel.PropertyChanged += OnSharedDataChanged;
            EventBus.Instance.Subscribe<ParaSaveEvent>(e => { if (e.ParaSaveType == ParaSaveType.Element && e.Trigger && e.TriggerTitle.Equals("文本")) _ = OnApplyAsync(); });

        }

        private void OnSharedDataChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ShareTextModel.Text):
                    CurrentTextParameter.Text = _shareTextModel.Text;
                    break;
                case nameof(ShareTextModel.CurrentFontFamily):
                    CurrentTextParameter.CurrentFontFamily = _shareTextModel.CurrentFontFamily;
                    break;
                case nameof(ShareTextModel.FontSize):
                    CurrentTextParameter.FontSize = _shareTextModel.FontSize;
                    break;
                case nameof(ShareTextModel.IsBold):
                    CurrentTextParameter.IsBold = _shareTextModel.IsBold;
                    break;
                case nameof(ShareTextModel.IsItalic):
                    CurrentTextParameter.IsItalic = _shareTextModel.IsItalic;
                    break;
                case nameof(ShareTextModel.IsUnderline):
                    CurrentTextParameter.IsUnderline = _shareTextModel.IsUnderline;
                    break;
                case nameof(ShareTextModel.IsVerticalLayout):
                    CurrentTextParameter.IsVerticalLayout = _shareTextModel.IsVerticalLayout;
                    break;
                case nameof(ShareTextModel.TextAlign):
                    CurrentTextParameter.TextAlign = _shareTextModel.TextAlign;
                    break;
                case nameof(ShareTextModel.LineHeight):
                    CurrentTextParameter.LineHeight = _shareTextModel.LineHeight;
                    break;
                case nameof(ShareTextModel.CharSpacing):
                    CurrentTextParameter.CharSpacing = _shareTextModel.CharSpacing;
                    break;
            }
        }

        protected override Task BeforeApplyAsync(TextParameter parameter)
        {
            var fontSettingsDto = new FontSettingsDto
            {
                FontFamily = CurrentTextParameter.CurrentFontFamily,
                FontSize = (float)CurrentTextParameter.FontSize,
                IsBold = CurrentTextParameter.IsBold,
                IsItalic = CurrentTextParameter.IsItalic,
                IsUnderline = CurrentTextParameter.IsUnderline,
                IsVerticalLayout = CurrentTextParameter.IsVerticalLayout,
                HorizontalAlign = (int)CurrentTextParameter.TextAlign,
                LineHeight = (float)CurrentTextParameter.LineHeight,
                CharacterSpacing = (float)CurrentTextParameter.CharSpacing,
                UpdatedFields = FontSettingsFields.All
            };

            _shareTextModel.Text = CurrentTextParameter.Text;
            _shareTextModel.CurrentFontFamily = CurrentTextParameter.CurrentFontFamily;
            _shareTextModel.FontSize = CurrentTextParameter.FontSize;
            _shareTextModel.IsBold = CurrentTextParameter.IsBold;
            _shareTextModel.IsItalic = CurrentTextParameter.IsItalic;
            _shareTextModel.IsUnderline = CurrentTextParameter.IsUnderline;
            _shareTextModel.IsVerticalLayout = CurrentTextParameter.IsVerticalLayout;
            _shareTextModel.TextAlign = (int)CurrentTextParameter.TextAlign;
            _shareTextModel.LineHeight = CurrentTextParameter.LineHeight;
            _shareTextModel.CharSpacing = CurrentTextParameter.CharSpacing;

            Model.Text = CurrentTextParameter.Text;
            Model.CurrentFontFamily = CurrentTextParameter.CurrentFontFamily;
            Model.FontSize = CurrentTextParameter.FontSize;
            Model.IsBold = CurrentTextParameter.IsBold;
            Model.IsItalic = CurrentTextParameter.IsItalic;
            Model.IsUnderline = CurrentTextParameter.IsUnderline;
            Model.IsVerticalLayout = CurrentTextParameter.IsVerticalLayout;
            Model.TextAlign = CurrentTextParameter.TextAlign;
            Model.LineHeight = CurrentTextParameter.LineHeight;
            Model.CharSpacing = CurrentTextParameter.CharSpacing;

            _drawingService?.Shapes.SetTextFont(fontSettingsDto, _shareTextModel.Text, FontSettingsFields.All);

            return Task.CompletedTask;
        }

        private IAsyncRelayCommand? _applyCommand;
        public new IAsyncRelayCommand ApplyCommand => _applyCommand ??= new AsyncRelayCommand(OnApplyAsync);

        private async Task OnApplyAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentTextParameter.Text))
            {
                MessageBox.Show("请输入文本内容！");
                return;
            }

            await BeforeApplyAsync(Model);

            var parameters = new List<ParameterBase> { Model };
            if (_service != null)
            {
                await _service.BindParametersAsync(RuntimeContext.ActiveCanvasId, RuntimeContext.Selections, parameters);
            }
            await AfterApplyAsync(Model);
        }
    }
}
