using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace DrSoft.MarkCard.UI.Views.Tool
{
    /// <summary>
    /// ComboBoxUserControl.xaml 的交互逻辑
    /// </summary>
    public partial class ComboBoxUserControl : UserControl
    {
        private Image _arrowImage;

        public ComboBoxUserControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 获取 ComboBox 的模板，并从中找到名为 "ArrowImage" 的元素
            _arrowImage = ComboBox.Template?.FindName("ArrowImage", ComboBox) as Image;

            // 初始设置图片
            UpdateArrowImage();

            // 可选：监听 IsEnabled 变化，动态改变图片
            DependencyPropertyDescriptor.FromProperty(UIElement.IsEnabledProperty, typeof(ComboBox))
                .AddValueChanged(ComboBox, (_, _) => UpdateArrowImage());
        }

        private void UpdateArrowImage()
        {
            if (_arrowImage == null) return;

            if (ComboBox.IsEnabled)
            {
                _arrowImage.Source = new BitmapImage(new Uri("/Resource/image/Fonts/FontFamilyArrowEnable.png", UriKind.Relative));
            }
            else
            {
                _arrowImage.Source = new BitmapImage(new Uri("/Resource/image/Fonts/FontFamilyArrowDisable.png", UriKind.Relative));
            }
        }
    }
}
