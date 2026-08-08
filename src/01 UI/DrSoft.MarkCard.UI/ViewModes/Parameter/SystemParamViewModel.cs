using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Model.Parameter;
using DrSoft.MarkCard.UI.UIConfig;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class SystemParamViewModel : BaseParamViewModel<SystemParam>
    {
        private CanvasSystemConfig canvasSystemConfig;
        public SystemParamViewModel() : base()
        {
            canvasSystemConfig = App.GetService<CanvasSystemConfig>();
            EventBus.Instance.Subscribe<ParaSaveEvent>(OnSaveAll);
        }
        private void OnSaveAll(ParaSaveEvent @event)
        {
            if (@event.ParaSaveType == ParaSaveType.Canvas && @event.Trigger)
            {
                SaveFun();
            }
        }

        protected override Task ExecuteApplyAsync()
        {
            SaveFun();
            return Task.CompletedTask;
        }
        private void SaveFun()
        {
            canvasSystemConfig.SystemParam = Model;
            canvasSystemConfig.SaveToFile();
        }
    }
}
