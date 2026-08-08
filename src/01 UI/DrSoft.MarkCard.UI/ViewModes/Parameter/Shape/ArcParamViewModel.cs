using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class ArcParamViewModel : BaseParamViewModel<ArcParameter>
    {

        [ObservableProperty] private double centerX;
        [ObservableProperty] private double centerY;

        // 半径
        [ObservableProperty] private double radiusX;
        [ObservableProperty] private double radiusY;
        //[ObservableProperty] private bool isEqualRadius = false;

        public bool IsEqualRadius
        {
            get => Model.IsEqualRadius;
            set
            {
                Model.IsEqualRadius = value;
                if (value)
                {
                    RadiusY = RadiusX;
                }
                OnPropertyChanged(nameof(IsEqualRadius));
            }
        }
        [ObservableProperty] private double startX;
        [ObservableProperty] private double startY;
        [ObservableProperty] private double startAngle;


        [ObservableProperty] private double middleX;
        [ObservableProperty] private double middleY;
        [ObservableProperty] private double middleAngle;

        [ObservableProperty] private double endX;
        [ObservableProperty] private double endY;
        [ObservableProperty] private double endAngle;
        private IEventBus? _eventBus => EventBus.Instance;
        public ArcParamViewModel()
        {
            //EventBus.Instance.Subscribe<ParaSaveEvent>(e => { if (e.ParaSaveType == ParaSaveType.Element && e.Trigger && e.TriggerTitle.Equals("圆弧")) _ = base.ApplyAsync(); });

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

        protected override Task ExecuteApplyAsync()
        {
            if (IsEqualRadius)
            {
                RadiusY = RadiusX;
            }

            Model.CenterX = CenterX;
            Model.CenterY = CenterY;
            Model.RadiusX = RadiusX;
            Model.RadiusY = RadiusY;
            Model.IsEqualRadius = IsEqualRadius;
            Model.StartAngle = StartAngle;
            Model.EndAngle = EndAngle;

            _drawingService.Shapes.AdjustArc(
                CenterX, CenterY,
                RadiusX, RadiusY,
                StartAngle,EndAngle);

            return Task.CompletedTask;
        }

        public override Task<ArcParameter> LoadParameterAsync()
        {
            var result = _drawingService.Shapes.GetSelections();
            if (result.IsSuccess && result.Value != null && result.Value.Count > 0)
            {
                var shapeData = result.Value[0];

                if (shapeData is not IArcShapeData arc) return Task.FromResult(Model);
                CenterX = Model.CenterX = arc.CircumcircleCenterX;
                CenterY = Model.CenterY = arc.CircumcircleCenterY;

                RadiusX = Model.RadiusX = arc.RadiusX;
                RadiusY = Model.RadiusY = arc.RadiusY;
                IsEqualRadius = Math.Abs(RadiusX - RadiusY) < 0.001;

                StartAngle = Model.StartAngle = arc.StartAngle;
                EndAngle = Model.EndAngle = arc.SweepAngle;

                //StartX = Model.StartX = arc.OutlinePoints[0].X;
                //StartY = Model.StartY = arc.OutlinePoints[0].Y;
                //StartAngle = Model.StartAngle = arc.StartAngle;

                //MiddleX = Model.MiddleX = arc.OutlinePoints[1].X;
                //MiddleY = Model.MiddleY = arc.OutlinePoints[1].Y;

                //EndX = Model.EndX = arc.OutlinePoints[2].X;
                //EndY = Model.EndY = arc.OutlinePoints[2].Y;
                //EndAngle = Model.EndAngle = arc.SweepAngle;
            }   
            
            return Task.FromResult(Model);
        }
    }
}