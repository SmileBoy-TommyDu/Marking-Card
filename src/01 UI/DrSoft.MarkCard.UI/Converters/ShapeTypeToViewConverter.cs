using DrSoft.Drawing.DTO;
using DrSoft.Drawing.Model;
using DrSoft.MarkCard.UI.Views.Shape;
using System.Globalization;
using System.Windows.Data;

namespace DrSoft.MarkCard.UI.Converters
{
    /// <summary>
    /// 将 ShapeType 转换为对应的图形参数视图
    /// </summary>
    public class ShapeTypeToViewConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not ShapeType shapeType)
                return null;

            return shapeType switch
            {
                ShapeType.Rectangle => new RectangleParamView(),
                ShapeType.Arc => new ArcParamView(),
                ShapeType.PolyLine => new CurveParamView(),
                ShapeType.Circle => new CircleParamView(),
                ShapeType.Text => new TextParamView(),
                ShapeType.Polygon => new PolygonParamView(),
                ShapeType.Combination => new GroupParamView(),
                ShapeType.Group => new GroupParamView(),
                ShapeType.ArbitraryCurve => new CurveParamView(),
                ShapeType.Bezier => new CurveParamView(),
                _ => null
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
