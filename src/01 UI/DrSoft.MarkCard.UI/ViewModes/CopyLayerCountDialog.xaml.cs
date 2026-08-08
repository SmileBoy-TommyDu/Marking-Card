using System.Windows;
using System.Windows.Controls;

namespace DrSoft.MarkCard.UI.ViewModes
{
    /// <summary>
    /// 复制图层数量输入对话框
    /// </summary>
    public partial class CopyLayerCountDialog : Window
    {
        /// <summary>用户输入的复制数量</summary>
        public int CopyCount { get; private set; } = 1;

        public CopyLayerCountDialog(string layerName)
        {
            InitializeComponent();
            PromptText.Text = $"请输入复制图层数量（图层：{layerName}）";
            CountInput.Focus();
            CountInput.SelectAll();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CountInput.Text, out int count) && count > 0 && count <= 100)
            {
                CopyCount = count;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("请输入1到100之间的正整数", "输入无效", MessageBoxButton.OK, MessageBoxImage.Warning);
                CountInput.Focus();
                CountInput.SelectAll();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        /// <summary>仅允许输入数字</summary>
        private void CountInput_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>阻止粘贴非数字内容</summary>
        private void CountInput_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var text = (string)e.DataObject.GetData(typeof(string));
                if (!int.TryParse(text, out _))
                    e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }
    }
}
