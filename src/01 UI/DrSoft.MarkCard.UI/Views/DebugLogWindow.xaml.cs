using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility.AOP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace DrSoft.MarkCard.UI.Views
{
    public partial class DebugLogWindow : Window
    {
        private const int MaxVisibleLines = 2000;
        private readonly Queue<string> _visibleLines = new();
        private bool _isInitializing;
        private DrawingCanvas? _subscribedCanvas;

        public DebugLogWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Closed += OnClosed;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;
            EnableDebugCheckBox.IsChecked = LogAttribute.IsEnabled;
            LoadSnapshot();
            RefreshUndoRedoView();
            DebugLogHub.MessageAppended += OnMessageAppended;
            DebugLogHub.Cleared += OnLogsCleared;
            DocumentContext.Instance.ActiveCanvasChanged += OnActiveCanvasChanged;
            _isInitializing = false;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            DebugLogHub.MessageAppended -= OnMessageAppended;
            DebugLogHub.Cleared -= OnLogsCleared;
            DocumentContext.Instance.ActiveCanvasChanged -= OnActiveCanvasChanged;
            UnsubscribeCanvasCommandHistory();
        }

        private void EnableDebugCheckBox_OnChecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            LogAttribute.SetEnable(true);
        }

        private void EnableDebugCheckBox_OnUnchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            LogAttribute.SetEnable(false);
        }

        private void OnMessageAppended(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendMessage(message)));
                return;
            }

            AppendMessage(message);
        }

        private void OnLogsCleared()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(ClearVisibleLogs));
                return;
            }

            ClearVisibleLogs();
        }

        private void LoadSnapshot()
        {
            _visibleLines.Clear();

            foreach (var line in DebugLogHub.GetSnapshot().TakeLast(MaxVisibleLines))
            {
                _visibleLines.Enqueue(line);
            }

            LogTextBox.Text = string.Join(Environment.NewLine, _visibleLines);
            LogTextBox.CaretIndex = LogTextBox.Text.Length;
            LogTextBox.ScrollToEnd();
        }

        private void RefreshUndoRedoView()
        {
            SubscribeCanvasCommandHistory();

            var activeCanvas = DocumentContext.Instance.ActiveCanvas as DrawingCanvas;
            var viewData = DebugUndoRedoViewData.Build(activeCanvas);
            UndoRedoCanvasLabelTextBlock.Text = viewData.CanvasLabel;
            UndoRedoCountsLabelTextBlock.Text = viewData.CountsLabel;

            var undoSection = viewData.Sections.ElementAtOrDefault(0);
            UndoSectionTitleTextBlock.Text = undoSection?.Title ?? "Undo 栈";
            UndoSectionSummaryTextBlock.Text = undoSection?.Summary ?? "<empty>";
            UndoEntriesListBox.ItemsSource = undoSection?.Entries;

            var redoSection = viewData.Sections.ElementAtOrDefault(1);
            RedoSectionTitleTextBlock.Text = redoSection?.Title ?? "Redo 栈";
            RedoSectionSummaryTextBlock.Text = redoSection?.Summary ?? "<empty>";
            RedoEntriesListBox.ItemsSource = redoSection?.Entries;
        }

        private void SubscribeCanvasCommandHistory()
        {
            var activeCanvas = DocumentContext.Instance.ActiveCanvas as DrawingCanvas;
            if (ReferenceEquals(_subscribedCanvas, activeCanvas))
            {
                return;
            }

            UnsubscribeCanvasCommandHistory();

            _subscribedCanvas = activeCanvas;
            if (_subscribedCanvas == null)
            {
                return;
            }

            _subscribedCanvas.CommandHistory.CommandExecuted += OnCommandHistoryChanged;
        }

        private void UnsubscribeCanvasCommandHistory()
        {
            if (_subscribedCanvas == null)
            {
                return;
            }

            _subscribedCanvas.CommandHistory.CommandExecuted -= OnCommandHistoryChanged;
            _subscribedCanvas = null;
        }

        private void AppendMessage(string message)
        {
            _visibleLines.Enqueue(message);
            LogTextBox.AppendText(message + Environment.NewLine);

            if (_visibleLines.Count > MaxVisibleLines)
            {
                while (_visibleLines.Count > MaxVisibleLines)
                {
                    _visibleLines.Dequeue();
                }

                LogTextBox.Text = string.Join(Environment.NewLine, _visibleLines);
                LogTextBox.CaretIndex = LogTextBox.Text.Length;
            }

            LogTextBox.ScrollToEnd();
        }

        private void ClearLogButton_OnClick(object sender, RoutedEventArgs e)
        {
            DebugLogHub.Clear();
        }

        private void ClearVisibleLogs()
        {
            _visibleLines.Clear();
            LogTextBox.Clear();
        }

        private void RefreshUndoRedoButton_OnClick(object sender, RoutedEventArgs e)
        {
            RefreshUndoRedoView();
        }

        private void OnActiveCanvasChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshUndoRedoView));
                return;
            }

            RefreshUndoRedoView();
        }

        private void OnCommandHistoryChanged(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RefreshUndoRedoView));
                return;
            }

            RefreshUndoRedoView();
        }

        private int type = 3;
        private void ShowTipButton_Click(object sender, RoutedEventArgs e)
        {
            if (type == 1)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("恭喜！你所提交的信息已经审核通过。", ToastType.Info));
                type = 2;
            }
            else if (type == 2)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("系统将于 15 : 00 - 17 : 00 进行升级，请及时保存你的资料！", ToastType.Warning));
                type = 3;
            }
            else
            {
                EventBus.Instance.Publish(new ToastMessageEvent("系统错误，请稍后重试。", ToastType.Error));
                type = 1;
            }
        }
    }
}
