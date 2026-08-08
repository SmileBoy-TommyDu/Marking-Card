using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.UI.UserControls;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class OutlineParamViewModel : BaseParamViewModel<OutlineParameter>
    {
        private readonly IDialogService _dialogService;
        private readonly FillParamViewModel _fillParamViewModel;

        public OutlineParamViewModel(IDialogService dialogService, FillParamViewModel fillParamViewModel)
        {
            _dialogService = dialogService;
            _fillParamViewModel = fillParamViewModel;

            // 订阅参数保存事件，应用外框参数到选中图形
            EventBus.Instance.Subscribe<ParaSaveEvent>(e =>
            {
                if (e.ParaSaveType == ParaSaveType.Element && e.Trigger)
                    _ = ApplyAsync();
            });

            // 撤销 / 重做后重新从 IShapeData 加载外框参数，保证 UI 与实际状态一致
            EventBus.Instance.Subscribe<CanvasChangedEvent>(data =>
            {
                if (data.ChangeType == CanvasChangeType.Command)
                    _ = LoadParameterAsync();
            });
        }

        #region 绑定属性

        private Visibility _buttonVisibility;
        public Visibility ButtonVisibility
        {
            get => _buttonVisibility;
            set
            {
                _buttonVisibility = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 外框颜色（十六进制），用于 UI 绑定。
        /// 仅更新 Model，套用后才生效。
        /// </summary>
        public string OutlineColor
        {
            get => Model.OutlineColor;
            set
            {
                if (Model.OutlineColor == value) return;
                Model.OutlineColor = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 外框样式索引（0=实线, 1=短虚线, 2=点虚线, 3=无外框），用于 UI 绑定。
        /// 仅更新 Model，套用后才生效。
        /// </summary>
        public int OutlineStyleIndex
        {
            get => Model.OutlineStyleIndex;
            set
            {
                if (Model.OutlineStyleIndex == value) return;
                Model.OutlineStyleIndex = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 命令

        [RelayCommand]
        private async Task ShowDialog()
        {
            var fillParam = await _dialogService.ShowDialogAsync<FillParamViewModel, FillParameter>(vm =>
            {
                vm.Title = "创建填充";
                vm.ConfirmText = "创建";
                vm.CancelText = "取消";
                vm.WindowHeight = 420;
            });

            if (fillParam != null)
            {
                _fillParamViewModel.ApplyCommand.Execute(fillParam);
            }
        }

        [RelayCommand]
        private void BorderClick(object parameter)
        {
            if (Model is not OutlineParameter outline) return;
            var dlg = new ColorPickerDialog() { Owner = Application.Current.MainWindow };
            if (dlg.ShowDialog() == true)
            {
                var myColor = dlg.SelectedColor.ToString();
                if (string.IsNullOrEmpty(myColor)) return;
                OutlineColor = myColor;  // 通过属性 setter 更新 Model 并触发 OnPropertyChanged
            }
        }

        /// <summary>
        /// 反向雕刻
        /// </summary>
        [RelayCommand]
        private async Task ReverseEngraving()
        {
            var param = await _dialogService.ShowDialogAsync<ReverseEngravingViewModel, ReverseEngravingParameter>(vm =>
            {
                vm.Title = "反向雕刻";
                vm.WindowHeight = 260;
            });

            if (param != null)
            {
                
            }
        }

        #endregion

        #region 重写基类方法
        protected override Task ExecuteApplyAsync()
        {
            // 外框颜色应用
            if (Model is not OutlineParameter outline) return Task.CompletedTask; ;
            // 无外框时传入 null 让图形回退到图层颜色
            _drawingService.Shapes.SetOutlineStyle(outline.OutlineColor, outline.OutlineStyleIndex);
            return Task.CompletedTask;
        }

        public override async Task<OutlineParameter> LoadParameterAsync()
        {
            var result = _drawingService.Shapes.GetSelections();
            if (!result.IsSuccess || result.Value == null || result.Value.Count == 0)
                return Model;

            var shapeData = result.Value[0];

            // DrawingColor? → 十六进制字符串，通过属性 setter 更新 Model 并触发 UI 通知
            OutlineColor = shapeData.OutlineColor.HasValue
                ? string.Format("#{0:X2}{1:X2}{2:X2}", shapeData.OutlineColor.Value.R, shapeData.OutlineColor.Value.G, shapeData.OutlineColor.Value.B)
                : "#000000";

            // OutlineStyle 枚举 → int 索引（与 OutlineStyleIndex 语义一致），通过属性 setter 触发通知
            OutlineStyleIndex = (int)shapeData.OutlineStyle;

            return Model;
        }

        #endregion
    }
}
