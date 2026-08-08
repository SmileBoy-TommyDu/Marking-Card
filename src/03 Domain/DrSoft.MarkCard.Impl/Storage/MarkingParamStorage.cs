using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.Impl.Storage
{
    /// <summary>
    /// 打标参数存储类
    /// 结构：画布ID (int) → 图形ID (int) → 参数列表
    /// </summary>
    public class MarkingParamStorage
    {
        // 两层字典：CanvasId -> EntityId -> Parameters
        private readonly Dictionary<int, Dictionary<int, IList<ParameterBase>>> _storage = new();

        #region 画布维度操作

        /// <summary>
        /// 获取整个画布的所有参数（返回指定画布下所有图形的参数）
        /// </summary>
        public Dictionary<int, IList<ParameterBase>> GetCanvasParameters(int canvasId)
        {
            if (!_storage.TryGetValue(canvasId, out var entityDict))
            {
                return new Dictionary<int, IList<ParameterBase>>();
            }
            return new Dictionary<int, IList<ParameterBase>>(entityDict);
        }

        /// <summary>
        /// 设置整个画布的参数（整体替换）
        /// </summary>
        public void SetCanvasParameters(int canvasId, Dictionary<int, IList<ParameterBase>> entityParameters)
        {
            if (entityParameters == null)
            {
                _storage.Remove(canvasId);
                return;
            }
            _storage[canvasId] = new Dictionary<int, IList<ParameterBase>>(entityParameters);
        }

        /// <summary>
        /// 删除整个画布的所有参数
        /// </summary>
        public bool RemoveCanvas(int canvasId)
        {
            return _storage.Remove(canvasId);
        }

        /// <summary>
        /// 清空画布下所有图形的参数
        /// </summary>
        public void ClearCanvas(int canvasId)
        {
            if (_storage.TryGetValue(canvasId, out var entityDict))
            {
                entityDict.Clear();
            }
        }

        /// <summary>
        /// 判断画布是否存在
        /// </summary>
        public bool HasCanvas(int canvasId)
        {
            return _storage.ContainsKey(canvasId);
        }

        /// <summary>
        /// 获取所有画布ID
        /// </summary>
        public IEnumerable<int> GetAllCanvasIds()
        {
            return _storage.Keys;
        }

        #endregion

        #region 图形维度操作

        /// <summary>
        /// 获取指定画布中指定图形的参数
        /// </summary>
        public IList<ParameterBase>? GetEntityParameters(int canvasId, int entityId)
        {
            if (_storage.TryGetValue(canvasId, out var entityDict) &&
                entityDict.TryGetValue(entityId, out var parameters))
            {
                return parameters;
            }
            return null;
        }

        /// <summary>
        /// 获取参数，如果不存在则通过工厂方法创建并存储
        /// </summary>
        public IList<ParameterBase> GetOrCreateEntityParameters(
            int canvasId,
            int entityId,
            Func<IList<ParameterBase>> factory)
        {
            EnsureCanvasExists(canvasId);
            var entityDict = _storage[canvasId];

            if (!entityDict.TryGetValue(entityId, out var parameters))
            {
                parameters = factory();
                entityDict[entityId] = parameters;
            }
            return parameters;
        }

        /// <summary>
        /// 添加或更新图形参数
        /// </summary>
        public void AddOrUpdateEntity(int canvasId, int entityId, IList<ParameterBase> parameters)
        {
            EnsureCanvasExists(canvasId);
            _storage[canvasId][entityId] = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>
        /// 删除指定图形的参数
        /// </summary>
        public bool RemoveEntity(int canvasId, int entityId)
        {
            if (_storage.TryGetValue(canvasId, out var entityDict))
            {
                return entityDict.Remove(entityId);
            }
            return false;
        }

        /// <summary>
        /// 判断画布中是否包含指定图形的参数
        /// </summary>
        public bool HasEntity(int canvasId, int entityId)
        {
            return _storage.TryGetValue(canvasId, out var entityDict) &&
                   entityDict.ContainsKey(entityId);
        }

        /// <summary>
        /// 获取画布下所有图形ID
        /// </summary>
        public IEnumerable<int> GetEntityIds(int canvasId)
        {
            if (_storage.TryGetValue(canvasId, out var entityDict))
            {
                return entityDict.Keys;
            }
            return Enumerable.Empty<int>();
        }

        /// <summary>
        /// 获取画布下图形数量
        /// </summary>
        public int GetEntityCount(int canvasId)
        {
            if (_storage.TryGetValue(canvasId, out var entityDict))
            {
                return entityDict.Count;
            }
            return 0;
        }

        #endregion

        #region 批量操作

        /// <summary>
        /// 批量添加或更新图形参数
        /// </summary>
        public void AddOrUpdateEntities(int canvasId, Dictionary<int, IList<ParameterBase>> entities)
        {
            if (entities == null) throw new ArgumentNullException(nameof(entities));

            EnsureCanvasExists(canvasId);
            var entityDict = _storage[canvasId];

            foreach (var kvp in entities)
            {
                entityDict[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// 批量删除图形参数
        /// </summary>
        public void RemoveEntities(int canvasId, IEnumerable<int> entityIds)
        {
            if (!_storage.TryGetValue(canvasId, out var entityDict))
                return;

            foreach (var entityId in entityIds)
            {
                entityDict.Remove(entityId);
            }
        }

        /// <summary>
        /// 清空所有数据
        /// </summary>
        public void ClearAll()
        {
            _storage.Clear();
        }

        /// <summary>
        /// 获取所有数据（深拷贝）
        /// </summary>
        public Dictionary<int, Dictionary<int, IList<ParameterBase>>> GetAll()
        {
            var result = new Dictionary<int, Dictionary<int, IList<ParameterBase>>>();
            foreach (var canvasKvp in _storage)
            {
                result[canvasKvp.Key] = new Dictionary<int, IList<ParameterBase>>(canvasKvp.Value);
            }
            return result;
        }

        #endregion

        #region Private Methods

        private void EnsureCanvasExists(int canvasId)
        {
            if (!_storage.ContainsKey(canvasId))
            {
                _storage[canvasId] = new Dictionary<int, IList<ParameterBase>>();
            }
        }

        #endregion
    }
}
