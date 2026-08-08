using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.DTO;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.Views.EditMenu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.ViewModes.EditMenu
{
    public partial class DistributionPopupViewModel : DialogViewModelBase<DistributionSettingsModel>
    {
        private bool _isAlignLeftDistribute;
        public bool IsAlignLeftDistribute
        {
            get => _isAlignLeftDistribute;
            set
            {
                if (_isAlignLeftDistribute != value)
                {
                    _isAlignLeftDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignLeftDistribute,0);
                    }
                }
            }
        }

        private bool _isAlignCenterDistribute;
        public bool IsAlignCenterDistribute
        {
            get => _isAlignCenterDistribute;
            set
            {
                if (_isAlignCenterDistribute != value)
                {
                    _isAlignCenterDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignCenterDistribute,0);
                    }
                }
            }
        }

        private bool _isAlignRightDistribute;
        public bool IsAlignRightDistribute
        {
            get => _isAlignRightDistribute;
            set
            {
                if (_isAlignRightDistribute != value)
                {
                    _isAlignRightDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignRightDistribute,0);
                    }
                }
            }
        }



        private bool _isAlignHorizontalSpaceDistribute;
        public bool IsAlignHorizontalSpaceDistribute
        {
            get => _isAlignHorizontalSpaceDistribute;
            set
            {
                if (_isAlignHorizontalSpaceDistribute != value)
                {
                    _isAlignHorizontalSpaceDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignHorizontalSpaceDistribute,0);
                    }
                }
            }
        }

        private bool _isAlignTopDistribute;
        public bool IsAlignTopDistribute
        {
            get => _isAlignTopDistribute;
            set
            {
                if (_isAlignTopDistribute != value)
                {
                    _isAlignTopDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignTopDistribute, 1);
                    }
                }
            }
        }

        private bool _isAlignMiddleDistribute;
        public bool IsAlignMiddleDistribute
        {
            get => _isAlignMiddleDistribute;
            set
            {
                if (_isAlignMiddleDistribute != value)
                {
                    _isAlignMiddleDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignMiddleDistribute, 1);
                    }
                }
            }
        }

        private bool _isAlignBottomDistribute;
        public bool IsAlignBottomDistribute
        {
            get => _isAlignBottomDistribute;
            set
            {
                if (_isAlignBottomDistribute != value)
                {
                    _isAlignBottomDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignBottomDistribute,1);
                    }
                }
            }
        }

        private bool _isAlignVerticalSpaceDistribute;
        public bool IsAlignVerticalSpaceDistribute
        {
            get => _isAlignVerticalSpaceDistribute;
            set
            {
                if (_isAlignVerticalSpaceDistribute != value)
                {
                    _isAlignVerticalSpaceDistribute = value;
                    OnPropertyChanged();
                    // 在此处执行业务逻辑，例如应用粗体样式
                    if (value)
                    {
                        ApplyDistributionMethod(DistributionType.AlignVerticalSpaceDistribute,1);
                    }
                }
            }
        }

        protected override void OnPrepareForDialog()
        {

            ApplyDistributionMethod(DistributionType.None, 0);
            ApplyDistributionMethod(DistributionType.None, 1);
        }

        public DistributionType DistributionType { get; set; }
        public DistributionType HorizontalDistributionType { get; set; }
        public DistributionType VerticalDistributionType { get; set; }
        public DistributionStandard DistributionStandard { get; set; }

        public ICommand DistributionStandardRadioCheckedCommand { get; }

        private List<DistributionType> _distributionTypes = new List<DistributionType>();

        public DistributionPopupViewModel()
        {
            Title = "分布设置";
            WindowHeight = 260;
            Content = new DistributionPopupView() { DataContext = this };
            DistributionStandardRadioCheckedCommand = new RelayCommand<string>(DistributionStandardRadioChecked);

        _distributionTypes.Add(DistributionType.AlignLeftDistribute);
        _distributionTypes.Add(DistributionType.AlignCenterDistribute);
        _distributionTypes.Add(DistributionType.AlignRightDistribute);
        _distributionTypes.Add(DistributionType.AlignHorizontalSpaceDistribute);
        _distributionTypes.Add(DistributionType.AlignTopDistribute);
        _distributionTypes.Add(DistributionType.AlignMiddleDistribute);
        _distributionTypes.Add(DistributionType.AlignBottomDistribute);
        _distributionTypes.Add(DistributionType.AlignVerticalSpaceDistribute);

            HorizontalDistributionType = DistributionType.AlignLeftDistribute;
            DistributionType = DistributionType.AlignLeftDistribute;

            

        }

       

        private void ApplyDistributionMethod(DistributionType distributionType,int flag)
        {
            try
            {
                //把其他的按钮置为false
                foreach (var item in _distributionTypes)
                {
                    if (item == distributionType)
                        continue;
                    switch (item)
                    {
                        case DistributionType.AlignLeftDistribute:
                            if (flag == 1) continue;
                            IsAlignLeftDistribute = false;
                            break;
                        case DistributionType.AlignCenterDistribute:
                            if (flag == 1) continue;
                            IsAlignCenterDistribute = false;
                            break;
                        case DistributionType.AlignRightDistribute:
                            if (flag == 1) continue;
                            IsAlignRightDistribute = false;
                            break;
                        case DistributionType.AlignHorizontalSpaceDistribute:
                            if (flag == 1) continue;
                            IsAlignHorizontalSpaceDistribute = false;
                            break;
                        case DistributionType.AlignTopDistribute:
                            if (flag == 0) continue;
                            IsAlignTopDistribute = false;
                            break;
                        case DistributionType.AlignMiddleDistribute:
                            if (flag == 0) continue;
                            IsAlignMiddleDistribute = false;
                            break;
                        case DistributionType.AlignBottomDistribute:
                            if (flag == 0) continue;
                            IsAlignBottomDistribute = false;
                            break;
                        case DistributionType.AlignVerticalSpaceDistribute:
                            if (flag == 0) continue;
                            IsAlignVerticalSpaceDistribute = false;
                            break;
                        default:
                            break;
                    }
                }

                DistributionType = distributionType;

                // 根据 flag 分别记录水平/垂直分布类型
                if (flag == 0)
                    HorizontalDistributionType = distributionType;
                else if (flag == 1)
                    VerticalDistributionType = distributionType;
            }
            catch (ArgumentException)
            {
                // 处理无效字符串
            }
        }

        private void DistributionStandardRadioChecked(string mode)
        {
            try
            {
                DistributionStandard = (DistributionStandard)Enum.Parse(typeof(DistributionStandard), mode, true);
            }
            catch (ArgumentException)
            {
                // 处理无效字符串
            }
        }

        protected override DistributionSettingsModel? GetCancelResult()
        {
            return null;
        }

        protected override DistributionSettingsModel? GetConfirmResult()
        {
            return new DistributionSettingsModel()
            {
                DistributionType = DistributionType,
                HorizontalDistributionType = HorizontalDistributionType,
                VerticalDistributionType = VerticalDistributionType,
                DistributionStandard = DistributionStandard,
            };
        }

        /// <summary>
        /// 将 DistributionSettingsModel 转换为 DistributeSettingsDto
        /// </summary>
        private static DistributeTypeDto MapType(DistributionType t) => t switch
        {
            DistributionType.AlignLeftDistribute => DistributeTypeDto.AlignLeftDistribute,
            DistributionType.AlignCenterDistribute => DistributeTypeDto.AlignCenterDistribute,
            DistributionType.AlignRightDistribute => DistributeTypeDto.AlignRightDistribute,
            DistributionType.AlignHorizontalSpaceDistribute => DistributeTypeDto.AlignHorizontalSpaceDistribute,
            DistributionType.AlignTopDistribute => DistributeTypeDto.AlignTopDistribute,
            DistributionType.AlignMiddleDistribute => DistributeTypeDto.AlignMiddleDistribute,
            DistributionType.AlignBottomDistribute => DistributeTypeDto.AlignBottomDistribute,
            DistributionType.AlignVerticalSpaceDistribute => DistributeTypeDto.AlignVerticalSpaceDistribute,
            _ => DistributeTypeDto.None,
        };

        public static DistributeSettingsDto ToDto(DistributionSettingsModel model)
        {
            return new DistributeSettingsDto
            {
                DistributeType = MapType(model.DistributionType),
                HorizontalDistributeType = MapType(model.HorizontalDistributionType),
                VerticalDistributeType = MapType(model.VerticalDistributionType),
                DistributeStandard = model.DistributionStandard switch
                {
                    DistributionStandard.SelectArea => DistributeStandardDto.SelectArea,
                    DistributionStandard.CanvasArea => DistributeStandardDto.CanvasArea,
                    _ => DistributeStandardDto.SelectArea,
                },
            };
        }
    }
}
