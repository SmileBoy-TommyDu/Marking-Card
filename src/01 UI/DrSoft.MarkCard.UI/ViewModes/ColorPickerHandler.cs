using DrSoft.Drawing.Event;
using DrSoft.MarkCard.CommonUI.UserControls;
using DrSoft.MarkCard.UI.UserControls;
using System.Windows;

namespace DrSoft.MarkCard.UI.ViewModes
{
    // UI-side handler: listens for ColorPickerRequestEvent, shows dialog, then publishes ColorPickedEvent
    public class ColorPickerHandler
    {
        public ColorPickerHandler()
        {
            EventBus.Instance.Subscribe<ColorPickerRequestEvent,string>(OnRequest);
        }

        private string OnRequest(ColorPickerRequestEvent req)
        {
            string result = "#000000";
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new ColorPickerDialog() { Owner = Application.Current.MainWindow };
                if (!string.IsNullOrEmpty(req.CurrentColor))
                    dlg.SetInitialColor(req.CurrentColor);

                if (dlg.ShowDialog() == true)
                {
                    var newColor = dlg.SelectedColor;
                    result= newColor.ToString();
                }
            });
            return result;
        }
    }
}
