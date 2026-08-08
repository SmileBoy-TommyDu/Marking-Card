using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Collections;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 画布当前选中集的聚合对象。
    /// 统一承载多选的枚举、整体变换和选择框边界计算。
    /// </summary>
    public sealed class SelectionSet : ISelectionSet
    {
        private List<IShape> _items = [];

        public int Count => _items.Count;

        public IShape this[int index] => _items[index];

        ISelectionSet ISelectionSet.Transformables => new SelectionSet { _items = _items.Where(x => x.CanTransform).ToList() };
        internal void Reset(IEnumerable<IShape>? shapes)
        {
            _items = shapes?.ToList() ?? [];
        }

        public void Translate(float dx, float dy, bool commit = false)
        {
            foreach (var shape in _items)
            {
                shape.Translate(dx, dy, commit);
            }
        }

        public void Scale(float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = false)
        {
            foreach (var shape in _items)
            {
                shape.Scale(scaleX, scaleY, anchor, directionRad, commit);
            }
        }

        public void Rotate(float deltaAngle, SKPoint center, bool commit = false)
        {
            foreach (var shape in _items)
            {
                shape.Rotate(deltaAngle, center, commit);
            }
        }

        public void Skew(float skewX, float skewY, SKPoint anchor, bool commit = false)
        {
            foreach (var shape in _items)
            {
                shape.Skew(skewX, skewY, anchor, commit);
            }
        }

        public (SKPoint[] Corners, SKPoint Center) GetAABB2()
        {
            return Count switch
            {
                0 => (Array.Empty<SKPoint>(), SKPoint.Empty),
                1 => _items[0].GetAABB2(),
                _ => _items.GetUnionAABB().CreateBoundsGeometry()
            };
        }

        public (SKPoint[] Corners, SKPoint Center) GetOBB()
        {
            return Count switch
            {
                0 => (Array.Empty<SKPoint>(), SKPoint.Empty),
                1 => _items[0].GetOBB(),
                _ => _items.GetUnionOBB()
            };
        }

        public (SKPoint[] Corners, SKPoint Center) GetPreviewAABB()
        {
            return Count switch
            {
                0 => (Array.Empty<SKPoint>(), SKPoint.Empty),
                1 => _items[0].GetPreviewAABB(),
                _ => _items.GetUnionPreviewAABB().CreateBoundsGeometry()
            };
        }

        public (SKPoint[] Corners, SKPoint Center) GetPreviewOBB()
        {
            return Count switch
            {
                0 => (Array.Empty<SKPoint>(), SKPoint.Empty),
                1 => _items[0].GetPreviewOBB(),
                _ => _items.GetUnionPreviewOBB()
            };
        }

        public IEnumerator<IShape> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
