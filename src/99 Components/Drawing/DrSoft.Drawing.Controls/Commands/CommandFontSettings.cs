using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 字体设置命令：支持文本字体/内容修改的撤销/重做。
    /// 同时捕获 DrawObjectMemento（几何+变换）和文本专属状态（Text、FontSettings）。
    /// </summary>
    internal class CommandFontSettings : IDeferredCommand
    {
        private readonly List<TextSnapshot> _snapshots;
        public string Description { get; }

        public CommandFontSettings(IEnumerable<DrawText> textShapes, string description = "修改字体")
        {
            Description = description;
            _snapshots = textShapes.Select(t => new TextSnapshot(t)).ToList();
        }

        /// <summary>
        /// 在字体修改完成后调用，捕获 After 快照以支持 Redo。
        /// </summary>
        public void CaptureAfterState()
        {
            foreach (var s in _snapshots)
                s.CaptureAfter();
        }

        public void Execute()
        {
            foreach (var s in _snapshots)
                s.RestoreAfter();
            RefreshUI();
        }

        public bool Undo()
        {
            foreach (var s in _snapshots)
                s.RestoreBefore();
            RefreshUI();
            return true;
        }

        private void RefreshUI()
        {
            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                var shapes = _snapshots
                    .Select(snapshot => snapshot.Shape)
                    .Cast<IShape>()
                    .ToList();

                canvas.InvalidateVisibleCache();
                canvas.InvalidateGeometryCaches(shapes);
                canvas.SetSelectedShapes();
                canvas.RegenerateHatchForShapes(shapes);
            }

            DocumentContext.Instance?.RequestRedraw();
        }

        /// <summary>
        /// 单个 DrawText 的 Before/After 快照，包含基类 Memento 和文本专属属性。
        /// </summary>
        private class TextSnapshot
        {
            private readonly DrawText _shape;
            public DrawText Shape => _shape;

            // Before
            private readonly IShapeMemento _beforeMemento;
            private readonly string? _beforeText;
            private readonly FontSettings? _beforeFontSettings;

            // After (populated by CaptureAfter)
            private IShapeMemento? _afterMemento;
            private string? _afterText;
            private FontSettings? _afterFontSettings;

            public TextSnapshot(DrawText shape)
            {
                _shape = shape;
                _beforeMemento = shape.CaptureSnapshot();
                _beforeText = shape.TextModel?.Text;
                _beforeFontSettings = CloneFontSettings(shape.TextModel?.FontSettings);
            }

            public void CaptureAfter()
            {
                _afterMemento = _shape.CaptureSnapshot();
                _afterText = _shape.TextModel?.Text;
                _afterFontSettings = CloneFontSettings(_shape.TextModel?.FontSettings);
            }

            public void RestoreBefore()
            {
                ApplyTextState(_beforeText, _beforeFontSettings);
                _beforeMemento.Restore();
            }

            public void RestoreAfter()
            {
                if (_afterMemento == null)
                {
                    return;
                }

                ApplyTextState(_afterText, _afterFontSettings);
                _afterMemento.Restore();
            }

            private void ApplyTextState(string? text, FontSettings? fontSettings)
            {
                _shape.TextModel ??= new TextModel();
                _shape.TextModel.Text = text;
                _shape.TextModel.FontSettings = CloneFontSettings(fontSettings);
            }

            private static FontSettings? CloneFontSettings(FontSettings? source)
            {
                if (source == null) return null;
                return new FontSettings
                {
                    FontFamily = source.FontFamily,
                    FontSize = source.FontSize,
                    IsBold = source.IsBold,
                    IsItalic = source.IsItalic,
                    IsUnderline = source.IsUnderline,
                    IsVerticalLayout = source.IsVerticalLayout,
                    HorizontalAlign = source.HorizontalAlign,
                    VerticalAlign = source.VerticalAlign,
                    LineHeight = source.LineHeight,
                    CharacterSpacing = source.CharacterSpacing,
                    TextColor = source.TextColor
                };
            }
        }
    }
}
