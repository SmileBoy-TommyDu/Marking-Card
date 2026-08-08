using System.Windows;
using System.Windows.Controls.Primitives;

namespace DrSoft.Drawing.Utility
{
    public static class PopupManagement
    {
        private static readonly object _lock = new object();
        private static readonly HashSet<Popup> _allPopups = new HashSet<Popup>();

        public static void Register(Popup popup)
        {
            if (popup == null) return;
            lock (_lock)
            {
                _allPopups.Add(popup);
            }
        }

        public static void Unregister(Popup popup)
        {
            if (popup == null) return;
            lock (_lock)
            {
                _allPopups.Remove(popup);
            }
        }

        public static void CloseAllPopups()
        {
            List<Popup> snapshots;
            lock (_lock)
            {
                snapshots = _allPopups.ToList();
            }

            // 必须在 UI 线程上执行关闭操作
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var popup in snapshots)
                {
                    try
                    {
                        if (popup != null && popup.IsOpen)
                            popup.IsOpen = false;
                    }
                    catch
                    {
                        // 忽略已经销毁的 Popup
                    }
                }
            });
        }
    }
}
