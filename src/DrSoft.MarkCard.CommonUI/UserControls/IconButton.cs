using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DrSoft.MarkCard.CommonUI.UserControls
{
    /// <summary>
    /// 32×32 图标按鈕组件，支持悬停/按下状态的内边框与内阴影效果，可自定义图标。
    /// 支持 GroupName 实现同组互斥选中行为。
    /// </summary>
    public class IconButton : ToggleButton
    {
        #region 互斥组管理

        // key: groupName, value: 当前组内所有按鈕的弱引用列表
        private static readonly Dictionary<string, List<WeakReference<IconButton>>> _groups
            = new Dictionary<string, List<WeakReference<IconButton>>>();

        private static void RegisterToGroup(IconButton btn, string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return;
            if (!_groups.TryGetValue(groupName, out var list))
            {
                list = new List<WeakReference<IconButton>>();
                _groups[groupName] = list;
            }
            // 清理已回收的弱引用
            list.RemoveAll(r => !r.TryGetTarget(out _));
            list.Add(new WeakReference<IconButton>(btn));
        }

        private static void UnregisterFromGroup(IconButton btn, string groupName)
        {
            if (string.IsNullOrEmpty(groupName)) return;
            if (!_groups.TryGetValue(groupName, out var list)) return;
            list.RemoveAll(r => !r.TryGetTarget(out var b) || ReferenceEquals(b, btn));
        }

        /// <summary>当前按鈕被选中时，取消同组内其他按鈕的选中状态</summary>
        private void UnselectOthersInGroup()
        {
            var groupName = GroupName;
            if (string.IsNullOrEmpty(groupName)) return;
            if (!_groups.TryGetValue(groupName, out var list)) return;

            foreach (var wr in list)
            {
                if (wr.TryGetTarget(out var other) && !ReferenceEquals(other, this))
                {
                    other.IsChecked = false;
                }
            }
        }

        #endregion

        #region 依赖属性

        /// <summary>
        /// 按鈕图标路径（字符串，支持 pack URI 或相对路径如 /Resource/...）
        /// </summary>
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(
                nameof(Icon),
                typeof(string),
                typeof(IconButton),
                new PropertyMetadata(null, OnIconChanged));

        /// <summary>
        /// 内部使用的 ImageSource，由 Icon 路径自动解析生成，模板直接绑定此项。
        /// </summary>
        public static readonly DependencyProperty IconImageProperty =
            DependencyProperty.Register(
                nameof(IconImage),
                typeof(ImageSource),
                typeof(IconButton),
                new PropertyMetadata(null));

        /// <summary>
        /// 图标宽度（默认 16）
        /// </summary>
        public static readonly DependencyProperty IconWidthProperty =
            DependencyProperty.Register(
                nameof(IconWidth),
                typeof(double),
                typeof(IconButton),
                new PropertyMetadata(16.0));

        /// <summary>
        /// 图标高度（默认 16）
        /// </summary>
        public static readonly DependencyProperty IconHeightProperty =
            DependencyProperty.Register(
                nameof(IconHeight),
                typeof(double),
                typeof(IconButton),
                new PropertyMetadata(16.0));

        /// <summary>
        /// 分组名称，同组内的 IconButton 互斥选中。
        /// </summary>
        public static readonly DependencyProperty GroupNameProperty =
            DependencyProperty.Register(
                nameof(GroupName),
                typeof(string),
                typeof(IconButton),
                new PropertyMetadata(null, OnGroupNameChanged));

        private static void OnGroupNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var btn = (IconButton)d;
            if (e.OldValue is string oldGroup)
                UnregisterFromGroup(btn, oldGroup);
            if (e.NewValue is string newGroup)
                RegisterToGroup(btn, newGroup);
        }

        #endregion

        #region 属性包装

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public ImageSource IconImage
        {
            get => (ImageSource)GetValue(IconImageProperty);
            set => SetValue(IconImageProperty, value);
        }

        public double IconWidth
        {
            get => (double)GetValue(IconWidthProperty);
            set => SetValue(IconWidthProperty, value);
        }

        public double IconHeight
        {
            get => (double)GetValue(IconHeightProperty);
            set => SetValue(IconHeightProperty, value);
        }

        public string GroupName
        {
            get => (string)GetValue(GroupNameProperty);
            set => SetValue(GroupNameProperty, value);
        }

        #endregion

        #region 图标路径解析

        private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var button = (IconButton)d;
            var path = (string)e.NewValue;

            if (string.IsNullOrWhiteSpace(path))
            {
                button.IconImage = null;
                return;
            }

            try
            {
                Uri uri;
                if (Uri.TryCreate(path, UriKind.Absolute, out uri))
                {
                    // 已是绝对 URI（如 pack://application:,,,,...）
                }
                else if (path.StartsWith("/"))
                {
                    // /Resource/... → pack://application:,,,/Resource/...
                    uri = new Uri("pack://application:,,," + path, UriKind.Absolute);
                }
                else
                {
                    uri = new Uri("pack://application:,,,/" + path, UriKind.Absolute);
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                button.IconImage = bitmap;
            }
            catch
            {
                button.IconImage = null;
            }
        }

        #endregion

        static IconButton()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(IconButton),
                new FrameworkPropertyMetadata(typeof(IconButton)));
        }

        public IconButton()
        {
            // 注册进组（如果 XAML 里已设置 GroupName 则自动触发）
        }

        /// <summary>拦截选中事件，实现互斥</summary>
        protected override void OnChecked(RoutedEventArgs e)
        {
            base.OnChecked(e);
            UnselectOthersInGroup();
        }
    }
}
