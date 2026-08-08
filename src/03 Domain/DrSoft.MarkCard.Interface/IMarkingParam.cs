using DrSoft.Drawing.Model;
using DrSoft.MarkCard.Model;

namespace DrSoft.MarkCard.Interface
{
    public interface IMarkingParam
    {
        /// <summary>
        /// 加工参数绑定
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        /// <param name="entityIds">图形ID/群组ID/组合ID列表</param>
        /// <param name="param">加工参数对象</param>
        Task BindParametersAsync(int canvasId, List<int> entityIds, IList<ParameterBase> param);

        /// <summary>
        /// 获取指定画布所有绑定的加工参数，返回一个字典，其中键是图形ID/群组ID/组合ID，值是对应的加工参数对象。
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        /// <returns></returns>
        Dictionary<int, IList<ParameterBase>> GetParameters(int canvasId);

        /// <summary>
        /// 设置指定画布的所有参数（整体替换）
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        /// <param name="pairs">参数字典</param>
        void SetParameters(int canvasId, Dictionary<int, IList<ParameterBase>> pairs);

        /// <summary>
        /// 获取加工参数对象
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        /// <param name="entityId">图形ID/群组ID/组合ID</param>
        /// <returns>加工参数对象</returns>
        IList<ParameterBase>? GetParameters(int canvasId, int entityId);


        Task<T> GetParameterAsync<T>(int canvasId, int item) where T : ParameterBase, new();

        /// <summary>
        /// 删除指定画布的所有参数
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        bool RemoveCanvasParameters(int canvasId);

        /// <summary>
        /// 删除指定图形的加工参数
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        /// <param name="entityId">图形ID</param>
        bool RemoveEntityParameters(int canvasId, int entityId);

        /// <summary>
        /// 批量删除指定图形的加工参数
        /// </summary>
        /// <param name="canvasId">画布ID</param>
        /// <param name="entityIds">图形ID列表</param>
        void RemoveEntityParameters(int canvasId, IEnumerable<int> entityIds);

        /// <summary>
        /// 将群组和组合中的子节点的绑定参数进行扁平化处理，生成一个新的字典，
        /// 其中每个顶层实体（非被 Group 包含的子节点）都对应一个最终的 ProcessParam。
        /// </summary>
        /// <param name="entities"></param>
        /// <param name="bindings"></param>
        /// <returns></returns>
        Dictionary<int, IList<ParameterBase>> FlattenParameters(
            ICanvasData canvas,
            Dictionary<int, IList<ParameterBase>> bindings);

        /// <summary>
        /// 获取全局加工参数（作为新建图形的默认参数模板）
        /// </summary>
        IList<ParameterBase> GetGlobalParameters();

        /// <summary>
        /// 设置全局加工参数
        /// </summary>
        void SetGlobalParameters(IList<ParameterBase> parameters);
    }
}
