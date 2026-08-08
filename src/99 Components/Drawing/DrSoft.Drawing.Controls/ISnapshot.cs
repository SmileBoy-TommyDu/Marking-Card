namespace DrSoft.Drawing.Controls
{
    /// <summary>
    /// 图形状态快照接口，用于撤销/重做。
    /// </summary>
    public interface IShapeMemento
    {
        /// <summary>
        /// 将快照中的状态恢复到关联的图形上。
        /// </summary>
        void Restore();
    }

    /// <summary>
    /// 支持状态快照的图形接口。
    /// 实现此接口的图形可通过 <see cref="CaptureSnapshot"/> 捕获当前状态，
    /// 并通过 <see cref="IShapeMemento.Restore"/> 恢复到捕获时的状态。
    /// </summary>
    public interface ISnapshotable
    {
        /// <summary>
        /// 捕获图形当前状态的快照。
        /// </summary>
        IShapeMemento CaptureSnapshot();
    }
}
