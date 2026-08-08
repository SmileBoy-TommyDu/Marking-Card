using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.UI.UIConfig
{
    /// <summary>
    /// 参数页签配置
    /// </summary>
    public class ElementTabConfig
    {
        /// <summary>
        /// 参数页签类型
        /// </summary>
        public enum ParameterTabType
        {
            Shape,      // 图形参数
            Engraving,  // 雕刻参数
            Delay,      // 延迟参数
            Outline,    // 外框/填满
            Fill,       // 填满参数
            MatrixCopy,  // 矩形复制
            LayerInputIO,  // 图层输入
            LayerOutputIO,  // 图层输出
            GroupParam,  // 图层/群组参数
        }

        /// <summary>
        /// 图形类型对应的参数页签配置
        /// Key: 图形类型，Value: 应该显示的参数页签列表
        /// </summary>
        private static readonly Dictionary<ShapeType, List<ParameterTabType>> ShapeTypeToTabsMap =
            new()
            {
                // 点、线、曲线、贝塞尔曲线 - 只显示雕刻、延迟、矩形复制
                {
                    ShapeType.Point,
                    new List<ParameterTabType> 
                    { 
                        // 从这里移除了 "Shape"（图形参数），因为点没有图形参数页面内容
                       //  ParameterTabType.Shape,
                        ParameterTabType.Engraving, 
                        ParameterTabType.Delay,
                        ParameterTabType.MatrixCopy 
                    }
                },
                {
                    ShapeType.Line,
                    new List<ParameterTabType> 
                    { 
                        ParameterTabType.Shape, 
                        ParameterTabType.Engraving, 
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy 
                    }
                },
                {
                    ShapeType.PolyLine,
                    new List<ParameterTabType> 
                    { 
                        ParameterTabType.Shape, 
                        ParameterTabType.Engraving, 
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy 
                    }
                },
                {
                    ShapeType.Bezier,
                    new List<ParameterTabType> 
                    {
                        // 贝塞尔曲线也不显示图形参数页签
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy 
                    }
                },

                // 矩形、圆、多边形、圆弧 - 显示所有页签
                {
                    ShapeType.Rectangle,
                    new List<ParameterTabType>
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                },
                {
                    ShapeType.Circle,
                    new List<ParameterTabType>
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                },
                {
                    ShapeType.Polygon,
                    new List<ParameterTabType>
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                },
                {
                    ShapeType.Arc,
                    new List<ParameterTabType>
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                },

                // 文本 - 显示雕刻、延迟、矩形复制
                {
                    ShapeType.Text,
                    new List<ParameterTabType> 
                    { 
                        ParameterTabType.Shape, 
                        ParameterTabType.Engraving, 
                        ParameterTabType.Delay, 
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy 
                    }
                },

                // 填满 - 显示所有页签
                {
                    ShapeType.Hatch,
                    new List<ParameterTabType>
                    {
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Fill,
                        ParameterTabType.MatrixCopy
                    }
                },

                // 组合、群组 - 只显示通用参数
                {
                    ShapeType.Combination,
                    new List<ParameterTabType> 
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                },
                {
                    ShapeType.Group,
                    new List<ParameterTabType> 
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                },
                {
                    // 任意曲线 - 显示图形参数、雕刻、延迟、矩形复制（不显示外框/填满，因为任意曲线没有明确的边界）
                    ShapeType.ArbitraryCurve,
                    new List<ParameterTabType>
                    {
                        ParameterTabType.Shape,
                        ParameterTabType.Engraving,
                        ParameterTabType.Delay,
                        ParameterTabType.Outline,
                        ParameterTabType.MatrixCopy
                    }
                }
            };

        /// <summary>
        /// 获取指定图形类型应该显示的参数页签
        /// </summary>
        public static List<ParameterTabType> GetTabs(ShapeType shapeType)
        {
            if (ShapeTypeToTabsMap.TryGetValue(shapeType, out var tabs))
            {
                return new List<ParameterTabType>(tabs);
            }
            return new List<ParameterTabType>();
        }
    }
}
