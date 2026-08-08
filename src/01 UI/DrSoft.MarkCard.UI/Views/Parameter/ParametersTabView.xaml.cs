using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.UI.UIConfig;
using DrSoft.MarkCard.UI.ViewModes;
using System.Windows;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.Views
{
    /// <summary>
    /// PropertyTableView.xaml 的交互逻辑
    /// </summary>
    public partial class ParametersTabView : UserControl
    {
        private readonly ShapeParamViewModel _shapeParamViewModel;
        private readonly ParametersTabViewModel _parametersTabViewModel;
        private readonly OutlineParamViewModel _outlineParamViewModel;

        private readonly DrawingBoardViewModel _drawingBoardViewModel;
        private readonly GalvoSettingViewModel _galvoSettingViewModel;
        private readonly IOViewModel _ioViewModel;
        private readonly LaserTestViewModel _laserTestViewModel;
        private readonly SystemParamViewModel _systemParamViewModel;

        private CanvasSystemConfig canvasSystemConfig;
        public ParametersTabView()
        {
            InitializeComponent();
            _parametersTabViewModel = App.GetService<ParametersTabViewModel>();
            DataContext = _parametersTabViewModel;
            _parametersTabViewModel._saveType = ParaSaveType.Canvas;
            ElementTab.Visibility = Visibility.Collapsed;
            SystemTab.Visibility = Visibility.Visible;

            _outlineParamViewModel = App.GetService<OutlineParamViewModel>();
            _shapeParamViewModel = App.GetService<ShapeParamViewModel>();


            canvasSystemConfig = App.GetService<CanvasSystemConfig>();
            _drawingBoardViewModel = App.GetService<DrawingBoardViewModel>();
            _galvoSettingViewModel = App.GetService<GalvoSettingViewModel>();
            _ioViewModel= App.GetService<IOViewModel>();
            _laserTestViewModel= App.GetService<LaserTestViewModel>();
            _systemParamViewModel = App.GetService<SystemParamViewModel>();

            LoadUISystemPara();
            
            // 订阅节点选择事件
            EventBus.Instance.Subscribe<NodeSelectedEvent>(OnSelectionChanged);
        }

        /// <summary>
        /// 加载系统参数
        /// </summary>
        private void LoadUISystemPara()
        {
            ElementTab.Visibility = Visibility.Collapsed;
            SystemTab.Visibility = Visibility.Visible;
            _parametersTabViewModel._saveType = ParaSaveType.Canvas;
            _drawingBoardViewModel.Model = canvasSystemConfig.DrawingBoardParameter;
            _galvoSettingViewModel.Model = canvasSystemConfig.GalvoConfig;
            _systemParamViewModel.Model = canvasSystemConfig.SystemParam;
        }

        /// <summary>
        /// 响应框选事件，根据选中节点的类型，更新页签显示和参数数据
        /// </summary>
        /// <param name="e"></param>
        private void OnSelectionChanged(NodeSelectedEvent e)
        {
            RuntimeContext.ActiveCanvasId = e.CanvasId;
            RuntimeContext.Selections = e.Summary.SelectionIds;

            ElementTab.Visibility = Visibility.Visible;
            SystemTab.Visibility = Visibility.Collapsed;
            _parametersTabViewModel._saveType = ParaSaveType.Element;
            // 根据不同的节点类型，显示不同的页签，页签参数数据
            switch (e.NodeType)
            {
                case NodeType.Canvas:
                    LoadUISystemPara();
                    break;
                case NodeType.Layer:
                    _parametersTabViewModel.BuildTabsForLayer();
                    break;
                case NodeType.Shape:
                    // 1、单图形场景 2、多图形场景（同类型图形、不同类型图形）
                    if (e.Summary?.UniformType != null)
                    {
                        // 文本类型显示反向雕刻按钮
                        _outlineParamViewModel.ButtonVisibility = (e.Summary.EditingObject != null && e.Summary.EditingObject is DrawTextDto) ? Visibility.Visible : Visibility.Collapsed;

                        // 根据图形类型更新页签列表
                        _parametersTabViewModel.BuildTabsForShape(e.Summary.UniformType.Value);
                        _shapeParamViewModel.SetShapeType(e.Summary.UniformType.Value);
                    }
                    else
                    {
                        // 多图形场景且是不同类型的图形
                        _parametersTabViewModel.BuildTabsForMultipleShapes();
                    }
                    break;
            }
        }
    }
}
