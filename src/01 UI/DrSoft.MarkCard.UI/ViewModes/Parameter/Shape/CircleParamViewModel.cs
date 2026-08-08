using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class CircleParamViewModel : BaseParamViewModel<CircleParameter>
    {

        private IEventBus? _eventBus => EventBus.Instance;

        [ObservableProperty] private double centerX;
        [ObservableProperty] private double centerY;
        [ObservableProperty] private double radiusY;

        private double radiusX;
        public double RadiusX
        {
            get => radiusX;
            set
            {
                // Use ObservableObject.SetProperty to raise change notifications and avoid generated code issues
                if (SetProperty(ref radiusX, value))
                {
                    if (Model.IsEqualRadius)
                    {
                        RadiusY = value;
                    }
                }
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public bool IsEqual
        {
            get => Model.IsEqualRadius;
            set
            {
                Model.IsEqualRadius = value;
                if (value)
                {
                    RadiusY = RadiusX;
                }
                OnPropertyChanged(nameof(IsEqual));
            }
        }
        public CircleParamViewModel()
        {
            EventBus.Instance.Subscribe<ParaSaveEvent>(e => { if (e.ParaSaveType == ParaSaveType.Element && e.Trigger && e.TriggerTitle.Equals("圆")) _ = ApplyAsync(); });
            _eventBus?.Subscribe<CanvasChangedEvent>(data =>
            {
                switch (data.ChangeType)
                {
                    case CanvasChangeType.Command:
                        LoadParameterAsync();
                        break;
                    default:
                        break;
                }
            }, replayLast: true);
        }

        [RelayCommand]
        private void GoCenter()
        {
            CenterX = 0;
            CenterY = 0;
            Model.CenterX = CenterX;
            Model.CenterY = CenterY;
            Model.RadiusX = RadiusX;
            Model.RadiusY = RadiusY;
            _drawingService.Shapes.AdjustCircle((float)Model.CenterX, (float)Model.CenterY, (float)Model.RadiusX, (float)Model.RadiusY);

            /* 切换对齐基准点逻辑 */
        }

        protected override Task ExecuteApplyAsync()
        {
            Model.CenterX = CenterX;
            Model.CenterY = CenterY;
            Model.RadiusX = RadiusX;
            Model.RadiusY = RadiusY;
            Model.IsEqualRadius = IsEqual;

            _drawingService.Shapes.AdjustCircle((float)Model.CenterX, (float)Model.CenterY, (float)Model.RadiusX, (float)Model.RadiusY);

            return Task.CompletedTask;
        }

        public override Task<CircleParameter> LoadParameterAsync()
        {
            var result = _drawingService.Shapes.GetSelections();
            if (result.IsSuccess && result.Value != null && result.Value.Count > 0)
            {
                var shapeData = result.Value[0];
                if (shapeData is not ICircleShapeData circle) return Task.FromResult(Model);

                CenterX = Model.CenterX = circle.X;
                CenterY = Model.CenterY = circle.Y;
                RadiusX = Model.RadiusX = circle.RadiusX;
                RadiusY = Model.RadiusY = circle.RadiusY;
                IsEqual = circle.RadiusX== circle.RadiusY;
            }

            return Task.FromResult(Model);
        }
    }
}
