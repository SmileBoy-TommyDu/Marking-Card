using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;

namespace DrSoft.Drawing.Controls.Views
{
    /// <summary>
    /// DialogWindow.xaml 的交互逻辑
    /// </summary>
    public partial class DialogWindow : Window
    {
        public DialogWindow()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty WindowHeightProperty =
           DependencyProperty.Register(nameof(WindowHeight), typeof(double), typeof(DialogWindow),
               new FrameworkPropertyMetadata(400.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                   OnWindowHeightPropertyChanged));


        private static void OnWindowHeightPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DialogWindow control)
            {
                control.Height = (double)e.NewValue;
            }
        }
        public double WindowHeight
        {
            get => (double)GetValue(WindowHeightProperty);
            set => SetValue(WindowHeightProperty, value);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
