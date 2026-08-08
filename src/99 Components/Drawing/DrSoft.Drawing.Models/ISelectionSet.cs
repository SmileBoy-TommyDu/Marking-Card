namespace DrSoft.Drawing.Model
{
    public interface ISelectionSet : IReadOnlyList<IShape>, ITransformService, IBoundable
    {
        ISelectionSet Transformables { get; }
    }
}
