using CommunityToolkit.Mvvm.ComponentModel;
using DrSoft.Drawing.Event;
using DrSoft.MarkCard.Event.Tool;
using Newtonsoft.Json.Linq;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace DrSoft.MarkCard.UI.Views.Tool
{
    /// <summary>
    /// NumericUpDownControl.xaml 的交互逻辑
    /// </summary>
    public partial class TextFontNumericUpDownControl : UserControl
    {
        public string Title { get; set; }

        public static readonly DependencyProperty IsShowContentProperty =
            DependencyProperty.Register(nameof(IsShowContent), typeof(Visibility), typeof(TextFontNumericUpDownControl), new PropertyMetadata(Visibility.Collapsed));

        public Visibility IsShowContent
        {
            get => (Visibility)GetValue(IsShowContentProperty);
            set => SetValue(IsShowContentProperty, value);
        }

        public static readonly DependencyProperty ImageUrlProperty =
            DependencyProperty.Register(nameof(ImageUrl), typeof(ImageSource), typeof(TextFontNumericUpDownControl));

        public ImageSource ImageUrl
        {
            get => (ImageSource)GetValue(ImageUrlProperty);
            set => SetValue(ImageUrlProperty, value);
        }

        public string FilePath { get; set; }

        //public static readonly DependencyProperty IsEnabledProperty =
        //    DependencyProperty.Register(nameof(IsEnabled), typeof(bool), typeof(NumericUpDownControl));

        //public bool IsEnabled
        //{
        //    get => (bool)GetValue(IsEnabledProperty);
        //    set => SetValue(IsEnabledProperty, value);
        //}

        //public static readonly DependencyProperty NumberProperty =
        //        DependencyProperty.Register(nameof(Number), typeof(double), typeof(NumericUpDownControl), 
        //        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));


        //public double Number
        //{
        //    get => (double)GetValue(NumberProperty);
        //    set => SetValue(NumberProperty, value);
        //}

        //private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        //{
        //    var control = (NumericUpDownControl)d;
        //    double newValue = (double)e.NewValue;

        //    EventBus.Instance.Publish(new NumberChangedEvent()
        //    {
        //        ToolTip = control.Title,   // 注意使用 control.Title
        //        Value = (float)newValue
        //    });
        //}

        private double _currentNumber;
        public double CurrentNumber
        {
            get => _currentNumber;
            set
            {
                if (_currentNumber != value)
                {
                    _currentNumber = value;
                    if (NumericUpDownBtn.IsCommandTriggered)
                    {
                        NumericUpDownBtn.IsCommandTriggered = false; // 重置状态
                        CommandTriggered = true; // 标记命令已触发

                    }
                    EventBus.Instance.Publish(new NumberChangedEvent()
                    {
                        ToolTip = Title,
                        Value = (float)_currentNumber
                    });
                    OnPropertyChanged();
                    // 值改变时自动执行其他逻辑（例如保存、通知等）
                }
            }
        }


        public bool CommandTriggered
        {
            get;
            set;
        } = false;

        public TextFontNumericUpDownControl(string title)
        {
            InitializeComponent();

            Title = title;

            if (Title == "字号")
            {
                IsShowContent = Visibility.Collapsed;
            }
            else if (Title == "行距")
            {
                IsShowContent = Visibility.Visible;
                FilePath = "/Resource/image/Fonts/HeightDistanceDisable.png";
                ImageUrl = new BitmapImage(new Uri(FilePath, UriKind.Relative));
            }
            else if (Title == "字距")
            {
                IsShowContent = Visibility.Visible;
                FilePath = "/Resource/image/Fonts/FontDistanceDisable.png";
                ImageUrl = new BitmapImage(new Uri(FilePath, UriKind.Relative));
            }

            //NumericUpDownBtn.UpButton
        }

        public void UpdateEnabledState()
        {
            if (Title != "字号")
            {
                if (NumericUpDownBtn.IsEnabled)
                {
                    if (FilePath.Contains("Disable"))
                    {
                        FilePath = FilePath.Replace("Disable", "Enable");
                        ImageUrl = new BitmapImage(new Uri(FilePath, UriKind.Relative));
                    }
                }
                else
                {
                    if (FilePath.Contains("Enable"))
                    {
                        FilePath = FilePath.Replace("Enable", "Disable");
                        ImageUrl = new BitmapImage(new Uri(FilePath, UriKind.Relative));
                    }
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
