using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Clipboard
{
    public sealed class DrawingClipboard
    {
        /// <summary>全局共享剪贴板实例（跨画布共享）</summary>
        public static DrawingClipboard Instance { get; } = new();

        private static readonly SKPoint DefaultPasteOffsetStep = new(2f, -2f);
        private readonly List<IShape> _items = new();
        private int _pasteGeneration;
        public bool HasContent => _items.Count > 0;

        /// <summary>将图形写入剪贴板（深拷贝）</summary>
        public void Set(IEnumerable<IShape> shapes)
        {
            _items.Clear();
            _pasteGeneration = 0;
            _items.AddRange(CloneClipboardBatch(shapes));
        }

        /// <summary>
        /// 从剪贴板取出副本。
        /// 默认按固定步进相对原始位置偏移，避免与源图形完全重叠。
        /// 传入 false 时保持原始绝对坐标。
        /// 调用方直接 Add 到画布，不会影响剪贴板内容。
        /// </summary>
        public IReadOnlyList<IShape> Paste(bool useMousePosition = true)
        {
            if (!HasContent) return Array.Empty<IShape>();

            var translation = SKPoint.Empty;
            if (useMousePosition)
            {
                _pasteGeneration++;
                translation = new SKPoint(
                    DefaultPasteOffsetStep.X * _pasteGeneration,
                    DefaultPasteOffsetStep.Y * _pasteGeneration);
            }

            var result = CloneClipboardBatch(_items);
            foreach (var copy in result)
            {
                if (translation != SKPoint.Empty)
                {
                    copy.Translate(translation.X, translation.Y, commit: true);
                }
            }
            return result;
        }

        private static List<IShape> CloneClipboardBatch(IEnumerable<IShape> shapes)
        {
            var sourceList = shapes.ToList();
            var cloneList = sourceList.Select(shape => shape.Clone()).ToList();

            var topLevelCloneMap = new Dictionary<int, IShape>(sourceList.Count);
            for (int i = 0; i < sourceList.Count; i++)
            {
                topLevelCloneMap[sourceList[i].UId] = cloneList[i];
            }

            for (int i = 0; i < sourceList.Count; i++)
            {
                if (sourceList[i] is not DrawingHatch sourceHatch || cloneList[i] is not DrawingHatch cloneHatch)
                    continue;

                cloneHatch.Boundaries.Clear();
                foreach (var boundary in sourceHatch.Boundaries)
                {
                    if (topLevelCloneMap.TryGetValue(boundary.UId, out var reboundBoundary))
                    {
                        cloneHatch.Boundaries.Add(reboundBoundary);
                    }
                    else
                    {
                        cloneHatch.Boundaries.Add(boundary.Clone());
                    }
                }
            }

            return cloneList;
        }
    }
}
