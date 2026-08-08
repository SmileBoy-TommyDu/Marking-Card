using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 图形添加策略接口
    /// 用于处理不同类型的图形（基础图形、群组、组合等）添加到图层的逻辑
    /// </summary>
    public interface IShapeAddStrategy
    {
        /// <summary>
        /// 检查此策略是否支持处理给定的图形
        /// </summary>
        bool CanHandle(IShape shape);

        /// <summary>
        /// 将图形添加到图层ViewModel和模型中
        /// </summary>
        void Add(
            IShape shape,
            LayerViewModel layerViewModel,
            DrawingLayer layerModel,
            IList<INodeViewModel> children);
    }

    /// <summary>
    /// 基础图形添加策略
    /// 处理矩形、圆形、直线等基础图形
    /// </summary>
    public class BasicShapeAddStrategy : IShapeAddStrategy
    {
        public bool CanHandle(IShape shape)
        {
            // 判断基础图形
            return shape is not DrawingGroup && shape is not DrawCombination && shape is not DrawingHatch;
        }

        public void Add(
            IShape shape,
            LayerViewModel layerViewModel,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            var nodeVm = NodeViewModelFactory.Create(shape, layerViewModel);
            children.Add(nodeVm);
            layerModel.AddShape(shape);
        }
    }

    /// <summary>
    /// 群组添加策略
    /// 处理 DrawingGroup 类型的群组
    /// </summary>
    public class GroupAddStrategy : IShapeAddStrategy
    {
        public bool CanHandle(IShape shape) => shape is DrawingGroup;

        public void Add(
            IShape shape,
            LayerViewModel layerViewModel,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            if (shape is not DrawingGroup)
                return;

            var nodeVm = NodeViewModelFactory.Create(shape, layerViewModel);
            children.Add(nodeVm);
            layerModel.AddShape(shape);
        }
    }

    /// <summary>
    /// 填满添加策略
    /// 处理 DrawingHatch 类型的填满容器
    /// </summary>
    public class HatchAddStrategy : IShapeAddStrategy
    {
        public bool CanHandle(IShape shape) => shape is DrawingHatch;

        public void Add(
            IShape shape,
            LayerViewModel layerViewModel,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            if (shape is not DrawingHatch)
                return;

            var nodeVm = NodeViewModelFactory.Create(shape, layerViewModel);
            children.Add(nodeVm);
            layerModel.AddShape(shape);
        }
    }

    /// <summary>
    /// 组合添加策略
    /// 处理 Combination 类型的组合
    /// </summary>
    public class CombinationAddStrategy : IShapeAddStrategy
    {
        public bool CanHandle(IShape shape) => shape is DrawCombination;

        public void Add(
            IShape shape,
            LayerViewModel layerViewModel,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            if (shape is not DrawCombination)
                return;

            var nodeVm = NodeViewModelFactory.Create(shape, layerViewModel);
            children.Add(nodeVm);
            layerModel.AddShape(shape);
        }
    }

    /// <summary>
    /// 图形添加策略工厂
    /// 集中管理所有图形添加策略
    /// </summary>
    public class ShapeAddStrategyFactory
    {
        private readonly List<IShapeAddStrategy> _strategies;

        public ShapeAddStrategyFactory()
        {
            _strategies = new List<IShapeAddStrategy>
            {
                new GroupAddStrategy(),
                new CombinationAddStrategy(),
                new HatchAddStrategy(),
                new BasicShapeAddStrategy()
            };
        }

        /// <summary>
        /// 获取支持处理给定图形的策略
        /// </summary>
        public IShapeAddStrategy GetStrategy(IShape shape)
        {
            foreach (var strategy in _strategies)
            {
                if (strategy.CanHandle(shape))
                    return strategy;
            }

            return _strategies.Last();
        }

        /// <summary>
        /// 注册自定义策略
        /// </summary>
        public void Register(IShapeAddStrategy strategy)
        {
            _strategies.Insert(0, strategy);
        }
    }
}
