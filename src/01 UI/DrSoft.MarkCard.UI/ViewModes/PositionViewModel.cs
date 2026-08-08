using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Service;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.UI.UserControls;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class PositionViewModel : ObservableObject
    {
        [ObservableProperty] private double _x, _y;

        [ObservableProperty] private double _centerX, _centerY;
        [ObservableProperty] private double _angle;

        [ObservableProperty] private double _width = 0;
        [ObservableProperty] private double _height = 0;
        [ObservableProperty] private bool _isLocked = false;
        // 是否允许用户手动切换宽高锁。某些选区约束会强制禁用该开关。
        [ObservableProperty] private bool _isLockToggleEnabled = true;

        [ObservableProperty] private bool _isUEnabled = false;

        [ObservableProperty] private bool _isAlignEnabled = false;

        [ObservableProperty] private bool _isAlignLeft = false;

        [ObservableProperty] private bool isScaleEnabled = true;
        [ObservableProperty] private bool isRotateEnabled = true;
        partial void OnIsAlignLeftChanged(bool value)
        {

            if (value)
            {
                ApplyVerticalAlignment(AlignType.None);
                ApplyHorizontalAlignment(AlignType.Left);
            }
            else
            {
                ApplyHorizontalAlignment(AlignType.None);
            }
        }

        //水平居中对齐
        [ObservableProperty] private bool _isAlignCenter = false;
        partial void OnIsAlignCenterChanged(bool value)
        {
            if (value)
            {
                ApplyVerticalAlignment(AlignType.None);
                ApplyHorizontalAlignment(AlignType.Center);
            }
            else
            {
                ApplyHorizontalAlignment(AlignType.None);
            }
        }

        [ObservableProperty] private bool _isAlignRight = false;
        partial void OnIsAlignRightChanged(bool value)
        {
            if (value)
            {
                ApplyVerticalAlignment(AlignType.None);
                ApplyHorizontalAlignment(AlignType.Right);
            }
            else
            {
                ApplyHorizontalAlignment(AlignType.None);
            }
        }

        [ObservableProperty] private bool _isAlignTop = false;
        partial void OnIsAlignTopChanged(bool value)
        {
            if (value)
            {
                ApplyHorizontalAlignment(AlignType.None);
                ApplyVerticalAlignment(AlignType.Top);
            }
            else
            {
                ApplyVerticalAlignment(AlignType.None);
            }
        }

        //垂直居中对齐
        [ObservableProperty] private bool _isAlignMiddle = false;
        partial void OnIsAlignMiddleChanged(bool value)
        {
            if (value)
            {
                ApplyHorizontalAlignment(AlignType.None);
                ApplyVerticalAlignment(AlignType.Middle);
            }
            else
            {
                ApplyVerticalAlignment(AlignType.None);
            }
        }

        [ObservableProperty] private bool _isAlignBottom = false;
        partial void OnIsAlignBottomChanged(bool value)
        {
            if (value)
            {
                ApplyHorizontalAlignment(AlignType.None);
                ApplyVerticalAlignment(AlignType.Bottom);
            }
            else
            {
                ApplyVerticalAlignment(AlignType.None);
            }
        }

        // ── 分布属性 ──
        [ObservableProperty] private bool _isDistributeEnabled = false;

        [ObservableProperty] private bool _isAlignCenterDistribute = false;

        [ObservableProperty] private bool _isDistributeSelectAreaEnabled = false;

        partial void OnIsAlignCenterDistributeChanged(bool value)
        {
            if (value)
            {
                ApplyHorizontalDistribution(DistributionType.AlignCenterDistribute);
            }
        }

        [ObservableProperty] private bool _isAlignMiddleDistribute = false;
        partial void OnIsAlignMiddleDistributeChanged(bool value)
        {
            if (value)
            {
                ApplyVerticalDistribution(DistributionType.AlignMiddleDistribute);
            }
        }

        private DistributionStandard _distributionStandard = DistributionStandard.SelectArea;
        public DistributionStandard DistributionStandard
        {
            get => _distributionStandard;
            set
            {
                if (_distributionStandard != value)
                {
                    _distributionStandard = value;
                    OnPropertyChanged();
                    UpdateDistributeEnabled();
                }
            }
        }
        public DistributionType HorizontalDistributionType { get; set; }
        public DistributionType VerticalDistributionType { get; set; }

        public AlignStandard AlignStandard { get; set; }
        private IEventBus? _eventBus => EventBus.Instance;
        private readonly IShapeService _shapeService;
        private int selectObjectCount = 0;
        // 防止程序按选区约束回写 IsLocked 时，又把这次回写误记成用户偏好。
        private bool _isApplyingSelectionLock;

        public PositionViewModel()
        {
            _shapeService = App.GetService<IShapeService>();
            _eventBus?.Subscribe<CanvasChangedEvent>(data =>
            {

                switch (data.ChangeType)
                {
                    case CanvasChangeType.SelectChanged:

                        if (data.Data is not Dictionary<ShapeType, SelectChangedInfo> selectedObjects)
                        {
                            ResetValue();
                        }

                        break;

                    case CanvasChangeType.SelectSharps:
                        if (data.Data != null)
                        {
                            var SCurData = data.Data as SelectedSharpsDto;
                            if (SCurData != null)
                                SetValue(SCurData);
                        }
                        break;

                    case CanvasChangeType.TransformChanged:
                        if (data.Data != null)
                        {
                            var SCurData = data.Data as SelectedSharpsDto;
                            SetValue(SCurData);
                        }
                        break;
                    default:
                        break;
                }
            });

            _eventBus.Subscribe<CommandCapabilityChangedEvent>(data =>
            {
                selectObjectCount = data.Capabilities.TotalCount;

                if (selectObjectCount > 1)
                {
                    IsDistributeSelectAreaEnabled = true;
                }
                else
                {
                    IsDistributeSelectAreaEnabled = false;
                }

                if (selectObjectCount > 0)
                {

                    IsUEnabled = true;
                    if (selectObjectCount == 1)
                    {
                        if (AlignStandard == AlignStandard.PageCenter || AlignStandard == AlignStandard.PageEdge)
                            IsAlignEnabled = true;
                        else IsAlignEnabled = false;
                    }
                    else
                    {
                        IsAlignEnabled = true;
                    }

                }
                else
                {
                    IsUEnabled = false;
                    IsAlignEnabled = false;
                    if (!IsUEnabled)
                    {
                        X = Y = 0;
                        Width = Height = 0;
                        CenterX = CenterY = 0;
                        Angle = 0;
                    }
                }



                ClearAlign();

                UpdateDistributeEnabled();



            });
        }

        DrawObjectDto? CurBound;
        private void SetValue(SelectedSharpsDto? dto)
        {

            if (dto != null)
            {
                IsUEnabled = true;
                IsScaleEnabled = true;
                CurBound = dto.DrawObjectDtoData;
                if (dto.EditingObject != null)
                {
                    X = dto.EditingObject.OBBInfo.Center.X;
                    Y = dto.EditingObject.OBBInfo.Center.Y;

                    CenterX = dto.EditingObject.OBBInfo.Center.X;
                    CenterY = dto.EditingObject.OBBInfo.Center.Y;

                    if (dto.EditingObject.OBBInfo.Corners.Length >= 4)
                    {
                        Width = GetDistance(dto.EditingObject.OBBInfo.Corners[0], dto.EditingObject.OBBInfo.Corners[1]);
                        Height = GetDistance(dto.EditingObject.OBBInfo.Corners[0], dto.EditingObject.OBBInfo.Corners[3]);
                    }
                    else
                    {
                        Width = dto.DrawObjectDtoData.Width;
                        Height = dto.DrawObjectDtoData.Height;
                    }
                }
                else
                {
                    X = dto.DrawObjectDtoData.SharpCenter.X;
                    Y = dto.DrawObjectDtoData.SharpCenter.Y;

                    CenterX = dto.DrawObjectDtoData.RotationCenter.X;
                    CenterY = dto.DrawObjectDtoData.RotationCenter.Y;

                    Width = dto.DrawObjectDtoData.Width;
                    Height = dto.DrawObjectDtoData.Height;
                }

                Angle = dto.DrawObjectDtoData.Rotation;

                var requiresUniformScale = dto.ResizeConstraint.HasFlag(SelectionResizeConstraint.RequireUniformScale);

                _isApplyingSelectionLock = true;
                if (requiresUniformScale)
                {
                    IsLocked = false;
                    IsLocked = true;
                    IsLockToggleEnabled = false;
                }
                else
                {
                    IsLockToggleEnabled = true;
                    IsLocked = false;
                }

                _isApplyingSelectionLock = false;

                IsUEnabled = !dto.IsAllLock;
                if (!IsUEnabled)
                {
                    X = Y = 0;
                    Width = Height = 0;
                    CenterX = CenterY = 0;
                    Angle = 0;
                }

                if (dto.EditingObject?.Type == ShapeType.Point)
                {
                    IsScaleEnabled = false;
                }

                if (dto.EditingObject?.Type == ShapeType.Hatch)
                {
                    var data = dto.EditingObject as HatchDto;
                    if (data != null && data.IsAssociative)
                    {
                        IsUEnabled = false;
                    }
                }
            }
            else ResetValue();

        }

        private double GetDistance(Point2D p1, Point2D p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
        private void ResetValue()
        {
            X = Y = 0;
            Width = Height = 0;
            CenterX = CenterY = 0;
            Angle = 0;
            IsLocked = false;
            IsLockToggleEnabled = true;
        }

        // 手动实现命令属性（替代源生成器）
        public ICommand OnXCommittedCommand => new RelayCommand<double>(OnXCommitted);
        public ICommand OnYCommittedCommand => new RelayCommand<double>(OnYCommitted);
        public ICommand OnCenterXCommittedCommand => new RelayCommand<double>(OnCenterXCommitted);
        public ICommand OnCenterYCommittedCommand => new RelayCommand<double>(OnCenterYCommitted);
        public ICommand OnAngleCommittedCommand => new RelayCommand<double>(OnAngleCommitted);

        public ICommand AlignStandardRadioCheckedCommand => new RelayCommand<string>(ApplyAlignmentStandard);
        public ICommand DistributionStandardRadioCheckedCommand => new RelayCommand<string>(ApplyDistributionStandard);
        // 如果还有 Width/Height 等，也需要添加
        public ICommand OnWidthCommittedCommand => new RelayCommand<double>(OnWidthCommitted);
        public ICommand OnHeightCommittedCommand => new RelayCommand<double>(OnHeightCommitted);
        public ICommand OnFinalCommitCommand => new RelayCommand<Tuple<double, double>>(OnFinalCommit);

        public ICommand OnSizeCommittedCommand => new RelayCommand<SizeLockEventArg>(OnSizeCommitted);

        /// <summary>水平方向对齐类型</summary>
        public AlignType HorizontalAlignType { get; set; }

        /// <summary>垂直方向对齐类型</summary>
        public AlignType VerticalAlignType { get; set; }

        private void OnSizeCommitted(SizeLockEventArg? value)
        {
            if (Math.Abs(value.WidthValue - CurBound.Width) < 0.001 && Math.Abs(value.HeightValue - CurBound.Height) < 0.001) return;
            _shapeService.SetDimension(value.WidthValue, value.HeightValue);
        }

        private void ApplyAlignmentStandard(string mode)
        {
            try
            {
                AlignStandard = (AlignStandard)Enum.Parse(typeof(AlignStandard), mode, true);

                if (selectObjectCount == 1)
                {
                    if (AlignStandard == AlignStandard.PageCenter || AlignStandard == AlignStandard.PageEdge)
                        IsAlignEnabled = true;
                    else IsAlignEnabled = false;
                }
            }
            catch (ArgumentException)
            {
                // 处理无效字符串
            }
        }
        private void ApplyDistributionStandard(string mode)
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



        /// <summary>
        /// 根据选中数量和分布范围更新分布按钮的可用状态
        /// >= 3 个图形：可点击
        /// == 2 个图形且画布范围：可点击
        /// == 2 个图形且选取范围：不可点击
        /// </summary>
        private void UpdateDistributeEnabled()
        {
            IsDistributeEnabled = selectObjectCount >= 3
                || (selectObjectCount == 2 && DistributionStandard == DistributionStandard.CanvasArea);
        }

        /// <summary>
        /// 应用水平方向分布，并将其他分布按钮置为 false
        /// </summary>
        private void ApplyHorizontalDistribution(DistributionType distributionType)
        {
            // 取消垂直分布按钮（不触发其 changed 回调，因为值设为 false）
            IsAlignMiddleDistribute = false;
            HorizontalDistributionType = distributionType;
            _shapeService.Distribute(ToDistributeDto(distributionType, DistributionStandard));
        }

        /// <summary>
        /// 应用垂直方向分布，并将其他分布按钮置为 false
        /// </summary>
        private void ApplyVerticalDistribution(DistributionType distributionType)
        {
            // 取消水平分布按钮
            IsAlignCenterDistribute = false;
            VerticalDistributionType = distributionType;
            _shapeService.Distribute(ToDistributeDto(distributionType, DistributionStandard));
        }

        private static DistributeTypeDto MapDistributeType(DistributionType t) => t switch
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

        private static DistributeSettingsDto ToDistributeDto(DistributionType type, DistributionStandard standard)
        {
            var dto = MapDistributeType(type);
            bool isHorizontal = type is DistributionType.AlignLeftDistribute
                or DistributionType.AlignCenterDistribute
                or DistributionType.AlignRightDistribute
                or DistributionType.AlignHorizontalSpaceDistribute;

            return new DistributeSettingsDto
            {
                DistributeType = dto,
                HorizontalDistributeType = isHorizontal ? dto : DistributeTypeDto.None,
                VerticalDistributeType = isHorizontal ? DistributeTypeDto.None : dto,
                DistributeStandard = standard switch
                {
                    DistributionStandard.SelectArea => DistributeStandardDto.SelectArea,
                    DistributionStandard.CanvasArea => DistributeStandardDto.CanvasArea,
                    _ => DistributeStandardDto.SelectArea,
                },
            };
        }

        private void OnFinalCommit(Tuple<double, double>? tuple)
        {
            Width = tuple.Item1;
            Height = tuple.Item2;
            _shapeService.SetDimension(Width, Height);
        }

        private void OnXCommitted(double value)
        {
            X = value;
            if (Math.Abs(value - CurBound.SharpCenter.X) < 0.0001) return;
            _shapeService.SetCenter(X, Y);
        }

        private void OnYCommitted(double value)
        {
            Y = value;
            if (Math.Abs(value - CurBound.SharpCenter.Y) < 0.0001) return;
            _shapeService.SetCenter(X, Y);
        }

        private void OnCenterXCommitted(double value)
        {
            // _shapeService.SetRotary(CenterX, CenterY, Angle);
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
                case AlignType.None:
                    // 取消对齐时，重置所有相关按钮
                    IsAlignLeft = false;
                    IsAlignCenter = false;
                    IsAlignRight = false;
                    break;
            }
            HorizontalAlignType = alignType;
            if (alignType == AlignType.None) return;
            _shapeService.Align(ToDto(new AlignSettingsModel() { AlignStandard = AlignStandard, HorizontalAlignType = HorizontalAlignType, VerticalAlignType = VerticalAlignType }));
        }

        private void ClearAlign()
        {
            IsAlignLeft = false;
            IsAlignRight = false;
            IsAlignMiddle = false;
            IsAlignTop = false;
            IsAlignBottom = false;
            IsAlignCenter = false;
            HorizontalAlignType = AlignType.None;
            VerticalAlignType = AlignType.None;

            // 同时清除分布状态
            IsAlignCenterDistribute = false;
            IsAlignMiddleDistribute = false;
        }

        private AlignSettingsDto ToDto(AlignSettingsModel model)
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
                case AlignType.None:
                    // 取消对齐时，重置所有相关按钮
                    IsAlignTop = false;
                    IsAlignMiddle = false;
                    IsAlignBottom = false;
                    break;
            }
            VerticalAlignType = alignType;
            if (alignType == AlignType.None) return;
            _shapeService.Align(ToDto(new AlignSettingsModel() { AlignStandard = AlignStandard, HorizontalAlignType = HorizontalAlignType, VerticalAlignType = VerticalAlignType }));
        }

        private void OnCenterYCommitted(double value)
        {
            // _shapeService.SetRotary(CenterX, CenterY, Angle);
        }

        private void OnAngleCommitted(double value)
        {
            Angle = value;
            // IShapeService 定义为 SetRotation
            _shapeService.SetAbsoluteRotation(CenterX, CenterY, Angle);
        }

        private void OnWidthCommitted(double value)
        {
            Width = value;
            _shapeService.SetDimension(value, Height);
        }
        private void OnHeightCommitted(double value)
        {
            Height = value;
            _shapeService.SetDimension(Width, value);
        }

        partial void OnIsLockedChanged(bool value)
        {
            if (_isApplyingSelectionLock)
            {
                return;
            }
        }



        [RelayCommand]
        private void GoCenter()
        {
            _shapeService.SetCenter(0, 0);

            /* 切换对齐基准点逻辑 */
        }
    }
}
