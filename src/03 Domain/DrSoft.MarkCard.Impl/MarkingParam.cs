using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Impl.Storage;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.EditMenu;
using DrSoft.MarkCard.Model.Parameter;

namespace DrSoft.MarkCard.Impl
{
    public class MarkingParam : IMarkingParam
    {
        private readonly MarkingParamStorage _storage = new();

        /// <summary>
        /// 全局加工参数，作为新建图形的默认参数模板。
        /// 初始值为硬编码默认值，每次修改图形参数时同步更新。
        /// </summary>
        private IList<ParameterBase> _globalParameters;

        public MarkingParam()
        {
            _globalParameters = CreateHardcodedDefaultParameters();
        }

        #region 图形参数绑定

        public async Task BindParametersAsync(int canvasId, List<int> entityIds, IList<ParameterBase> parameters)
        {
            foreach (var entityId in entityIds)
            {
                var existingParams = _storage.GetOrCreateEntityParameters(canvasId, entityId, CreateDefaultParameters);
                
                foreach (var param in parameters)
                {
                    var type = param.GetType();
                    var parameter = existingParams.FirstOrDefault(p => p.GetType() == type);
                    if (parameter != null)
                    {
                        existingParams.Remove(parameter);
                        existingParams.Add(param with { });
                    }
                    else
                    {
                        existingParams.Add(param with { });
                    }
                }
            }

            // 同步更新全局加工参数：以最后绑定的参数为准
            SyncGlobalParameters(parameters);

            await Task.CompletedTask;
        }

        /// <summary>
        /// 创建默认的 A, B, C, D 参数列表。
        /// 优先使用全局加工参数作为模板，若全局参数不存在则回退到硬编码默认值。
        /// </summary>
        private List<ParameterBase> CreateDefaultParameters()
        {
            /*if (_globalParameters != null && _globalParameters.Count > 0)
            {
                // 复制全局参数作为新图形的默认值，
                // 但头尾延伸等一次性参数始终重置为 0，不继承上一次设置值。
                return _globalParameters.Select(p => p switch
                {
                    ExtendHeadTailSettingsModel => (ParameterBase)new ExtendHeadTailSettingsModel(),
                    _ => p with { }
                }).ToList();
            }*/
            return CreateHardcodedDefaultParameters();
        }

        /// <summary>
        /// 硬编码的默认参数列表，仅在首次初始化时使用
        /// </summary>
        private static List<ParameterBase> CreateHardcodedDefaultParameters()
        {
            return new List<ParameterBase>
            {
                new EngravingParameter(),
                new DelayParameter(),
                //new OutlineParameter(),
                //new MatrixCopyParameter()
            };
        }

        /// <summary>
        /// 将新绑定的参数同步到全局参数中。
        /// 同类型参数覆盖，不同类型追加，确保全局参数始终是最新的。
        /// </summary>
        private void SyncGlobalParameters(IList<ParameterBase> newParameters)
        {
            var globalList = _globalParameters.ToList();
            foreach (var param in newParameters)
            {
                var type = param.GetType();
                var existing = globalList.FirstOrDefault(p => p.GetType() == type);
                if (existing != null)
                {
                    globalList.Remove(existing);
                }
                globalList.Add(param with { });
            }
            _globalParameters = globalList;
        }

        public Dictionary<int, IList<ParameterBase>> GetParameters(int canvasId)
        {
            return _storage.GetCanvasParameters(canvasId);
        }

        public void SetParameters(int canvasId, Dictionary<int, IList<ParameterBase>> pairs)
        {
            _storage.SetCanvasParameters(canvasId, pairs);
        }

        public IList<ParameterBase>? GetParameters(int canvasId, int entityId)
        {
            var param = _storage.GetEntityParameters(canvasId, entityId);
            if (param == null)
            {
                param = CreateDefaultParameters();
                _storage.AddOrUpdateEntity(canvasId, entityId, param);
            }
            return param;
        }

        public async Task<T> GetParameterAsync<T>(int canvasId, int entityId) where T : ParameterBase, new()
        {
            var param = _storage.GetEntityParameters(canvasId, entityId);
            if (param?.OfType<T>().FirstOrDefault() is T p)
            {
                return p;
            }

            var newParam = new T();
            //var updatedList = param?.ToList() ?? new List<ParameterBase>();
            //updatedList.Add(newParam);
            //_storage.AddOrUpdateEntity(canvasId, entityId, updatedList);
            return newParam;
        }

        public bool RemoveCanvasParameters(int canvasId)
        {
            return _storage.RemoveCanvas(canvasId);
        }

        public bool RemoveEntityParameters(int canvasId, int entityId)
        {
            return _storage.RemoveEntity(canvasId, entityId);
        }

        public void RemoveEntityParameters(int canvasId, IEnumerable<int> entityIds)
        {
            _storage.RemoveEntities(canvasId, entityIds);
        }

        #endregion

        #region 全局加工参数

        public IList<ParameterBase> GetGlobalParameters()
        {
            return _globalParameters.Select(p => p with { }).ToList();
        }

        public void SetGlobalParameters(IList<ParameterBase> parameters)
        {
            _globalParameters = parameters?.Select(p => p with { }).ToList()
                ?? CreateHardcodedDefaultParameters();
        }

        #endregion

        #region 图形参数扁平化

        /// <summary>
        /// 收集所有叶子节点的加工参数。
        /// 由于套用时已将参数打平到末级图形，此处直接返回叶子节点已有的参数，
        /// 不再做父级→子级的继承合并。
        /// </summary>
        /// <param name="canvas">画布数据</param>
        /// <param name="parameters">已设置的图形参数关系（键 = UId）</param>
        /// <returns>叶子节点 UId → 参数列表</returns>
        public Dictionary<int, IList<ParameterBase>> FlattenParameters(
            ICanvasData canvas,
            Dictionary<int, IList<ParameterBase>> parameters)
        {
            var result = new Dictionary<int, IList<ParameterBase>>();

            foreach (var layer in canvas.Layers)
            {
                foreach (var shape in layer.Shapes)
                {
                    CollectLeafParams(shape, parameters, result);
                }
            }

            return result;
        }

        /// <summary>
        /// 递归遍历图形树，收集叶子节点的加工参数。
        /// 若叶子节点未套用参数（参数字典中不存在），则生成默认参数。
        /// </summary>
        private void CollectLeafParams(
        IShapeData shape,
        Dictionary<int, IList<ParameterBase>> parameters,
        Dictionary<int, IList<ParameterBase>> result,
        IList<ParameterBase> inheritedParams = null)
        {
            // 计算当前节点的有效参数：自身参数优先，缺失的类型从父级继承
            IList<ParameterBase> effectiveParams = inheritedParams;

            if (parameters.TryGetValue(shape.UId, out var ownParams) && ownParams != null && ownParams.Count > 0)
            {
                effectiveParams = MergeParams(ownParams, inheritedParams);
            }

            if (shape.ChildShapes != null && shape.ChildShapes.Count > 0)
            {
                foreach (var child in shape.ChildShapes)
                    CollectLeafParams(child, parameters, result, effectiveParams);
            }
            else
            {
                if (effectiveParams != null && effectiveParams.Count > 0)
                {
                    var markingParams = effectiveParams.Where(p => p is IMarkingParameter).ToList();
                    result[shape.UId] = markingParams.Count > 0
                        ? markingParams
                        : CreateDefaultParameters();
                }
                else
                {
                    // 未执行套用的叶子节点，生成默认加工参数
                    result[shape.UId] = CreateDefaultParameters();
                }
            }
        }

        /// <summary>
        /// 合并自身参数与父级参数：相同类型以自身为主，自身没有的类型继承父级，
        /// 保证合并后的参数个数不少于父级参数个数（自身可以更多，但不能更少）。
        /// </summary>
        private IList<ParameterBase> MergeParams(IList<ParameterBase> ownParams, IList<ParameterBase> parentParams)
        {
            if (parentParams == null || parentParams.Count == 0)
                return ownParams;

            // 以类型为 key，先放入父级参数，再用自身参数覆盖同类型项
            var merged = new Dictionary<Type, ParameterBase>();

            foreach (var p in parentParams)
                merged[p.GetType()] = p;

            foreach (var p in ownParams)
                merged[p.GetType()] = p; // 自身覆盖父级同类型

            return merged.Values.ToList();
        }

        #endregion
    }
}
