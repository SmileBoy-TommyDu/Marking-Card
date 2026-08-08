using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DrSoft.MarkCard.UI.UIConfig;
using DrSoft.MarkCard.UI.Views;
using DrSoft.MarkCard.UI.Views.Shape;

namespace DrSoft.MarkCard.UI.Converters
{
    /// <summary>
    /// 将参数页签类型转换为对应的视图
    /// </summary>
    public class ParameterTabTypeToViewConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not ElementTabConfig.ParameterTabType tabType)
                return null;

            return tabType switch
            {
                ElementTabConfig.ParameterTabType.Shape => new ScrollViewer { Content = new ShapeParamView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.Engraving => new ScrollViewer { Content = new EngravingParamView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.Delay => new ScrollViewer { Content = new DelayParamView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.Outline => new ScrollViewer { Content = new OutlineParamView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.Fill => new ScrollViewer { Content = new FillParamView(true), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.MatrixCopy => new ScrollViewer { Content = new MatrixCopyParamView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.LayerInputIO => new ScrollViewer { Content = new LayerInputIOView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.LayerOutputIO => new ScrollViewer { Content = new LayerOutputIOView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                ElementTabConfig.ParameterTabType.GroupParam => new ScrollViewer { Content = new GroupParamView(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                _ => null
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
