using System.Diagnostics.PerformanceData;
using System.Windows.Input;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Controls.Service;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class SizeToolbarViewModel : ObservableObject
    {
        [ObservableProperty] private int selectedTab = 0; // 0:位移 1:旋转 2:缩放 3:倾斜

        [ObservableProperty] private bool _isUEnabled = false;
        // 位移
        [ObservableProperty] private double moveX;
        [ObservableProperty] private double moveY;


        // 旋转
        [ObservableProperty] private double rotateAngle;
        // 实际角度
        [ObservableProperty] private double actRotateAngle;
        [ObservableProperty] private double rotateCenterX;
        [ObservableProperty] private double rotateCenterY;
        [ObservableProperty] private bool isLockCenter;
        [ObservableProperty] private bool isRotateEnabled = true;

        // 缩放
        [ObservableProperty] private double scaleWidth;
        [ObservableProperty] private double scaleHeight;
        [ObservableProperty] private bool isLocked = false;
        // 是否允许用户手动切换宽高锁。某些选区约束会强制禁用该开关。
        [ObservableProperty] private bool isLockToggleEnabled = true;
        [ObservableProperty] private bool isScaleEnabled = true;

        // 倾斜
        [ObservableProperty] private double skewHorizontal;
        [ObservableProperty] private double skewVertical;
        [ObservableProperty] private bool isSkewEnabled = true;

        // 位移中心点
        [ObservableProperty] private CenterPositionType moveCenter = CenterPositionType.Center;

        // 旋转中心点
        [ObservableProperty] private CenterPositionType rotateCenter = CenterPositionType.Center;

        // 缩放中心点
        [ObservableProperty] private CenterPositionType scaleCenter = CenterPositionType.Center;

        // 倾斜中心点
        [ObservableProperty] private CenterPositionType skewCenter = CenterPositionType.Center;

        public ICommand SelectTabCommand => new RelayCommand<object>(SelectedTabApply);

        private IEventBus? _eventBus => EventBus.Instance;
        private readonly IShapeService _shapeService;
        // 记录用户在“无强制约束”场景下最后一次选择的锁定状态。
        private bool _preferredIsLocked;
        // 防止程序按选区约束回写 IsLocked 时，又把这次回写误记成用户偏好。
        private bool _isApplyingSelectionLock;

        private bool _isShowSkewCenterIcon;

        public SizeToolbarViewModel()
        {
            _shapeService = App.GetService<IShapeService>();

            _eventBus?.Subscribe<CanvasChangedEvent>(data =>
            {
                switch (data.ChangeType)
                {
                    case CanvasChangeType.SelectChanged:

                        if (data.Data is Dictionary<ShapeType, SelectChangedInfo> selectedObjects)
                        {
                            if (selectedObjects != null && selectedObjects.Count > 0)
                            {
                                IsUEnabled = true;
                            }
                            else
                            {
                                IsUEnabled = false;
                                ResetValue();
                            }
                        }
                        else
                        {
                            IsUEnabled = false;
                            ResetValue();
                        }
                        break;
                    case CanvasChangeType.SelectSharps:
                        if (data.Data != null)
                        {
                            _selectedSharpsDto = data.Data as SelectedSharpsDto;
                            UpdateAllFromBounds();
                        }
                        break;

                    case CanvasChangeType.TransformChanged:
                        if (data.Data != null)
                        {
                            _selectedSharpsDto = data.Data as SelectedSharpsDto;
                            UpdateAllFromBounds();
                        }
                        break;
                    default:
                        break;
                }
            });
        }
        private SelectedSharpsDto? _selectedSharpsDto;

        partial void OnMoveCenterChanged(CenterPositionType value)
        {
            UpdateMovePosition();
        }

        partial void OnRotateCenterChanged(CenterPositionType value)
        {
            if (IsLockCenter) IsLockCenter = false;
            UpdateRotatePosition();

            UpdateRotateCenterIcon();
        }

        private void UpdateRotateCenterIcon()
        {
            _shapeService.ChangeSelectedState(3);

            _shapeService.UpdateRotateCenterIcon(RotateCenterX, RotateCenterY, _isShowSkewCenterIcon);
        }

        partial void OnSkewCenterChanged(CenterPositionType centerType)
        {
            ChangeSkewCenterIcon(centerType);
        }

        private void ChangeSkewCenterIcon(CenterPositionType centerType)
        {
            // 使用 AABB 计算旋转中心预设位置（而非图形自身 Width/Height）
            var aabb = GetSelectedShapesAABB();

            if (aabb.IsEmpty) return;

            var (offsetX, offsetY) = GetOffsetFromCenter(centerType, aabb.Width, aabb.Height);
            float newCX = aabb.MidX + (float)offsetX;
            float newCY = aabb.MidY + (float)offsetY;

            _shapeService.UpdateSkewCenterIcon(newCX, newCY, _isShowSkewCenterIcon);
        }

        /// <summary>
        /// 获取当前选中图形的轴对齐包围盒（AABB）。
        /// 直接从图形对象计算，避免缓存问题。
        /// </summary>
        private SKRect GetSelectedShapesAABB()
        {
            var ctx = DocumentContext.Instance;
            var canvas = ctx?.ActiveCanvas;
            if (canvas == null) return SKRect.Empty;

            var selectedShapes = canvas.Selection.Transformables;
            if (selectedShapes.Count == 0) return SKRect.Empty;

            // 直接从图形计算 AABB，不走缓存
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var shape in selectedShapes)
            {
                var bbox = shape.GetAABB();
                if (bbox.IsEmpty) continue;
                if (bbox.Left < minX) minX = bbox.Left;
                if (bbox.Top < minY) minY = bbox.Top;
                if (bbox.Right > maxX) maxX = bbox.Right;
                if (bbox.Bottom > maxY) maxY = bbox.Bottom;
            }

            if (minX > maxX || minY > maxY) return SKRect.Empty;
            return new SKRect(minX, minY, maxX, maxY);
        }

        partial void OnScaleCenterChanged(CenterPositionType value)
        {
            // 缩放中心点变更不需要更新输入框，仅影响 Apply 时的锚点计算
        }

        /// <summary>
        /// 根据当前选中的锚点类型(MoveCenter)和图形的真实中心(SharpCenter)反算出锚点的X,Y
        /// 使用 SharpCenter（OBB 真实中心）而非 AABB 中心，确保倾斜后锚点位置正确。
        /// 当图形倾斜时，SharpCenter ≠ AABB 中心，但 SharpCenter 才是 SetCenter 设置的目标。
        /// </summary>
        private void UpdateMovePosition()
        {
            //if (DocumentContext.Instance.SelectState == SelectState.None || DocumentContext.Instance.SelectState == SelectState.FirstSelected)
            //{
            //    var obb = GetSelectedShapesOBB();
            //    if (obb.aabb.IsEmpty) return;

            //    double centerX = obb.center.X;
            //    double centerY = obb.center.Y;
            //    double width = obb.aabb.Width;
            //    double height = obb.aabb.Height;

            //    var (offsetX, offsetY) = GetOffsetFromCenter(MoveCenter, width, height);
            //    MoveX = centerX + offsetX;
            //    MoveY = centerY + offsetY;
            //}
            //else
            //{
            var aabb = GetSelectedShapesAABB();
            if (aabb.IsEmpty) return;

            double centerX = aabb.MidX;
            double centerY = aabb.MidY;
            double width = aabb.Width;
            double height = aabb.Height;

            var (offsetX, offsetY) = GetOffsetFromCenter(MoveCenter, width, height);
            MoveX = centerX + offsetX;
            MoveY = centerY + offsetY;
            //}

            //var ctx = DocumentContext.Instance;
            //var canvas = ctx?.ActiveCanvas;
            //if (canvas == null) return;

            //var selectedShapes = canvas.SelectedShapes.OfType<DrawObject>().Where(s => !s.IsLocked).ToList();
            //if (selectedShapes.Count == 0) return;

            //// 获取真实的 OBB 中心（SharpCenter）
            //SKPoint obbCenter;
            //if (selectedShapes.Count == 1)
            //{
            //    obbCenter = selectedShapes[0].SharpCenter;
            //}
            //else
            //{
            //    // 多个图形时，计算 SharpCenter 的平均值
            //    float sumX = 0, sumY = 0;
            //    foreach (var shape in selectedShapes)
            //    {
            //        sumX += shape.SharpCenter.X;
            //        sumY += shape.SharpCenter.Y;
            //    }
            //    obbCenter = new SKPoint(sumX / selectedShapes.Count, sumY / selectedShapes.Count);
            //}

            //// 使用 AABB 来定义锚点的宽高（确保锚点逻辑一致）
            //var aabb = GetSelectedShapesAABB();
            //if (aabb.IsEmpty) return;

            //double width = aabb.Width;
            //double height = aabb.Height;

            //var (offsetX, offsetY) = GetOffsetFromCenter(MoveCenter, width, height);

            //// 基于真实 OBB 中心计算锚点位置
            //MoveX = obbCenter.X + offsetX;
            //MoveY = obbCenter.Y + offsetY;
        }

        /// <summary>
        /// 根据当前选中的锚点类型(RotateCenter)和图形Bounds，反算出旋转中心的X,Y
        /// </summary>
        private void UpdateRotatePosition()
        {
            if (_selectedSharpsDto?.DrawObjectDtoData == null) return;

            // 使用 AABB 计算旋转中心预设位置
            var aabb = GetSelectedShapesAABB();
            if (aabb.IsEmpty) return;

            double centerX = aabb.MidX;
            double centerY = aabb.MidY;

            var (offsetX, offsetY) = GetOffsetFromCenter(RotateCenter, aabb.Width, aabb.Height);

            RotateCenterX = centerX + offsetX;
            RotateCenterY = centerY + offsetY;
        }
        private void ResetValue()
        {
            MoveX = 0;
            MoveY = 0;
            RotateAngle = 0;
            RotateCenterX = 0;
            RotateCenterY = 0;
            ScaleWidth = 0;
            ScaleHeight = 0;
            SkewHorizontal = 0;
            SkewVertical = 0;
            IsLocked = false;
            IsLockToggleEnabled = true;
            _preferredIsLocked = false;
            IsSkewEnabled = true;
            IsScaleEnabled = true;
            IsRotateEnabled = true;
        }
        /// <summary>
        /// 从Bounds同步所有变换参数到UI字段
        /// </summary>
        private void UpdateAllFromBounds()
        {
            if (_selectedSharpsDto?.DrawObjectDtoData == null) return;

            var bounds = _selectedSharpsDto.DrawObjectDtoData;

            // 位移
            UpdateMovePosition();

            // 旋转
            //RotateAngle = bounds.Rotation;
            ActRotateAngle = bounds.Rotation;
            if (IsLockCenter)
            {
                RotateCenterX = bounds.RotationCenter.X;
                RotateCenterY = bounds.RotationCenter.Y;
            }
            else UpdateRotatePosition();
            // 缩放
            var requiresUniformScale = _selectedSharpsDto.ResizeConstraint.HasFlag(SelectionResizeConstraint.RequireUniformScale);

            _isApplyingSelectionLock = true;
            if (requiresUniformScale)
            {
                ScaleWidth = bounds.Width;
                ScaleHeight = bounds.Height;
                IsLocked = true;
                IsLockToggleEnabled = false;
            }
            else
            {
                IsLockToggleEnabled = true;
                IsLocked = _preferredIsLocked;
                ScaleWidth = bounds.Width;
                ScaleHeight = bounds.Height;
            }
            _isApplyingSelectionLock = false;

            // 倾斜
            SkewHorizontal = bounds.SkewX;
            SkewVertical = bounds.SkewY;

            if (_selectedSharpsDto.EditingObject != null)
            {
                IsUEnabled = !_selectedSharpsDto.IsAllLock;
                if (!IsUEnabled)
                {
                    ResetValue();
                }
                IsSkewEnabled = true;
                IsScaleEnabled = true;
                IsRotateEnabled = true;
            }
            if (_selectedSharpsDto.EditingObject?.Type == ShapeType.Circle ||
                _selectedSharpsDto.EditingObject?.Type == ShapeType.Arc ||
                _selectedSharpsDto.EditingObject?.Type == ShapeType.Point)
            {
                IsSkewEnabled = false;
            }
            if (_selectedSharpsDto.EditingObject?.Type == ShapeType.Point)
            {
                IsScaleEnabled = false;
                //IsRotateEnabled = false;
            }

            if (_selectedSharpsDto.EditingObject?.Type == ShapeType.Rectangle)
            {
                var data = _selectedSharpsDto.EditingObject as DrawRectangleDto;
                if (data != null)
                {
                    if (data.HasRoundedCorners)
                    {
                        IsSkewEnabled = false;
                    }
                }
            }
            if (_selectedSharpsDto.EditingObject?.Type == ShapeType.Hatch)
            {
                var data = _selectedSharpsDto.EditingObject as HatchDto;
                if (data != null && data.IsAssociative)
                {
                    IsUEnabled = false;
                }
            }
        }

        private void SelectedTabApply(object? obj)
        {
            string tabName = obj?.ToString() ?? "0";
            if (int.TryParse(tabName, out int tabIndex))
            {
                SelectedTab = tabIndex;

                _isShowSkewCenterIcon = SelectedTab == 3 ? true : false;

                if (SelectedTab == 1 || SelectedTab == 3)
                {
                    if (_isShowSkewCenterIcon)
                        ChangeSkewCenterIcon(SkewCenter);
                    else
                        UpdateRotateCenterIcon();
                    _shapeService.ChangeSelectedState(3);
                }
                else
                    _shapeService.ChangeSelectedState(2);
            }
        }

        private (double offsetX, double offsetY) GetOffsetFromCenter(CenterPositionType centerType, double width, double height)
        {
            if (_selectedSharpsDto.EditingObject?.Type == ShapeType.Point)
            {
                return (0, 0);
            }
            switch (centerType)
            {
                case CenterPositionType.TopLeft: return (-width / 2, height / 2); // 原先是 -height/2
                case CenterPositionType.TopCenter: return (0, height / 2);
                case CenterPositionType.TopRight: return (width / 2, height / 2);
                case CenterPositionType.MiddleLeft: return (-width / 2, 0);
                case CenterPositionType.Center: return (0, 0);
                case CenterPositionType.MiddleRight: return (width / 2, 0);
                case CenterPositionType.BottomLeft: return (-width / 2, -height / 2); // 原先是 +height/2
                case CenterPositionType.BottomCenter: return (0, -height / 2);
                case CenterPositionType.BottomRight: return (width / 2, -height / 2);
                default: return (0, 0);
            }
        }


        [RelayCommand]
        private void Apply()
        {
            if (_selectedSharpsDto?.DrawObjectDtoData == null) return;

            switch (SelectedTab)
            {
                case 0: // 位移
                    _shapeService.ChangeSelectedState(2);
                    ApplyMove();
                    break;
                case 1: // 旋转
                    _shapeService.ChangeSelectedState(3);
                    ApplyRotate();
                    break;
                case 2: // 缩放
                    _shapeService.ChangeSelectedState(2);
                    ApplyScale();
                    break;
                case 3: // 倾斜
                    _shapeService.ChangeSelectedState(3);
                    ApplySkew();
                    break;
            }
        }

        /// <summary>
        /// 位移：将所选图形的指定锚点移动到世界坐标 (MoveX, MoveY)
        /// 使用 AABB 计算锚点偏移逻辑，但补偿 OBB 和 AABB 的中心差值。
        /// 当图形有倾斜操作时，AABB 中心 ≠ OBB 中心，需要加上差值来确保设置的是真实 OBB 中心。
        /// </summary>
        private void ApplyMove()
        {
            double targetX = MoveX;
            double targetY = MoveY;

            var ctx = DocumentContext.Instance;
            var canvas = ctx?.ActiveCanvas;
            if (canvas == null) return;

            var selectedShapes = _shapeService.GetSelections();
            if (selectedShapes.Value?.Count == 0) return;

            // 获取 AABB 用来定义锚点的宽高和中心逻辑
            var aabb = GetSelectedShapesAABB();
            if (aabb.IsEmpty) return;

            double aabbCenterX = aabb.MidX;
            double aabbCenterY = aabb.MidY;

            // 基于 AABB 计算锚点偏移
            (double offsetX, double offsetY) = GetOffsetFromCenter(MoveCenter, aabb.Width, aabb.Height);

            // 计算目标中心：先从锚点反推到 AABB 中心，再加上 OBB 和 AABB 的差值
            double newCenterX = targetX - offsetX - aabbCenterX;
            double newCenterY = targetY - offsetY - aabbCenterY;

            _shapeService.SetTranslate(newCenterX, newCenterY);
            _shapeService.ChangeSelectedState(2);
        }

        /// <summary>旋转：设置旋转中心及旋转角度</summary>
        private void ApplyRotate()
        {
            _shapeService.SetRotation(RotateCenterX, RotateCenterY, RotateAngle);
        }

        /// <summary>缩放：设置尺寸，并根据锚点类型调整中心位置使锚点不动</summary>
        private void ApplyScale()
        {
            var aabb = GetSelectedShapesAABB();
            if (aabb.IsEmpty) return;

            double width = aabb.Width;
            double height = aabb.Height;
            double centerX = aabb.MidX;
            double centerY = aabb.MidY;

            // 计算当前锚点的位置（缩放时该点保持不动）
            var (oldOffsetX, oldOffsetY) = GetOffsetFromCenter(ScaleCenter, width, height);
            double anchorX = centerX + oldOffsetX;
            double anchorY = centerY + oldOffsetY;

            //// 设置新尺寸
            //_shapeService.SetDimension(ScaleWidth, ScaleHeight);

            //// 根据新尺寸计算新的中心点，使锚点位置不变
            //var (newOffsetX, newOffsetY) = GetOffsetFromCenter(ScaleCenter, ScaleWidth, ScaleHeight);
            //double newCenterX = anchorX - newOffsetX;
            //double newCenterY = anchorY - newOffsetY;

            //_shapeService.SetCenter(newCenterX, newCenterY);

            double scaleX = ScaleWidth / width;
            double scaleY = ScaleHeight / height;
            _shapeService.SetScale(anchorX, anchorY, scaleX, scaleY);
            _shapeService.ChangeSelectedState(2);
        }

        /// <summary>倾斜：设置水平/垂直倾斜角度</summary>
        private void ApplySkew()
        {
            // 根据SkewCenter锚点计算倾斜锚点坐标
            var aabb = GetSelectedShapesAABB();
            if (aabb.IsEmpty) return;
            var (offsetX, offsetY) = GetOffsetFromCenter(SkewCenter, aabb.Width, aabb.Height);
            double cx = aabb.MidX + offsetX;
            double cy = aabb.MidY + offsetY;
            _shapeService.SetSkew(cx, cy, SkewHorizontal, SkewVertical);
        }

        [RelayCommand]
        private void CopyAndApply()
        {
            if (_selectedSharpsDto?.DrawObjectDtoData == null) return;

            // 复制当前选中图形到剪贴板（保留原图备份）
            _shapeService.Copy();
            // 粘贴副本到画布
            _shapeService.Paste(false, true);
            // 对当前选中图形应用变换
            Apply();
        }

        partial void OnIsLockedChanged(bool value)
        {
            if (_isApplyingSelectionLock)
            {
                return;
            }

            _preferredIsLocked = value;
        }
    }
    public enum CenterPositionType
    {
        TopLeft,
        TopCenter,
        TopRight,
        MiddleLeft,
        Center,
        MiddleRight,
        BottomLeft,
        BottomCenter,
        BottomRight
    }
}
