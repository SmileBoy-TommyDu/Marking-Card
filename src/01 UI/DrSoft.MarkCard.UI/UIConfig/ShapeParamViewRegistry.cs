using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.UIConfig
{
    /// <summary>
    /// 图形参数视图注册表，支持动态注册和解析指定图形类型的参数视图
    /// </summary>
    public static class ShapeParamViewRegistry
    {
        private static readonly Dictionary<ShapeType, Func<UserControl>> _registry = new();

        /// <summary>
        /// 注册图形类型对应的视图工厂
        /// </summary>
        public static void Register(ShapeType shapeType, Func<UserControl> factory)
        {
            _registry[shapeType] = factory;
        }

        /// <summary>
        /// 根据图形类型创建对应的参数视图
        /// </summary>
        public static UserControl? CreateView(ShapeType shapeType)
        {
            return _registry.TryGetValue(shapeType, out var factory) ? factory() : null;
        }

        /// <summary>
        /// 判断指定图形类型是否已注册视图
        /// </summary>
        public static bool IsRegistered(ShapeType shapeType)
        {
            return _registry.ContainsKey(shapeType);
        }
    }
}
