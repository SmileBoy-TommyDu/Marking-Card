using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Mapping;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Event.Tool;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Service;
using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.ViewModes.Parameter;
using DrSoft.MarkCard.UI.Views.Tool;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.VisualStyles;
using System.Windows.Media.Imaging;
using static System.Net.Mime.MediaTypeNames;

namespace DrSoft.MarkCard.UI.ViewModes.Tools
{
    public class TextToolViewModel
    {
        //public FontSettings CurrentFontSettings { get; set; } = new FontSettings();

        private readonly IEventBus _eventBus;

        private readonly IShapeService _shapeService;
        private readonly TextParamViewModel _shapeParamViewModel;

        public ComboBoxUserControl FontFlamilyComBox { get; set; }

        public TextFontNumericUpDownControl FontSizeNumericUpDownControl { get; set; }

        public TextFontNumericUpDownControl LineHeightNumericUpDownControl { get; set; }
        public TextFontNumericUpDownControl CharacterSpacingNumericUpDownControl { get; set; }

        public CommonButton ItalicButton { get; set; } = new CommonButton("斜体", "/Resource/image/Fonts/ItalicDisable.png", "/Resource/image/Fonts/ItalicEnable.png");

        public CommonButton BoldButton { get; set; } = new CommonButton("加粗", "/Resource/image/Fonts/BoldDisable.png", "/Resource/image/Fonts/BoldEnable.png");
        public CommonButton UnderlineButton { get; set; } = new CommonButton("下划线", "/Resource/image/Fonts/UnderlineDisable.png", "/Resource/image/Fonts/UnderlineEnable.png");
        public CommonButton HorizontalAlignButton { get; set; } = new CommonButton("水平排列", "/Resource/image/Fonts/HorizontalAlignmentDisable.png", "/Resource/image/Fonts/HorizontalAlignmentEnable.png");
        public CommonButton VerticalAlignButton { get; set; } = new CommonButton("垂直排列", "/Resource/image/Fonts/VerticalAlignmentDisable.png", "/Resource/image/Fonts/VerticalAlignmentEnable.png");

        public TextAlignDropDown AlignButton { get; set; } = new TextAlignDropDown();

        // 标志位：防止从参数界面更新时循环触发
        private bool _isUpdatingFromCanvas = false;

        private ShareTextModel _shareTextModel;

        public TextToolViewModel(IShapeService shapeService, TextParamViewModel shapeParamViewModel, ShareTextModel shareTextModel)
        {
            try
            {
                _eventBus = EventBus.Instance;

                _shapeService = shapeService;
                _shapeParamViewModel = shapeParamViewModel;

                _shareTextModel = shareTextModel;

                //订阅共享数据的变化
                _shareTextModel.PropertyChanged += OnSharedDataChanged;

                FontFlamilyComBox = new ComboBoxUserControl();

                FontFlamilyComBox.ComboBox.SelectedIndex = 0;
                FontFlamilyComBox.ComboBox.DropDownOpened += (s, e) =>
                {
                    _isUpdatingFromCanvas = false;
                };
                FontFlamilyComBox.ComboBox.SelectionChanged += FontFlamilyComBox_SelectionChanged;

                foreach (var item in FontFlamilyComBox.ComboBox.Items)
                {
                    string temp = item.ToString();
                    if (!string.IsNullOrEmpty(temp) && temp.Contains(":"))
                    {
                        string itemStr = temp.Split(":").Last().Trim().ToString();
                        _shapeParamViewModel.FontFamilyList.Add(itemStr);
                    }
                }
                _shapeParamViewModel.HorizontalAlignList = new ObservableCollection<string> { HorizontalAlign.Left.ToString(), HorizontalAlign.Center.ToString(), HorizontalAlign.Right.ToString() };

                FontSizeNumericUpDownControl = new TextFontNumericUpDownControl("字号");
                FontSizeNumericUpDownControl.NumericUpDownBtn.ValueTextBoxLostFocus += FontSizeNumericUpDown_LostFocus;

                LineHeightNumericUpDownControl = new TextFontNumericUpDownControl("行距");
                LineHeightNumericUpDownControl.NumericUpDownBtn.ValueTextBoxLostFocus += LineHeightNumericUpDown_LostFocus;

                CharacterSpacingNumericUpDownControl = new TextFontNumericUpDownControl("字距");
                CharacterSpacingNumericUpDownControl.NumericUpDownBtn.ValueTextBoxLostFocus += CharacterSpacingNumericUpDown_LostFocus;
                //接受UI的Button信息，更新CurrentRenderOptions的属性
                _eventBus.Subscribe<ToolButtonClickedEvent>(e =>
                {
                    _isUpdatingFromCanvas = false;
                    int requestedTextAlignment = AlignButton.TextAlignment;
                    var selectedText = DocumentContext.Instance.ActiveCanvas?.Selection
                        ?.OfType<DrawText>()
                        .FirstOrDefault();
                    if (selectedText != null)
                    {
                        SyncShareModelFromDrawTextLayout(_shareTextModel, selectedText);
                    }
                    
                    
                    var updatedFields = FontSettingsFields.All;

                    if (e.ToolTip == "斜体")
                    {
                        _shareTextModel.IsItalic = e.IsChecked;
                        updatedFields = FontSettingsFields.IsItalic;
                    }
                    else if (e.ToolTip == "加粗")
                    {
                        _shareTextModel.IsBold = e.IsChecked;
                        updatedFields = FontSettingsFields.IsBold;
                    }
                    else if (e.ToolTip == "下划线")
                    {
                        _shareTextModel.IsUnderline = e.IsChecked;
                        updatedFields = FontSettingsFields.IsUnderline;
                    }
                    else if (e.ToolTip == "水平排列")
                    {
                        _shareTextModel.IsVerticalLayout = false;
                        updatedFields = FontSettingsFields.IsVerticalLayout;
                    }
                    else if (e.ToolTip == "垂直排列")
                    {
                        _shareTextModel.IsVerticalLayout = true;
                        updatedFields = FontSettingsFields.IsVerticalLayout;
                    }
                    else if (e.ToolTip == "对齐")
                    {
                        _shareTextModel.TextAlign = requestedTextAlignment;
                        updatedFields = FontSettingsFields.HorizontalAlign;
                    }
                    
                    ApplyFontSettings(updatedFields);
                });

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private void OnSharedDataChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ShareTextModel.Text):
                case nameof(ShareTextModel.CurrentFontFamily):
                case nameof(ShareTextModel.FontSize):
                case nameof(ShareTextModel.IsBold):
                case nameof(ShareTextModel.IsItalic):
                case nameof(ShareTextModel.IsUnderline):
                case nameof(ShareTextModel.IsVerticalLayout):
                case nameof(ShareTextModel.TextAlign):
                case nameof(ShareTextModel.LineHeight):
                case nameof(ShareTextModel.CharSpacing):
                    UpdateTextToolStatus(_shareTextModel, false);
                    break;
            }
        }

        internal static void SyncShareModelFromDrawTextLayout(ShareTextModel shareTextModel, DrawText drawText)
        {
            if (shareTextModel == null || drawText?.TextModel?.FontSettings == null)
            {
                return;
            }

            var fontSettings = drawText.TextModel.FontSettings;
            shareTextModel.Text = drawText.TextModel.Text;
            shareTextModel.CurrentFontFamily = fontSettings.FontFamily;
            shareTextModel.FontSize = fontSettings.FontSize;
            shareTextModel.LineHeight = fontSettings.LineHeight;
            shareTextModel.CharSpacing = fontSettings.CharacterSpacing;
            shareTextModel.IsItalic = fontSettings.IsItalic;
            shareTextModel.IsBold = fontSettings.IsBold;
            shareTextModel.IsUnderline = fontSettings.IsUnderline;
            shareTextModel.IsVerticalLayout = fontSettings.IsVerticalLayout;
            shareTextModel.TextAlign = (int)fontSettings.HorizontalAlign;
        }

        private void FontFlamilyComBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            _isUpdatingFromCanvas = false;

            if (FontFlamilyComBox.ComboBox.SelectedItem == null) return;

            string itemStr = FontFlamilyComBox.ComboBox.SelectedItem.ToString();
            if (!string.IsNullOrEmpty(itemStr) && itemStr.Contains(":"))
            {
                string selectedItem = itemStr.Split(":").Last().Trim().ToString();
                _shareTextModel.CurrentFontFamily = selectedItem;
                ApplyFontSettings(FontSettingsFields.FontFamily);
            }
        }

        private void FontSizeNumericUpDown_LostFocus(object sender, RoutedEventArgs e)
        {
            _isUpdatingFromCanvas = false;
            _shareTextModel.FontSize = FontSizeNumericUpDownControl.NumericUpDownBtn.Value;
            ApplyFontSettings(FontSettingsFields.FontSize);
        }

        private void LineHeightNumericUpDown_LostFocus(object sender, RoutedEventArgs e)
        {
            _isUpdatingFromCanvas = false;
            _shareTextModel.LineHeight = LineHeightNumericUpDownControl.NumericUpDownBtn.Value;
            ApplyFontSettings(FontSettingsFields.LineHeight);
        }

        private void CharacterSpacingNumericUpDown_LostFocus(object sender, RoutedEventArgs e)
        {
            _isUpdatingFromCanvas = false;
            _shareTextModel.CharSpacing = CharacterSpacingNumericUpDownControl.NumericUpDownBtn.Value;
            ApplyFontSettings(FontSettingsFields.CharacterSpacing);
        }

        private async Task ApplyFontSettings(FontSettingsFields updatedFields = FontSettingsFields.All)
        {
            if (_shapeService == null) return;

            if (_isUpdatingFromCanvas) return;
            var fontSettingsDto = new FontSettingsDto()
            {
                FontFamily = _shareTextModel.CurrentFontFamily,
                FontSize = (float)_shareTextModel.FontSize,
                LineHeight = (float)_shareTextModel.LineHeight,
                CharacterSpacing = (float)_shareTextModel.CharSpacing,
                IsBold = _shareTextModel.IsBold,
                IsItalic = _shareTextModel.IsItalic,
                IsUnderline = _shareTextModel.IsUnderline,
                IsVerticalLayout = _shareTextModel.IsVerticalLayout,
                HorizontalAlign = _shareTextModel.TextAlign,
                UpdatedFields = updatedFields,
            };
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _shapeService.SetTextFont(fontSettingsDto, updatedFields: updatedFields);
            });
        }
        
        /// <summary>
        /// UI按钮状态更新，保持与CurrentRenderOptions属性同步
        /// </summary>
        /// <param name="CurrentRenderOptions"></param>
        private void UpdateTextToolStatus(ShareTextModel shareTextModel, bool updatingFromCanvas = true)
        {
            if (updatingFromCanvas)
            {
                _isUpdatingFromCanvas = true;
                ItalicButton.CommandTriggered = false;
                BoldButton.CommandTriggered = false;
                UnderlineButton.CommandTriggered = false;
                HorizontalAlignButton.CommandTriggered = false;
                VerticalAlignButton.CommandTriggered = false;
                AlignButton.CommandTriggered = false;
                FontSizeNumericUpDownControl.CommandTriggered = false;
                LineHeightNumericUpDownControl.CommandTriggered = false;
                CharacterSpacingNumericUpDownControl.CommandTriggered = false;
            }

            ItalicButton.IsChecked = shareTextModel.IsItalic;
            BoldButton.IsChecked = shareTextModel.IsBold;
            UnderlineButton.IsChecked = shareTextModel.IsUnderline;
            HorizontalAlignButton.IsChecked = !shareTextModel.IsVerticalLayout;
            VerticalAlignButton.IsChecked = shareTextModel.IsVerticalLayout;
            AlignButton.TextAlignment = shareTextModel.TextAlign;

            FontSizeNumericUpDownControl.NumericUpDownBtn.Value = shareTextModel.FontSize;
            LineHeightNumericUpDownControl.NumericUpDownBtn.Value = shareTextModel.LineHeight;
            CharacterSpacingNumericUpDownControl.NumericUpDownBtn.Value = shareTextModel.CharSpacing;
            FontFlamilyComBox.ComboBox.SelectedIndex = GetFontFlamilySelectIndex(shareTextModel.CurrentFontFamily);

        }

        private int GetFontFlamilySelectIndex(string fontFamily)
        {
            int index = 0;
            if (_shapeParamViewModel.FontFamilyList.Contains(fontFamily))
            {
                index = _shapeParamViewModel.FontFamilyList.IndexOf(fontFamily);
            }
            return index;
        }

        public void EnableTextToolButton(bool enable)
        {
            ItalicButton.IsEnabled = enable;
            BoldButton.IsEnabled = enable;
            UnderlineButton.IsEnabled = enable;
            HorizontalAlignButton.IsEnabled = enable;
            VerticalAlignButton.IsEnabled = enable;
            AlignButton.DropDownButton.IsEnabled = enable;

            FontFlamilyComBox.ComboBox.IsEnabled = enable;
            FontSizeNumericUpDownControl.NumericUpDownBtn.IsEnabled = enable;
            LineHeightNumericUpDownControl.NumericUpDownBtn.IsEnabled = enable;
            CharacterSpacingNumericUpDownControl.NumericUpDownBtn.IsEnabled = enable;
            FontSizeNumericUpDownControl.UpdateEnabledState();
            LineHeightNumericUpDownControl.UpdateEnabledState();
            CharacterSpacingNumericUpDownControl.UpdateEnabledState();

            // 如果图标全部禁用，则清空选中状态
            if (!enable)
            {
                ItalicButton.IsChecked = false;
                BoldButton.IsChecked = false;
                UnderlineButton.IsChecked = false;
                HorizontalAlignButton.IsChecked = false;
                VerticalAlignButton.IsChecked = false;
            }
        }

        public void UpdateTextFontSettings(DrawTextDto drawTextDto)
        {
            if (drawTextDto == null)
            {
                return;
            }

            //var textModel = drawText.TextModel;
            var fontSettings = new FontSettings()
            {
                FontFamily = drawTextDto.FontFamily,
                FontSize = drawTextDto.FontSize,
                IsBold = drawTextDto.IsBold,
                IsItalic = drawTextDto.IsItalic,
                IsUnderline = drawTextDto.IsUnderline,
                IsVerticalLayout = drawTextDto.IsVerticalLayout,
                HorizontalAlign = (SKTextAlign)drawTextDto.HorizontalAlign,
                LineHeight = drawTextDto.LineHeight,
                CharacterSpacing = drawTextDto.CharacterSpacing
            };

            DocumentContext.Instance.CurrentTextFontSettings = CloneFontSettings(fontSettings);

            _shareTextModel.Text = drawTextDto.Text;

            if (!IsFontSettingsEqual(fontSettings))
            {
                _shareTextModel.CurrentFontFamily = fontSettings.FontFamily;
                _shareTextModel.FontSize = fontSettings.FontSize;
                _shareTextModel.LineHeight = fontSettings.LineHeight;
                _shareTextModel.CharSpacing = fontSettings.CharacterSpacing;
                _shareTextModel.IsItalic = fontSettings.IsItalic;
                _shareTextModel.IsBold = fontSettings.IsBold;
                _shareTextModel.IsUnderline = fontSettings.IsUnderline;
                _shareTextModel.IsVerticalLayout = fontSettings.IsVerticalLayout;
                _shareTextModel.TextAlign = (int)fontSettings.HorizontalAlign;
            }

            UpdateTextToolStatus(_shareTextModel, true);
        }

        public void UpdateTextFontSettings(DrawText drawText)
        {
            if (drawText?.TextModel == null)
            {
                return;
            }

            var textModel = drawText.TextModel;
            var fontSettings = textModel.FontSettings ?? new FontSettings();

            DocumentContext.Instance.CurrentTextFontSettings = CloneFontSettings(fontSettings);

            _shareTextModel.Text = textModel.Text;

            if (!IsFontSettingsEqual(fontSettings))
            {
                _shareTextModel.CurrentFontFamily = fontSettings.FontFamily;
                _shareTextModel.FontSize = fontSettings.FontSize;
                _shareTextModel.LineHeight = fontSettings.LineHeight;
                _shareTextModel.CharSpacing = fontSettings.CharacterSpacing;
                _shareTextModel.IsItalic = fontSettings.IsItalic;
                _shareTextModel.IsBold = fontSettings.IsBold;
                _shareTextModel.IsUnderline = fontSettings.IsUnderline;
                _shareTextModel.IsVerticalLayout = fontSettings.IsVerticalLayout;
                _shareTextModel.TextAlign = (int)fontSettings.HorizontalAlign;
            }

            UpdateTextToolStatus(_shareTextModel, true);
        }

        private bool IsFontSettingsEqual(FontSettings fontSettings)
        {
            return _shareTextModel.CurrentFontFamily == fontSettings.FontFamily &&
                   _shareTextModel.FontSize == fontSettings.FontSize &&
                   _shareTextModel.LineHeight == fontSettings.LineHeight &&
                   _shareTextModel.CharSpacing == fontSettings.CharacterSpacing &&
                   _shareTextModel.IsItalic == fontSettings.IsItalic &&
                   _shareTextModel.IsBold == fontSettings.IsBold &&
                   _shareTextModel.IsUnderline == fontSettings.IsUnderline &&
                   _shareTextModel.IsVerticalLayout == fontSettings.IsVerticalLayout &&
                   _shareTextModel.TextAlign == (int)fontSettings.HorizontalAlign;
        }

        private static FontSettings CloneFontSettings(FontSettings source)
        {
            if (source == null)
            {
                return new FontSettings();
            }

            return new FontSettings
            {
                FontFamily = source.FontFamily,
                FontSize = source.FontSize,
                IsBold = source.IsBold,
                IsItalic = source.IsItalic,
                IsUnderline = source.IsUnderline,
                IsVerticalLayout = source.IsVerticalLayout,
                HorizontalAlign = source.HorizontalAlign,
                LineHeight = source.LineHeight,
                CharacterSpacing = source.CharacterSpacing,
                TextColor = source.TextColor
            };
        }
    }
}
