

using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.Model
{

    public abstract record ParameterBase
    {
    }

    public interface IMarkingParameter
    {
        
    }

    /// <summary>
    /// 图形参数基类，所有具体图形参数都应继承此类
    /// </summary>
    public abstract record ShapeParameterBase : ParameterBase
    {
        /// <summary>
        /// 图形类型
        /// </summary>
        public abstract ShapeType Type { get; }
    }

    public interface IShapeParameter
    {
        public ShapeType Type { get; }
    }

    public record ShapeParameter : ShapeParameterBase
    {
        public override ShapeType Type => throw new NotImplementedException();
    }
}
