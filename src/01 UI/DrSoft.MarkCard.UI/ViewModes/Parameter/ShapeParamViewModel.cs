using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;


namespace DrSoft.MarkCard.UI.ViewModes
{
    /// <summary>
    /// 图形参数视图模型，支持多种图形类型（矩形、圆弧等）
    /// 继承 BaseParamViewModel 以共用应用按钮逻辑
    /// </summary>
    public partial class ShapeParamViewModel : ObservableObject
    {
        [ObservableProperty]
        private ShapeType _currentShapeType;

        /// <summary>
        /// 设置当前显示的图形类型
        /// </summary>
        /// <param name="shapeType">图形类型</param>
        public void SetShapeType(ShapeType shapeType)
        {
            CurrentShapeType = shapeType;
        }

    }
}
