using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Contracts;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Service;

namespace DrSoft.MarkCard.UI.ViewModes
{
    /// <summary>
    /// 参数视图模型基类
    /// T: 具体的参数模型类型 (如 EngravingParameter, FillParameter)
    /// </summary>
    public partial class BaseParamViewModel<T> : DialogViewModelBase<T> where T : ParameterBase, new()
    {
        protected override T? GetCancelResult()
        {
            return null;
        }

        protected override T? GetConfirmResult()
        {
            return Model;
        }

        [ObservableProperty]
        private T _model = new();

        protected readonly MarkParamService? _service;

        protected readonly IDrawingService _drawingService;

        protected BaseParamViewModel()
        {
            _service = App.GetService<MarkParamService>();
            _drawingService= App.GetService<IDrawingService>();

        }
   
        /// <summary>
        /// View 直接绑定这个 Command
        /// </summary>
        [RelayCommand]
        public async Task ApplyAsync()
        {
            try
            {
                // 1. 执行应用前逻辑
                await BeforeApplyAsync(Model);

                // 2. 执行核心应用逻辑
                await ExecuteApplyAsync();

                // 3. 执行应用后逻辑
                await AfterApplyAsync(Model);
            }
            catch (Exception ex)
            {
                // 可以在这里统一处理保存失败的情况
                EventBus.Instance.Publish(new ToastMessageEvent($"操作失败: {ex.Message}", ToastType.Error));
            }
        }

        /// <summary>
        /// 1. 参数应用之前事件 (子类可选重写)
        /// </summary>
        protected virtual async Task BeforeApplyAsync(T parameter)
        {
            await Task.CompletedTask;
        }

        /// <summary>
        /// 2. 核心应用逻辑 (子类如果完全改变应用方式可重写)
        /// </summary>
        protected virtual async Task ExecuteApplyAsync()
        {
            if (_service != null && RuntimeContext.Selections != null)
            {
                await _service.BindParametersAsync(RuntimeContext.ActiveCanvasId, RuntimeContext.Selections, new List<ParameterBase> { Model }).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Helper: merge given model into existing parameter lists for each entity and persist via service.
        /// This ensures saving one parameter type does not erase other parameter types attached to an entity.
        /// </summary>
        protected async Task BindParametersMergedAsync(IEnumerable<int> entityIds, ParameterBase model)
        {
            if (_service == null || entityIds == null) return;

            foreach (var id in entityIds)
            {
                var existing = _service.GetParameters(RuntimeContext.ActiveCanvasId, id);
                var combined = existing != null ? new List<ParameterBase>(existing) : new List<ParameterBase>();
                // Remove any existing parameter of the same concrete type as model
                combined.RemoveAll(p => p != null && p.GetType() == model.GetType());
                combined.Add(model);
                await _service.BindParametersAsync(RuntimeContext.ActiveCanvasId, new List<int> { id }, combined).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// 收集选中图形的末级图形 ID。
        /// 如果选中对象是容器（群组/组合/填充），递归展开到叶子节点；
        /// 如果是普通图形，直接使用其 ID。
        /// </summary>
        protected List<int> CollectLeafEntityIds()
        {
            var leafIds = new List<int>();

            if (_drawingService?.Shapes is IShapeQueryService queryService)
            {
                var result = queryService.GetSelections();
                if (result.IsSuccess && result.Value != null)
                {
                    foreach (var shape in result.Value)
                        CollectLeafIdsRecursive(shape, leafIds);
                }
            }

            // 如果无法获取图形数据，回退到使用 RuntimeContext.Selections
            return leafIds.Count > 0 ? leafIds : (RuntimeContext.Selections ?? new List<int>());
        }

        private static void CollectLeafIdsRecursive(IShapeData shape, List<int> leafIds)
        {
            if (shape.ChildShapes != null && shape.ChildShapes.Count > 0)
            {
                foreach (var child in shape.ChildShapes)
                    CollectLeafIdsRecursive(child, leafIds);
            }
            else
            {
                leafIds.Add(shape.UId);
            }
        }

        /// <summary>
        /// 3. 参数应用之后事件 (子类可选重写)
        /// </summary>
        protected virtual async Task AfterApplyAsync(T parameter)
        {
            //EventBus.Instance.Publish(new ToastMessageEvent("保存成功", ToastType.Info));
            await Task.CompletedTask;
        }

        public virtual async Task<T> LoadParameterAsync()
        {
            if (_service == null) return default!;
            if (RuntimeContext.Selections == null || RuntimeContext.Selections.Count == 0)
            {
                return Model;
            }

            var result = await _service.GetParameterAsync<T>(RuntimeContext.ActiveCanvasId, RuntimeContext.Selections[0]);
            if (result != null)
            {
                Model = result;
            }
            return Model;
        }
    }
}
