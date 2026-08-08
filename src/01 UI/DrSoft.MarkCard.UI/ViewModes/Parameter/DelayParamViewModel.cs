
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Model;


namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class DelayParamViewModel : BaseParamViewModel<DelayParameter>
    {
        public DelayParamViewModel()
        {
            EventBus.Instance.Subscribe<ParaSaveEvent>(e => { if (e.ParaSaveType == ParaSaveType.Element && e.Trigger) _ = ApplyAsync(); });
      
        }

        /// <summary>
        /// 套用逻辑：如果选中对象是容器（群组/组合/填充），
        /// </summary>
        protected override async Task ExecuteApplyAsync()
        {
            if (_service != null && RuntimeContext.Selections != null)
            {
                var leafIds = CollectLeafEntityIds();
                // 同时绑定到容器（显示用）和末级图形（加工用）
                var allIds = RuntimeContext.Selections.Union(leafIds).ToList();
                await _service.BindParametersAsync(
                    RuntimeContext.ActiveCanvasId,
                    allIds,
                    new List<ParameterBase> { Model }).ConfigureAwait(false);
            }
        }
    }
}
