using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.ViewModels
{
    /// <summary>
    /// 图形移除策略接口
    /// 用于处理不同类型的图形移除逻辑
    /// </summary>
    public interface IShapeRemoveStrategy
    {
        /// <summary>
        /// 检查此策略是否支持处理给定的图形
        /// </summary>
        bool CanHandle(IShape shape);

        /// <summary>
        /// 从图层ViewModel和模型中移除图形
        /// </summary>
        void Remove(
            IShape shape,
            DrawingLayer layerModel,
            IList<INodeViewModel> children);
    }

    /// <summary>
    /// 通用图形移除策略（支持递归查找）
    /// </summary>
    public class BasicShapeRemoveStrategy : IShapeRemoveStrategy
    {
        public bool CanHandle(IShape shape) => true;

        public void Remove(
            IShape shape,
            DrawingLayer layerModel,
            IList<INodeViewModel> childrenNode)
        {
            var removedFromNodes = RemoveNodeRecursive(childrenNode, shape);
            if (removedFromNodes || layerModel.AllShapesInternal.Any(existing => ReferenceEquals(existing, shape) || existing.UId == shape.UId))
            {
                layerModel.RemoveShape(shape);
            }
        }

        /// <summary>
        /// 递归移除节点（VirtualizingNodeCollection 使用模型层查找，避免枚举 ViewModel）
        /// </summary>
        private static bool RemoveNodeRecursive(IList<INodeViewModel> children, IShape shape)
        {
            // VirtualizingNodeCollection：基于模型层 UId 查找，不枚举 ViewModel
            if (children is VirtualizingNodeCollection vc)
                return vc.RemoveByModelId(shape.UId);

            // ObservableCollection：标准 LINQ 查找
            var node = children.FirstOrDefault(x => x.Id == shape.UId);
            if (node != null)
            {
                children.Remove(node);
                return true;
            }

            // 递归子容器
            foreach (var child in children)
            {
                if (RemoveNodeRecursive(child.Children, shape))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 群组移除策略
    /// </summary>
    public class GroupRemoveStrategy : IShapeRemoveStrategy
    {
        public bool CanHandle(IShape shape) => shape is DrawingGroup;

        public void Remove(
            IShape shape,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            if (shape is not DrawingGroup)
                return;

            var removedFromNodes = RemoveNodeRecursive(children, shape);
            if (removedFromNodes || layerModel.AllShapesInternal.Any(existing => ReferenceEquals(existing, shape) || existing.UId == shape.UId))
            {
                layerModel.RemoveShape(shape);
            }
        }

        private static bool RemoveNodeRecursive(IList<INodeViewModel> children, IShape shape)
        {
            if (children is VirtualizingNodeCollection vc)
                return vc.RemoveByModelId(shape.UId);

            var node = children.FirstOrDefault(x => x.Id == shape.UId);
            if (node != null)
            {
                children.Remove(node);
                return true;
            }

            foreach (var child in children)
            {
                if (RemoveNodeRecursive(child.Children, shape))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 组合移除策略
    /// </summary>
    public class CombinationRemoveStrategy : IShapeRemoveStrategy
    {
        public bool CanHandle(IShape shape) => shape is DrawCombination;

        public void Remove(
            IShape shape,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            if (shape is not DrawCombination)
                return;

            var removedFromNodes = RemoveNodeRecursive(children, shape);
            if (removedFromNodes || layerModel.AllShapesInternal.Any(existing => ReferenceEquals(existing, shape) || existing.UId == shape.UId))
            {
                layerModel.RemoveShapes(new IShape[] { shape });
            }
        }

        private static bool RemoveNodeRecursive(IList<INodeViewModel> children, IShape shape)
        {
            if (children is VirtualizingNodeCollection vc)
                return vc.RemoveByModelId(shape.UId);

            var node = children.FirstOrDefault(x => x.Id == shape.UId);
            if (node != null)
            {
                children.Remove(node);
                return true;
            }

            foreach (var child in children)
            {
                if (RemoveNodeRecursive(child.Children, shape))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 填满移除策略
    /// </summary>
    public class HatchRemoveStrategy : IShapeRemoveStrategy
    {
        public bool CanHandle(IShape shape) => shape is DrawingHatch;

        public void Remove(
            IShape shape,
            DrawingLayer layerModel,
            IList<INodeViewModel> children)
        {
            if (shape is not DrawingHatch)
                return;

            var removedFromNodes = RemoveNodeRecursive(children, shape);
            if (removedFromNodes || layerModel.AllShapesInternal.Any(existing => ReferenceEquals(existing, shape) || existing.UId == shape.UId))
            {
                layerModel.RemoveShapes(new IShape[] { shape });
                // 不在这里清除 HatchParamInfo：
                // 1. BreakFill 已在自身逻辑中显式清除；
                // 2. 撤销/重做依赖 HatchParamInfo 保持原值，
                //    否则重做时 DrawingHatchRender 因 HatchParamInfo==null 跳过渲染。
            }
        }

        private static bool RemoveNodeRecursive(IList<INodeViewModel> children, IShape shape)
        {
            if (children is VirtualizingNodeCollection vc)
                return vc.RemoveByModelId(shape.UId);

            var node = children.FirstOrDefault(x => x.Id == shape.UId);
            if (node != null)
            {
                children.Remove(node);
                return true;
            }

            foreach (var child in children)
            {
                if (RemoveNodeRecursive(child.Children, shape))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 图形移除策略工厂
    /// </summary>
    public class ShapeRemoveStrategyFactory
    {
        private readonly List<IShapeRemoveStrategy> _strategies;

        public ShapeRemoveStrategyFactory()
        {
            _strategies = new List<IShapeRemoveStrategy>
            {
                new GroupRemoveStrategy(),
                new CombinationRemoveStrategy(),
                new HatchRemoveStrategy(),
                new BasicShapeRemoveStrategy(),
            };
        }

        public IShapeRemoveStrategy GetStrategy(IShape shape)
        {
            foreach (var strategy in _strategies)
            {
                if (strategy.CanHandle(shape))
                    return strategy;
            }
            return _strategies.Last();
        }

        public void Register(IShapeRemoveStrategy strategy)
        {
            _strategies.Insert(0, strategy);
        }
    }
}
