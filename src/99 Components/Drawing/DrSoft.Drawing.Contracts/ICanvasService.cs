using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Contracts
{
    public interface ICanvasService
    {
        #region Interfaces
        // 画布生命周期
        int AddCanvas(string title = "未命名");
        void RenameCanvas(string NewName);
        void SwitchCanvas();
        void SwitchCanvas(int canvasId);
        void RenameCanvas(int canvasId, string newTitle);

        // 文件操作（作用于当前激活画布）
        int? Open(CanvasSnapshotDto snapShotDto);

        IEnumerable<CanvasSnapshotDto> Save(string filePath);

        void Close(int canvasId);

        /// <summary>加载文件</summary>
        CanvasStorageDocumentDto LoadFile(string filePath);
        void SaveFile(
            string filePath,
            IReadOnlyDictionary<int, byte[]>? layerPayloads = null,
            IReadOnlyDictionary<string, byte[]>? extensionPayloads = null);

        // 获取当前激活画布快照（DTO，用于文件存取）
        CanvasSnapshotDto GetActiveSnapshot();

        /// <summary>
        /// 获取当前活动画布的只读数据视图。
        /// <para>
        /// 零拷贝，直接返回画布内部数据的只读接口引用，无需 DTO 转换。
        /// 打标卡应通过此方法获取图形数据，而非 <see cref="GetActiveSnapshot"/>。
        /// </para>
        /// <para>
        /// 注意：打标作业执行期间画布不应被编辑（调用方负责协调），
        /// 如需线程安全快照请使用 <see cref="GetActiveSnapshot"/>。
        /// </para>
        /// </summary>
        ICanvasData? GetActiveCanvasData();

        /// <summary>
        /// 获取当前活动画布选中的图形
        /// </summary>
        SelectedSharpsDto GetSelectedSharps();

        // 导入DXF
        Task<bool> ImportDxfAsync(string filePath);

        // 导出DXF
        Task<bool> ExportDxfAsync(string filePath);
        #endregion

        #region 画布的系统修改
        /// <summary>
        /// 改变画布中心点
        /// </summary>
        void UpdateCanvasCenterPoint(double X, double Y);
        /// <summary>
        /// 设置画布的宽高
        /// </summary>
        void SetMachineBounds(float width, float height);
        /// <summary>
        /// 设置格点的宽高
        /// </summary>
        void SetGridSize(float width, float height);
        /// <summary>
        /// 设置微调的距离
        /// </summary>
        void SetMicroMove(float MicroMoveX, float MicroMoveY);
        /// <summary>
        /// 获取画布相关参数
        /// </summary>
        CanvasParaModelDto GetCanvasPara();
        #endregion
    }
}

