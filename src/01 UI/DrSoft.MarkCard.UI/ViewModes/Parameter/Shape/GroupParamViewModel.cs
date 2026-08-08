using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;

namespace DrSoft.MarkCard.UI.ViewModes.Parameter
{
    public partial class GroupParamViewModel : ObservableObject
    {
        [ObservableProperty]
        private int infoText = 0;

        public GroupParamViewModel()
        {
            // replayLast=true: 如果事件在订阅前已发布，立即重放最后事件
            EventBus.Instance.Subscribe<NodeSelectedEvent>(OnNodeSelected, replayLast: true);
        }

        private void OnNodeSelected(NodeSelectedEvent evt)
        {
            if (evt == null || evt.Summary == null) return;
            InfoText = 0;

            // 选中图层：显示图层内图形个数
            if (evt.NodeType == NodeType.Layer)
            {
                InfoText = evt.Summary.TotalCount;
            }
            // 选中群组/组合：显示包含子级的总数
            else if (evt.Summary.EditingObject != null)
            {
                if (evt.Summary.EditingObject.Type == ShapeType.Group || evt.Summary.EditingObject.Type == ShapeType.Combination)
                {
                    InfoText = evt.Summary.TotalCountWithChildren;
                }
            }
        }
    }
}

