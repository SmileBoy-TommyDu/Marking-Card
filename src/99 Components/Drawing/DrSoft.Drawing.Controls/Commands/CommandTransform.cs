using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Models;
using DrSoft.Drawing.Model;
using System.Collections.Generic;
using System.Linq;

namespace DrSoft.Drawing.Controls.Commands
{
    /// <summary>
    /// 变换命令：记录图形的世界变换矩阵快照，以及 DrawObject.partial 中可写的 *2 变换属性。
    /// </summary>
    internal class CommandTransform : IDeferredCommand, ICoalescableCommand
    {
        private readonly DrawObject[] _shapes;
        private readonly TransformCommandSnapshot[] _beforeSnapshots;
        private TransformCommandSnapshot[]? _afterSnapshots;

        public string Description { get; }

        /// <summary>
        /// true 表示快照中包含了容器子图形；恢复容器自身时需要抑制子图形属性传播。
        /// </summary>
        private readonly bool _includesChildren;
        private readonly bool _allowMerge;

        public CommandTransform(
            IEnumerable<DrawObject> shapes,
            string description = "变换",
            bool includesChildren = true,
            bool allowMerge = false)
        {
            Description = description;
            _includesChildren = includesChildren;
            _allowMerge = allowMerge;
            _shapes = shapes as DrawObject[] ?? shapes.ToArray();
            _beforeSnapshots = _shapes.Select(shape => shape.CaptureTransformCommandSnapshot()).ToArray();
        }

        public void CaptureAfterState()
        {
            _afterSnapshots = _shapes.Select(shape => shape.CaptureTransformCommandSnapshot()).ToArray();
        }

        public void Execute()
        {
            if (_afterSnapshots == null)
                return;

            for (int i = 0; i < _shapes.Length; i++)
                RestoreSnapshot(i, _afterSnapshots[i]);

            RefreshUI();
        }

        public bool Undo()
        {
            for (int i = 0; i < _shapes.Length; i++)
                RestoreSnapshot(i, _beforeSnapshots[i]);

            RefreshUI();
            return true;
        }

        public void RefreshUI()
        {
            if (DocumentContext.Instance?.ActiveCanvas is DrawingCanvas canvas)
            {
                canvas.InvalidateVisibleCache();
                canvas.InvalidateGeometryCaches(_shapes);
                canvas.SetSelectedShapes();
                canvas.RegenerateHatchForShapes(_shapes);
            }

            DocumentContext.Instance?.RequestRedraw();
        }

        public bool TryMergeWith(ICoalescableCommand incoming)
        {
            if (incoming is not CommandTransform other)
                return false;

            if (!_allowMerge || !other._allowMerge)
                return false;

            if (other._shapes.Length != _shapes.Length)
                return false;

            if (other.Description != Description)
                return false;

            for (int i = 0; i < _shapes.Length; i++)
            {
                if (!ReferenceEquals(_shapes[i], other._shapes[i]))
                    return false;
            }

            if (other._afterSnapshots != null)
                _afterSnapshots = other._afterSnapshots;

            return true;
        }

        private void RestoreSnapshot(int i, TransformCommandSnapshot snapshot)
        {
            var shape = _shapes[i];

            bool wasComboSuppressed = false;
            bool wasGroupSuppressed = false;
            if (_includesChildren && shape is DrawCombination combo)
            {
                wasComboSuppressed = true;
                combo._suppressChildPropagation = true;
            }
            else if (_includesChildren && shape is DrawingGroup group)
            {
                wasGroupSuppressed = true;
                group._suppressChildPropagation = true;
            }

            try
            {
                shape.RestoreTransformCommandSnapshot(snapshot);
            }
            finally
            {
                if (wasComboSuppressed && shape is DrawCombination c)
                    c._suppressChildPropagation = false;
                else if (wasGroupSuppressed && shape is DrawingGroup g)
                    g._suppressChildPropagation = false;
            }
        }

        /// <summary>
        /// 递归收集图形及其子图形（排除关联 Hatch），用于容器图形的变换快照。
        /// </summary>
        public static DrawObject[] CollectWithChildren(IEnumerable<DrawObject> shapes)
        {
            int count = 0;
            foreach (var shape in shapes)
                CountRecursive(shape, ref count);

            var result = new DrawObject[count];
            int index = 0;
            foreach (var shape in shapes)
                CollectRecursive(shape, result, ref index);

            return result;
        }

        private static void CountRecursive(DrawObject shape, ref int count)
        {
            if (!shape.CanTransform)
                return;

            count++;
            if (shape is IContainer container)
            {
                for (int i = 0; i < container.Children.Count; i++)
                {
                    if (container.Children[i] is DrawObject childObj)
                        CountRecursive(childObj, ref count);
                }
            }
        }

        private static void CollectRecursive(DrawObject shape, DrawObject[] buffer, ref int index)
        {
            if (!shape.CanTransform)
                return;

            buffer[index++] = shape;
            if (shape is IContainer container)
            {
                for (int i = 0; i < container.Children.Count; i++)
                {
                    if (container.Children[i] is DrawObject childObj)
                        CollectRecursive(childObj, buffer, ref index);
                }
            }
        }
    }
}
