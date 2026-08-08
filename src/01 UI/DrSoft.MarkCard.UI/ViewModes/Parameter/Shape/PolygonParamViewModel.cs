using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class PolygonParamViewModel : BaseParamViewModel<PolygonParameter>
    {
        [ObservableProperty]
        private int sideCount;

        [ObservableProperty]
        private PolygonType subType;

        public PolygonParamViewModel()
        {
            EventBus.Instance.Subscribe<ParaSaveEvent>(e => { if (e.ParaSaveType == ParaSaveType.Element && e.Trigger && e.TriggerTitle.Equals("多边形")) _ = ApplyAsync(); });
            EventBus.Instance.Subscribe<CanvasChangedEvent>(data =>
            {
                switch (data.ChangeType)
                {
                    case CanvasChangeType.Command:
                    case CanvasChangeType.SelectSharps:
                        _ = LoadParameterAsync();
                        break;
                }
            }, replayLast: true);
        }

        protected override Task ExecuteApplyAsync()
        {
            _drawingService.Shapes.AdjustPolygon(SideCount, SubType);
            return Task.CompletedTask;
        }

        public override Task<PolygonParameter> LoadParameterAsync()
        {
            var result = _drawingService.Shapes.GetSelections();
            if (result.IsSuccess && result.Value != null && result.Value.Count > 0)
            {
                var shapeData = result.Value[0];
                if (shapeData is not IPolygonShapeData polygon) return Task.FromResult(Model);

                SideCount = polygon.SideCount;
                SubType = polygon.IsStar ? PolygonType.Star : PolygonType.Regular;
            }

            return Task.FromResult(Model);
        }
    }
}
