using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.Views.EditMenu;
using System;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.ViewModes.EditMenu
{
    public partial class AlignPopupViewModel : DialogViewModelBase<AlignSettingsModel>
    {
        public AlignStandard AlignStandard { get; set; }

        /// <summary>旧字段，兼容单次对齐</summary>
        public AlignType AlignType { get; set; }

        /// <summary>水平方向对齐类型</summary>
        public AlignType HorizontalAlignType { get; set; }

        /// <summary>垂直方向对齐类型</summary>
        public AlignType VerticalAlignType { get; set; }

        private bool _isAlignLeft;
        public bool IsAlignLeft
        {
            get => _isAlignLeft;
            set
            {
                if (_isAlignLeft != value)
                {
                    _isAlignLeft = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        ApplyHorizontalAlignment(AlignType.Left);
                    }
                }
            }
        }

        private bool _isAlignCenter;
        public bool IsAlignCenter
        {
            get => _isAlignCenter;
            set
            {
                if (_isAlignCenter != value)
                {
                    _isAlignCenter = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        ApplyHorizontalAlignment(AlignType.Center);
                    }
                }
            }
        }

        private bool _isAlignRight;
        public bool IsAlignRight
        {
            get => _isAlignRight;
            set
            {
                if (_isAlignRight != value)
                {
                    _isAlignRight = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        ApplyHorizontalAlignment(AlignType.Right);
                    }
                }
            }
        }

        private bool _isAlignTop;
        public bool IsAlignTop
        {
            get => _isAlignTop;
            set
            {
                if (_isAlignTop != value)
                {
                    _isAlignTop = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        ApplyVerticalAlignment(AlignType.Top);
                    }
                }
            }
        }

        private bool _isAlignMiddle;
        public bool IsAlignMiddle
        {
            get => _isAlignMiddle;
            set
            {
                if (_isAlignMiddle != value)
                {
                    _isAlignMiddle = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        ApplyVerticalAlignment(AlignType.Middle);
                    }
                }
            }
        }

        private bool _isAlignBottom;
        public bool IsAlignBottom
        {
            get => _isAlignBottom;
            set
            {
                if (_isAlignBottom != value)
                {
                    _isAlignBottom = value;
                    OnPropertyChanged();
                    if (value)
                    {
                        ApplyVerticalAlignment(AlignType.Bottom);
                    }
                }
            }
        }

        protected override void OnPrepareForDialog()
        {
            IsAlignLeft = false;
            IsAlignRight = false;
            IsAlignMiddle = false;
            IsAlignTop = false; 
            IsAlignBottom = false;
            IsAlignCenter = false;
            HorizontalAlignType = AlignType.None;
            VerticalAlignType = AlignType.None;
        }

        public ICommand AlignStandardRadioCheckedCommand { get; }

        public AlignPopupViewModel()
        {
            Title = "对齐设置";
            WindowHeight = 260;
            Content = new AlignPopupView() { DataContext = this };

            AlignStandard = AlignStandard.LastChooseOne;

            AlignStandardRadioCheckedCommand = new RelayCommand<string>(ApplyAlignmentStandard);

            // 默认选中左对齐（水平方向）
            HorizontalAlignType = AlignType.Left;
            IsAlignLeft = true;
        }

        /// <summary>
        /// 应用水平方向对齐：仅在 Left/Center/Right 之间互斥
        /// </summary>
        private void ApplyHorizontalAlignment(AlignType alignType)
        {
            switch (alignType)
            {
                case AlignType.Left:
                    IsAlignCenter = false;
                    IsAlignRight = false;
                    break;
                case AlignType.Center:
                    IsAlignLeft = false;
                    IsAlignRight = false;
                    break;
                case AlignType.Right:
                    IsAlignLeft = false;
                    IsAlignCenter = false;
                    break;
            }
            HorizontalAlignType = alignType;
        }

        /// <summary>
        /// 应用垂直方向对齐：仅在 Top/Middle/Bottom 之间互斥
        /// </summary>
        private void ApplyVerticalAlignment(AlignType alignType)
        {
            switch (alignType)
            {
                case AlignType.Top:
                    IsAlignMiddle = false;
                    IsAlignBottom = false;
                    break;
                case AlignType.Middle:
                    IsAlignTop = false;
                    IsAlignBottom = false;
                    break;
                case AlignType.Bottom:
                    IsAlignTop = false;
                    IsAlignMiddle = false;
                    break;
            }
            VerticalAlignType = alignType;
        }

        private void ApplyAlignmentStandard(string mode)
        {
            try
            {
                AlignStandard = (AlignStandard)Enum.Parse(typeof(AlignStandard), mode, true);
            }
            catch (ArgumentException)
            {
                // 处理无效字符串
            }
        }


        protected override AlignSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override AlignSettingsModel? GetConfirmResult()
        {
            return new AlignSettingsModel()
            {
                AlignType = AlignType,
                HorizontalAlignType = HorizontalAlignType,
                VerticalAlignType = VerticalAlignType,
                AlignStandard = AlignStandard
            };
        }

        /// <summary>
        /// 将 AlignSettingsModel 转换为 AlignSettingsDto
        /// </summary>
        public static AlignSettingsDto ToDto(AlignSettingsModel model)
        {
            // 如果 HorizontalAlignType / VerticalAlignType 未设置（旧调用），
            // 则从 AlignType 推断（保持向后兼容）
            var hType = model.HorizontalAlignType;
            var vType = model.VerticalAlignType;
            if (hType == AlignType.None && vType == AlignType.None && model.AlignType != AlignType.None)
            {
                switch (model.AlignType)
                {
                    case AlignType.Left:
                    case AlignType.Center:
                    case AlignType.Right:
                        hType = model.AlignType;
                        break;
                    case AlignType.Top:
                    case AlignType.Middle:
                    case AlignType.Bottom:
                        vType = model.AlignType;
                        break;
                }
            }

            return new AlignSettingsDto
            {
                AlignType = model.AlignType switch
                {
                    Model.EditMenu.AlignType.Left => AlignTypeDto.Left,
                    Model.EditMenu.AlignType.Center => AlignTypeDto.Center,
                    Model.EditMenu.AlignType.Right => AlignTypeDto.Right,
                    Model.EditMenu.AlignType.Top => AlignTypeDto.Top,
                    Model.EditMenu.AlignType.Middle => AlignTypeDto.Middle,
                    Model.EditMenu.AlignType.Bottom => AlignTypeDto.Bottom,
                    _ => AlignTypeDto.None,
                },
                HorizontalAlignType = hType switch
                {
                    Model.EditMenu.AlignType.Left => AlignTypeDto.Left,
                    Model.EditMenu.AlignType.Center => AlignTypeDto.Center,
                    Model.EditMenu.AlignType.Right => AlignTypeDto.Right,
                    _ => AlignTypeDto.None,
                },
                VerticalAlignType = vType switch
                {
                    Model.EditMenu.AlignType.Top => AlignTypeDto.Top,
                    Model.EditMenu.AlignType.Middle => AlignTypeDto.Middle,
                    Model.EditMenu.AlignType.Bottom => AlignTypeDto.Bottom,
                    _ => AlignTypeDto.None,
                },
                AlignStandard = model.AlignStandard switch
                {
                    Model.EditMenu.AlignStandard.LastChooseOne => AlignStandardDto.LastChooseOne,
                    Model.EditMenu.AlignStandard.PageEdge => AlignStandardDto.PageEdge,
                    Model.EditMenu.AlignStandard.PageCenter => AlignStandardDto.PageCenter,
                    Model.EditMenu.AlignStandard.Baseline => AlignStandardDto.Baseline,
                    _ => AlignStandardDto.LastChooseOne,
                }
            };
        }
    }
}
