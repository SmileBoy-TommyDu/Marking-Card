using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.UI.Views;
using DrSoft.MarkCard.UI.Views.Parameter;
using DrSoft.MarkCard.UI.Views.Shape;
using System.Collections.ObjectModel;

namespace DrSoft.MarkCard.UI.UIConfig
{
    /// <summary>
    /// 参数页签工厂 - 负责根据图形类型动态构建页签列表
    /// </summary>
    public class ParameterTabFactory
    {
        private static readonly Dictionary<ElementTabConfig.ParameterTabType, (string Header, Type ViewType)> TabDefinitions =
            new()
            {
                { ElementTabConfig.ParameterTabType.Shape, ("图形参数", typeof(ShapeParamView)) },
                { ElementTabConfig.ParameterTabType.Engraving, ("雕刻参数", typeof(EngravingParamView)) },
                { ElementTabConfig.ParameterTabType.Delay, ("延迟参数", typeof(DelayParamView)) },
                { ElementTabConfig.ParameterTabType.Outline, ("外框填充", typeof(OutlineParamView)) },
                { ElementTabConfig.ParameterTabType.Fill, ("填充参数", typeof(FillParamView)) },
                { ElementTabConfig.ParameterTabType.MatrixCopy, ("复制设置", typeof(MatrixCopyParamView)) },
                { ElementTabConfig.ParameterTabType.LayerInputIO, ("输入信号", typeof(LayerInputIOView)) },
                { ElementTabConfig.ParameterTabType.LayerOutputIO, ("输出信号", typeof(LayerOutputIOView)) },
                { ElementTabConfig.ParameterTabType.GroupParam, ("图层", typeof(GroupParamView)) }
            };

        /// <summary>
        /// 为指定的图形类型创建参数页签列表
        /// </summary>
        public static ObservableCollection<ParameterTabItem> CreateTabsForShape(ShapeType shapeType)
        {
            var tabs = new ObservableCollection<ParameterTabItem>();
            var tabTypes = ElementTabConfig.GetTabs(shapeType);

            bool isFirstTab = true;
            foreach (var tabType in tabTypes)
            {
                if (TabDefinitions.TryGetValue(tabType, out var definition))
                {
                    tabs.Add(new ParameterTabItem(tabType, definition.Header, definition.ViewType, isFirstTab));
                    isFirstTab = false;
                }
            }

            return tabs;
        }

        /// <summary>
        /// 为 Layer 节点创建参数页签列表
        /// </summary>
        public static ObservableCollection<ParameterTabItem> CreateTabsForLayer()
        {
            var tabs = new ObservableCollection<ParameterTabItem>();
            var tabTypes = new[] 
            {
                ElementTabConfig.ParameterTabType.GroupParam,
                ElementTabConfig.ParameterTabType.LayerInputIO,
                ElementTabConfig.ParameterTabType.LayerOutputIO,
                ElementTabConfig.ParameterTabType.Engraving,
                ElementTabConfig.ParameterTabType.Delay,
                ElementTabConfig.ParameterTabType.MatrixCopy
            };

            bool isFirstTab = true;
            foreach (var tabType in tabTypes)
            {
                if (TabDefinitions.TryGetValue(tabType, out var definition))
                {
                    tabs.Add(new ParameterTabItem(tabType, definition.Header, definition.ViewType, isFirstTab));
                    isFirstTab = false;
                }
            }

            return tabs;
        }

        /// <summary>
        /// 为多个不同类型的图形创建参数页签列表（仅显示通用参数）
        /// </summary>
        public static ObservableCollection<ParameterTabItem> CreateTabsForMultipleShapes()
        {
            var tabs = new ObservableCollection<ParameterTabItem>();
            var tabTypes = new[]
            {
                ElementTabConfig.ParameterTabType.Engraving,
                ElementTabConfig.ParameterTabType.Delay,
                ElementTabConfig.ParameterTabType.Outline,
                ElementTabConfig.ParameterTabType.MatrixCopy
            };

            bool isFirstTab = true;
            foreach (var tabType in tabTypes)
            {
                if (TabDefinitions.TryGetValue(tabType, out var definition))
                {
                    tabs.Add(new ParameterTabItem(tabType, definition.Header, definition.ViewType, isFirstTab));
                    isFirstTab = false;
                }
            }

            return tabs;
        }

        /// <summary>
        /// 为 Hatch 节点创建参数页签列表
        /// </summary>
        public static ObservableCollection<ParameterTabItem> CreateTabsForHatch()
        {
            var tabs = new ObservableCollection<ParameterTabItem>();
            var tabTypes = new[]
            {
                ElementTabConfig.ParameterTabType.Engraving,
                ElementTabConfig.ParameterTabType.Delay,
                ElementTabConfig.ParameterTabType.Fill,
                ElementTabConfig.ParameterTabType.MatrixCopy
            };

            bool isFirstTab = true;
            foreach (var tabType in tabTypes)
            {
                if (TabDefinitions.TryGetValue(tabType, out var definition))
                {
                    tabs.Add(new ParameterTabItem(tabType, definition.Header, definition.ViewType, isFirstTab));
                    isFirstTab = false;
                }
            }

            return tabs;
        }
    }
}
