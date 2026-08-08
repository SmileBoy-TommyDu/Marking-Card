using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.UI.UserControls;
using DrSoft.MarkCard.UI.ViewModes.Parameter;
using DrSoft.MarkCard.UI.Views;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class FillParamViewModel : BaseParamViewModel<FillParameter>
    {
        private readonly IDialogService _dialogService;
        private readonly DashSettingsViewModel _dashSettingsViewModel;

        public FillParamViewModel(IDialogService dialogService, DashSettingsViewModel dashSettingsViewModel)
        {
            _dialogService = dialogService;
            _dashSettingsViewModel = dashSettingsViewModel;
            Content = new FillParamView();
            // When "Apply All" is triggered we should persist the current UI parameter values
            // to the selected entities. Use a dedicated async binder instead of the preview Update().
            EventBus.Instance.Subscribe<ParaSaveEvent>(e =>
            {
                if (e.ParaSaveType == ParaSaveType.Element && e.Trigger && e.TriggerTitle.Equals("填充"))
                    _ = ApplyParametersToSelectionsAsync();
            });

            // 选中变化或撤销/重做后从 DrawingHatch 重新加载填充参数
            EventBus.Instance.Subscribe<CanvasChangedEvent>(data =>
            {
                if (data.ChangeType == CanvasChangeType.Command
                    || data.ChangeType == CanvasChangeType.SelectSharps)
                {
                    LoadFromHatch();
                }
            });
        }

        /// <summary>
        /// 从选中的 DrawingHatch 读取 HatchParamInfo，映射到 ViewModel 属性。
        /// 通过 IShapeFillService 接口获取，不直接访问画布对象。
        /// </summary>
        public void LoadFromHatch()
        {
            var result = _drawingService.Shapes.GetHatchParam();
            if (!result.IsSuccess || result.Value == null) return;

            var dto = result.Value;
            IsModifyFill = true;

            Model.FillColor = dto.FillColor;
            Model.FillStyleIndex = dto.FillStyleIndex;
            Model.Margin = dto.Margin;
            Model.RingSpacing = dto.RingSpacing;
            Model.LineSpacing = dto.LineSpacing;
            Model.Count = dto.Count;
            Model.StartAngle = dto.StartAngle;
            Model.IncrementalAngle = dto.IncrementalAngle;
            Model.Extension = dto.Extension;
            Model.FillTypeIndex = dto.FillTypeIndex;
            Model.AverageDistribute = dto.AverageDistribute;
            Model.InternalRings = dto.InternalRings;
            Model.DirectionTypeIndex = dto.DirectionTypeIndex;
            Model.RelativeToAngle = dto.RelativeToAngle;
            Model.ReverseFillLine = dto.ReverseFillLine;

            FillColor = dto.FillColor;

            // 通过接口获取选中图形来确定可用填充类型
            var selections = _drawingService.Shapes.GetSelections();
            SetFillTypeOptions(selections.IsSuccess ? selections.Value : null);
        }

        /// <summary>
        /// 弹框显示前重置 Model，确保每次打开都是新增场景的干净数据
        /// </summary>
        protected override void OnPrepareForDialog()
        {
            Model = new FillParameter();
            FillColor = Model.FillColor;
            Model.FillTypeIndex = 0;
            IsModifyFill = false;

            var selections = _drawingService.Shapes.GetSelections();
            SetFillTypeOptions(selections.IsSuccess ? selections.Value : null);
        }

        /// <summary>
        /// 根据选中图形的 ShapeType 确定可用的填充类型选项。
        /// </summary>
        public void SetFillTypeOptions(IReadOnlyList<IShapeData>? shapes)
        {
            var dd = Model;

            if (shapes == null || shapes.Count() == 0)
            {
                // 没有选中图形时，显示默认选项
                FillTypeOptions = new List<string> { "Z字型单向", "弓字型双向", "回字型", "螺旋型" };
                Model.FillTypeIndex = 0;  // 默认选中第一个
                OnFillTypeZShape();
                return;
            }

            // 根据选中的图形获取可用的填充类型索引
            var availableTypeIndices = GetAvailableFillTypes(shapes);

            // 动态构建选项列表（只包含可用的）
            var options = new List<string>();
            var indexMap = new Dictionary<int, string>
            {
                { 0, "Z字型单向" },
                { 1, "弓字型双向" },
                { 2, "回字型" },
                { 3, "螺旋型" }
            };

            foreach (var index in availableTypeIndices.OrderBy(i => i))
            {
                if (indexMap.TryGetValue(index, out var name))
                    options.Add(name);
            }

            FillTypeOptions = options;

            // 检查当前 Model.FillTypeIndex 是否可用
            if (!availableTypeIndices.Contains(Model.FillTypeIndex))
            {
                // 不可用时，选择第一个可用选项
                Model.FillTypeIndex = availableTypeIndices.FirstOrDefault();
            }

            // 调用对应的初始化方法
            switch (Model.FillTypeIndex)
            {
                case 0:
                    OnFillTypeZShape();
                    break;
                case 1:
                    OnFillTypeBowShape();
                    break;
                case 2:
                    OnFillTypeRectangle();
                    break;
                case 3:
                    OnFillTypeSpiral();
                    break;
                default:
                    OnFillTypeZShape();
                    break;
            }
        }

        /// <summary>
        /// 根据选中的图形获取可用的填充类型索引集合
        /// </summary>
        private HashSet<int> GetAvailableFillTypes(IReadOnlyList<IShapeData> shapes)
        {
            if (shapes == null || shapes.Count == 0)
                return new HashSet<int> { 0, 1, 2, 3 };

            // 获取所有图形支持类型的交集
            var availableTypes = GetShapeSupportedFillTypes(shapes[0]);

            for (int i = 1; i < shapes.Count; i++)
            {
                var supported = GetShapeSupportedFillTypes(shapes[i]);
                availableTypes.IntersectWith(supported);

                // 如果没有交集了，提前退出
                if (availableTypes.Count == 0)
                    break;
            }

            return availableTypes;
        }

        /// <summary>
        /// 获取单个图形支持的填充类型（基于 ShapeType 枚举判断）
        /// </summary>
        private HashSet<int> GetShapeSupportedFillTypes(IShapeData shape)
        {
            // 矩形和圆支持所有填充类型
            if (shape.Type == ShapeType.Rectangle || shape.Type == ShapeType.Circle)
                return new HashSet<int> { 0, 1, 2, 3 };

            // 折线、圆弧、贝塞尔、多边形、文字只支持 Z字型和弓字型
            if (shape.Type == ShapeType.PolyLine
                || shape.Type == ShapeType.Arc
                || shape.Type == ShapeType.Bezier
                || shape.Type == ShapeType.Polygon
                || shape.Type == ShapeType.ArbitraryCurve
                || shape.Type == ShapeType.Text)
                return new HashSet<int> { 0, 1 };

            // 填充对象本身支持所有类型
            if (shape.Type == ShapeType.Hatch)
                return new HashSet<int> { 0, 1, 2, 3 };

            // 未知类型，支持所有
            return new HashSet<int> { 0, 1, 2, 3 };
        }

        #region enable
        [ObservableProperty]
        private bool marginEnable = true;
        [ObservableProperty]
        private bool ringSpacingEnable = false;
        [ObservableProperty]
        private bool lineSpacingEnable = true;
        [ObservableProperty]
        private bool startAngleEnable = true;
        [ObservableProperty]
        private bool extensionEnable = true;

        [ObservableProperty]
        private bool averageDistributeEnable = true;
        [ObservableProperty]
        private bool internalRingsEnable = false;
        [ObservableProperty]
        private bool directionTypeIndexEnable = false;
        [ObservableProperty]
        private bool relativeToAngleEnable = true;
        [ObservableProperty]
        private bool reverseFillLineEnable = true;
        #endregion
        // 只需要选项列表
        [ObservableProperty]
        private List<string> _fillTypeOptions = new List<string>();

        [ObservableProperty]
        private string fillColor = "#000000";
        [ObservableProperty]
        private bool isConcentricFill;
        [ObservableProperty]
        private bool isSpiralFill;
        [ObservableProperty]
        private bool isModifyFill;

        [RelayCommand]
        private void BorderClick(object parameter)
        {
            //// 处理点击逻辑
            var model = parameter as FillParameter;
            //// TODO: 实现点击逻辑

            if (Model is not FillParameter fill) return;
            var dlg = new ColorPickerDialog() { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                var myColor = dlg.SelectedColor.ToString();
                if (string.IsNullOrEmpty(myColor)) return;
                Model.FillColor = myColor;
                FillColor = myColor;
            }
        }

        private void UpdateFillTypeProperties(int selectedIndex)
        {
            if (selectedIndex == 2 || selectedIndex == 3)
            {
                IsConcentricFill = selectedIndex == 2;
                IsSpiralFill = selectedIndex == 3;
            }
            else
            {
                IsConcentricFill = true;
                IsSpiralFill = false;
            }
        }

        [RelayCommand]
        private void FillTypeChanged(object parameter)
        {
            int selectedIndex = -1;

            // 解析参数
            if (parameter is int index)
            {
                selectedIndex = index;
            }
            else if (parameter is string str && int.TryParse(str, out int parsed))
            {
                selectedIndex = parsed;
            }

            if (selectedIndex < 0) return;

            UpdateFillTypeProperties(selectedIndex);

            // 重置 DirectionTypeIndex 为有效的默认值
            if (selectedIndex == 2 && Model.DirectionTypeIndex > 5)
                Model.DirectionTypeIndex = 0;
            else if (selectedIndex == 3 && Model.DirectionTypeIndex > 3)
                Model.DirectionTypeIndex = 0;

            // 根据选中的填充类型执行相应操作
            switch (selectedIndex)
            {
                case 0:
                    // Z字型单向
                    //System.Diagnostics.Debug.WriteLine("选中：Z字型单向");
                    OnFillTypeZShape();
                    break;
                case 1:
                    // 弓字型双向
                    //System.Diagnostics.Debug.WriteLine("选中：弓字型双向");
                    OnFillTypeBowShape();
                    break;
                case 2:
                    // 回字型
                    //System.Diagnostics.Debug.WriteLine("选中：回字型");
                    OnFillTypeRectangle();
                    break;
                case 3:
                    // 螺旋型
                    //System.Diagnostics.Debug.WriteLine("选中：螺旋型");
                    OnFillTypeSpiral();
                    break;
                default:
                    //System.Diagnostics.Debug.WriteLine($"未处理的选择索引: {selectedIndex}");
                    break;
            }
        }

        private void OnFillTypeZShape()
        {
            // Z字型单向的业务逻辑
            // 例如：设置填充算法参数、重新计算填充线等
            MarginEnable = true;
            RingSpacingEnable = false;
            LineSpacingEnable = true;
            StartAngleEnable = true;
            ExtensionEnable = true;

            AverageDistributeEnable = true;
            InternalRingsEnable = false;
            DirectionTypeIndexEnable = false;
            RelativeToAngleEnable = true;
            ReverseFillLineEnable = true;
        }

        private void OnFillTypeBowShape()
        {
            // 弓字型双向的业务逻辑
            MarginEnable = true;
            RingSpacingEnable = false;
            LineSpacingEnable = true;
            StartAngleEnable = true;
            ExtensionEnable = true;

            AverageDistributeEnable = true;
            InternalRingsEnable = false;
            DirectionTypeIndexEnable = false;
            RelativeToAngleEnable = true;
            ReverseFillLineEnable = true;
        }

        private void OnFillTypeRectangle()
        {
            // 回字型的业务逻辑
            MarginEnable = true;
            RingSpacingEnable = true;
            LineSpacingEnable = false;
            StartAngleEnable = false;
            ExtensionEnable = false;

            AverageDistributeEnable = false;
            InternalRingsEnable = false;
            DirectionTypeIndexEnable = true;
            RelativeToAngleEnable = false;
            ReverseFillLineEnable = true;
        }

        private void OnFillTypeSpiral()
        {
            // 螺旋型的业务逻辑
            MarginEnable = true;
            RingSpacingEnable = true;
            LineSpacingEnable = false;
            StartAngleEnable = false;
            ExtensionEnable = false;

            AverageDistributeEnable = false;
            InternalRingsEnable = true;
            DirectionTypeIndexEnable = true;
            RelativeToAngleEnable = false;
            ReverseFillLineEnable = false;
        }


        private bool ValidationFillParam(FillParameter fill)
        {
            if (fill == null)
            {
                MessageBox.Show("填充参数为null!");
                return false;
            }

            bool result = false;
            // 根据选中的填充类型执行相应操作
            switch (Model.FillTypeIndex)
            {
                case 0:
                    // Z字型单向
                    System.Diagnostics.Debug.WriteLine("选中：Z字型单向");
                    result = ValidationFillTypeZShape(fill);
                    break;
                case 1:
                    // 弓字型双向
                    System.Diagnostics.Debug.WriteLine("选中：弓字型双向");
                    result = ValidationFillTypeBowShape(fill);
                    break;
                case 2:
                    // 回字型
                    System.Diagnostics.Debug.WriteLine("选中：回字型");
                    result = ValidationFillTypeRectangle(fill);
                    break;
                case 3:
                    // 螺旋型
                    System.Diagnostics.Debug.WriteLine("选中：螺旋型");
                    result = ValidationFillTypeSpiral(fill);
                    break;
                default:
                    System.Diagnostics.Debug.WriteLine($"未处理的选择索引: {Model.FillTypeIndex}");
                    break;
            }

            return result;
        }

        private bool ValidationFillTypeZShape(FillParameter fill)
        {
            if (fill.Margin < 0)
            {
                MessageBox.Show("边距参数小于0!");
                return false;
            }
            if (fill.LineSpacing < 0)
            {
                MessageBox.Show("间距小于0!");
                return false;
            }

            return true;
        }

        private bool ValidationFillTypeBowShape(FillParameter fill)
        {
            if (fill.Margin < 0)
            {
                MessageBox.Show("边距参数小于0!");
                return false;
            }
            if (fill.LineSpacing < 0)
            {
                MessageBox.Show("间距小于0!");
                return false;
            }

            return true;
        }

        private bool ValidationFillTypeRectangle(FillParameter fill)
        {
            if (fill.Margin < 0)
            {
                MessageBox.Show("边距参数小于0!");
                return false;
            }
            if (fill.RingSpacing < 0)
            {
                MessageBox.Show("圈距小于0!");
                return false;
            }

            return true;
        }

        private bool ValidationFillTypeSpiral(FillParameter fill)
        {
            if (fill.Margin < 0)
            {
                MessageBox.Show("边距参数小于0!");
                return false;
            }
            if (fill.RingSpacing < 0)
            {
                MessageBox.Show("圈距小于0!");
                return false;
            }
            if (fill.InternalRings < 0)
            {
                MessageBox.Show("内圈数小于0!");
                return false;
            }

            return true;
        }

        [RelayCommand]
        private void Update()
        {
            if (Model is not FillParameter fill) return;
            if (!ValidationFillParam(fill)) return;

            // 填充参数已由 Refill 写入 DrawingHatch.HatchParamInfo，无需额外缓存
            var hatchIds = _drawingService.Shapes.Refill(ConvertParam(fill));
            if (!hatchIds.IsSuccess)
            {
                MessageBox.Show(hatchIds.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        [RelayCommand]
        private async Task ShowDialog()
        {
            var settings = await _dialogService.ShowDialogAsync<DashSettingsViewModel, DashSettingParameter>(vm =>
            {
                vm.Title = "虚线设定";
                vm.ConfirmText = "确定";
                vm.CancelText = "取消";
                vm.WindowHeight = 600;
            });

            if (settings != null)
            {
                _dashSettingsViewModel.ApplyCommand.Execute(settings);
            }
        }


        /// <summary>
        /// Apply All 使用：将当前 UI 参数应用到选中的填充对象。
        /// 参数由 Refill 写入 DrawingHatch.HatchParamInfo，无需额外缓存。
        /// </summary>
        private async Task ApplyParametersToSelectionsAsync()
        {
            if (Model is not FillParameter fill) return;
            if (!ValidationFillParam(fill)) return;

            var hatchIdResult = _drawingService.Shapes.Refill(ConvertParam(fill));
            if (hatchIdResult == null || !hatchIdResult.IsSuccess)
            {
                if (hatchIdResult != null)
                    MessageBox.Show(hatchIdResult.Message);
            }

            await Task.CompletedTask;
        }
        protected override Task ExecuteApplyAsync()
        {
            if (Model is not FillParameter fill)
                return Task.CompletedTask;

            if (!ValidationFillParam(fill)) return Task.CompletedTask;
            // 填充参数由 Fill → CreateFromTargets 自动写入 DrawingHatch.HatchParamInfo
            var hatchId = _drawingService.Shapes.Fill(ConvertParam(fill));
            if (!hatchId.IsSuccess)
            {
                MessageBox.Show(hatchId.Message);
            }

            return Task.CompletedTask;
        }

        protected override Task AfterApplyAsync(FillParameter parameter)
        {
            return Task.CompletedTask;
        }

        private HatchParamDto ConvertParam(FillParameter fill)
        {
            return new HatchParamDto
            {
                FillColor = fill.FillColor,
                FillStyleIndex = fill.FillStyleIndex,

                Margin = fill.Margin,
                RingSpacing = fill.RingSpacing > 0 ? (float)fill.RingSpacing : 0.1f,
                LineSpacing = fill.LineSpacing > 0 ? (float)fill.LineSpacing : 0.1f,
                Count = fill.Count,
                StartAngle = fill.StartAngle,
                IncrementalAngle = fill.IncrementalAngle,
                Extension = fill.Extension,

                FillTypeIndex = fill.FillTypeIndex,
                AverageDistribute = fill.AverageDistribute,
                InternalRings = fill.InternalRings,
                DirectionTypeIndex = fill.DirectionTypeIndex,
                RelativeToAngle = fill.RelativeToAngle,
                ReverseFillLine = fill.ReverseFillLine,
            };
        }
    }
}
