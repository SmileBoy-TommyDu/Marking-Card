using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.UI.UIConfig;
using System.Collections.ObjectModel;
using System.ComponentModel;
using static DrSoft.MarkCard.UI.UIConfig.ElementTabConfig;

namespace DrSoft.MarkCard.UI.ViewModes
{
    public partial class ParametersTabViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _shapeTabHeader = "";

        public ParaSaveType _saveType = ParaSaveType.Canvas;

        /// <summary>
        /// 动态参数页签列表
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<ParameterTabItem> _elementTabs = new();

        public ParametersTabViewModel()
        {
            EventBus.Instance.Subscribe<CanvasChangedEvent>(OnTransformChanged);
        }

        private void OnTransformChanged(CanvasChangedEvent args)
        {
            if (args.ChangeType != CanvasChangeType.TransformChanged) return;

            // 获取当前活动画布，对发生变换的图形重新生成填充图形
            if (DocumentContext.Instance.ActiveCanvas is not DrawingCanvas canvas) return;
            if (args.Data is SelectedSharpsDto dto && !dto.RequiresHatchRegeneration) return;

            // 优先从事件负载中解析被变换的图形 Id；解析不到时回退使用当前选中图形
            if (args.Data is SelectedSharpsDto dtoWithSelection && dtoWithSelection.SelectionIds.Any())
            {
                var ids = dtoWithSelection.SelectionIds.ToHashSet();
                var affected = canvas.AllShapes.Where(s => ids.Contains(s.UId)).ToList();
                if (!canvas.RequiresHatchRegeneration(affected)) return;
                canvas.RegenerateHatchForShapes(affected);
            }
            else
            {
                if (!canvas.RequiresHatchRegeneration()) return;
                canvas.RegenerateHatchForShapes();
            }
        }
        /// <summary>
        /// 根据图形类型更新页签名称
        /// </summary>
        public string UpdateShapeTabHeader(ShapeType shapeType)
        {
            var field = typeof(ShapeType).GetField(shapeType.ToString());
            var attribute = (DescriptionAttribute?)field?.GetCustomAttributes(typeof(DescriptionAttribute), false).FirstOrDefault();
            ShapeTabHeader = attribute?.Description ?? shapeType.ToString();
            return ShapeTabHeader;
        }

        /// <summary>
        /// 为指定的图形类型更新参数页签列表
        /// </summary>
        public void BuildTabsForShape(ShapeType shapeType)
        {
            var newTabs = ParameterTabFactory.CreateTabsForShape(shapeType);
            var shapeTab = newTabs.FirstOrDefault(x => x.TabType == ParameterTabType.Shape);
            if (shapeTab != null)
            {
                shapeTab.Header = UpdateShapeTabHeader(shapeType);
            }

            BuildElementTabs(newTabs);
        }

        /// <summary>
        /// 为 Layer 节点更新参数页签列表
        /// </summary>
        public void BuildTabsForLayer()
        {
            var newTabs = ParameterTabFactory.CreateTabsForLayer();
            BuildElementTabs(newTabs);
        }

        /// <summary>
        /// 为多个不同类型的图形更新参数页签列表
        /// </summary>
        public void BuildTabsForMultipleShapes()
        {
            var newTabs = ParameterTabFactory.CreateTabsForMultipleShapes();
            BuildElementTabs(newTabs);
        }

        /// <summary>
        /// 为 Hatch 节点更新参数页签列表
        /// </summary>
        public void BuildTabsForHatch()
        {
            var newTabs = ParameterTabFactory.CreateTabsForHatch();
            BuildElementTabs(newTabs);
        }

        /// <summary>
        /// 更新参数页签列表
        /// </summary>
        private void BuildElementTabs(ObservableCollection<ParameterTabItem> newTabs)
        {
            ElementTabs?.Clear();
            if (newTabs != null)
            {
                foreach (var tab in newTabs)
                {
                    ElementTabs?.Add(tab);
                }
            }
        }

        [RelayCommand]
        private void ApplyAll()
        {
            string? title = _saveType == ParaSaveType.Element ? ElementTabs.FirstOrDefault(x => x.Header.Equals("填充"))?.Header : string.Empty; 
            if (title == string.Empty||title==null)
                title = _saveType == ParaSaveType.Element ? ElementTabs.FirstOrDefault()?.Header : string.Empty;
            EventBus.Instance.Publish(new ParaSaveEvent()
            {
                TriggerTitle = title ?? string.Empty,
                ParaSaveType = _saveType,
                Trigger = true
            });
        }
    }

    public class ParaSaveEvent : IEvent
    {
        public string TriggerTitle { get; set; }
        public bool Trigger { get; set; }
        public ParaSaveType ParaSaveType { get; set; }
    }
    public enum ParaSaveType
    {
        Canvas,
        Element
    }
}
