namespace DrSoft.Drawing.Event.Tool
{
    /// <summary>
    /// 节点编辑主模式下的子模式。
    /// Move 为一次性动作，不作为持续子模式参与互斥。
    /// </summary>
    public enum NodeEditSubMode
    {
        None = 0,
        Add = 1,
        Delete = 2,
        Separate = 3,
        Extend = 4,
        Connect = 5,
        Select = 6,
    }
}
