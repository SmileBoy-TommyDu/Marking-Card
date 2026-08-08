using System.Windows;

namespace DrSoft.MarkCard.UI.UIConfig
{
    /// <summary>
    /// 参数页签数据模型
    /// </summary>
    public class ParameterTabItem
    {
        /// <summary>
        /// 页签类型
        /// </summary>
        public ElementTabConfig.ParameterTabType TabType { get; set; }

        /// <summary>
        /// 页签标题
        /// </summary>
        public string Header { get; set; }

        /// <summary>
        /// 页签内容视图类型
        /// </summary>
        public Type ContentViewType { get; set; }

        /// <summary>
        /// 是否是默认选中的页签
        /// </summary>
        public bool IsSelected { get; set; }

        public ParameterTabItem(ElementTabConfig.ParameterTabType tabType, string header, Type contentViewType, bool isSelected = false)
        {
            TabType = tabType;
            Header = header;
            ContentViewType = contentViewType;
            IsSelected = isSelected;
        }

        public override bool Equals(object? obj)
        {
            return obj is ParameterTabItem item && item.TabType == TabType;
        }

        public override int GetHashCode()
        {
            return TabType.GetHashCode();
        }
    }
}
