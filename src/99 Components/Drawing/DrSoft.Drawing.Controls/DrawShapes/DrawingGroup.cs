using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Controls.Tools;
using DrSoft.Drawing.Model;
using SkiaSharp;
using System.Runtime.CompilerServices;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    /// <summary>
    /// 群组对象
    /// </summary>
    public class DrawingGroup : DrawObject, IContainer
    {
        // ── IShapeData.ChildShapes：重写基类虚方法，返回子图形集合 ────────────
        protected override IReadOnlyList<IShapeData> GetChildShapeData() =>
            Children.OfType<IShapeData>().ToArray();
        private SKRect? _cachedChildrenBounds;
        private bool _childrenBoundsDirty = true;

        public DrawingGroup()
        {
            UId = UniqueIdGenerator.NextId();
            Type = ShapeType.Group;
            Children = new ChildCollection(InvalidateChildCaches, () => _suppressChildPropagation);
        }

        public DrawingGroup(List<IShape> children) : this()
        {
            Children = new ChildCollection(children, InvalidateChildCaches, () => _suppressChildPropagation);
            SetRotationCenter(children.GetUnionAABB().Center());
        }

        internal bool _suppressChildPropagation;

        public ChildCollection Children { get; init; } = null!;

        public override IShape Clone()
        {
            var clone = new DrawingGroup
            {
            };
            clone.Children.AddRange(Children.Select(c => c.Clone()));
            return FinalizeClone(clone);            
        }

        internal List<IShape> CreateUngroupedChildren()
        {
            return Children.ToList();
        }

        public override bool HitTest(SKPoint point, float tolerance = 6.0f)
        {
            foreach (var child in Children)
            {
                if (child.HitTest(point, tolerance))
                    return true;
            }
            return false;
        }

        public override float GetDistanceToPath(SKPoint worldPoint)
        {
            float minDist = float.MaxValue;
            foreach (var child in Children)
            {
                float dist = child.GetDistanceToPath(worldPoint);
                if (dist < minDist) minDist = dist;
            }
            return minDist;
        }

        public override bool IntersectsWith(SKRect rect)
        {
            var skRect = new SKRect((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
            foreach (var child in Children)
            {
                if (child.GetAABB().IntersectsWith(skRect))
                    return true;
            }
            return false;
        }

        public override SKPath GetPath()
        {
            return new SKPath();
            //throw new NotImplementedException();
        }

        public override IEnumerable<IShape> Flatten()
        {
            return Children.SelectMany(c => c.Flatten());
        }

        /// <summary>
        /// 递归子级总数，O(1) 懒缓存。
        /// </summary>
        public override int FlattenCount => Children.FlattenCount;

        /// <summary>
        /// 使子级缓存失效（由 ChildCollection 回调）。
        /// </summary>
        private void InvalidateChildCaches()
        {
            _childrenBoundsDirty = true;
            _bboxDirty = true;
            NotifyBoundingBoxInvalidated();
        }


        #region BoundingBox Overrides

        private IEnumerable<DrawObject> GetDrawableChildren()
        {
            return Children?.OfType<DrawObject>() ?? Enumerable.Empty<DrawObject>();
        }

        private SKRect GetChildrenAabbBounds()
        {
            return GetDrawableChildren().GetUnionAABB();
        }

        private SKRect GetChildrenPreviewAabbBounds()
        {
            return GetDrawableChildren().GetUnionPreviewAABB();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewAABB()
        {
            return GetChildrenPreviewAabbBounds().CreateBoundsGeometry();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetPreviewOBB()
        {
            return GetChildrenPreviewAabbBounds().CreateBoundsGeometry();
        }

        public override SKRect GetAABB()
        {
            return GetChildrenAabbBounds();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetAABB2()
        {
            return GetChildrenAabbBounds().CreateBoundsGeometry();
        }

        public override (SKPoint[] Corners, SKPoint Center) GetOBB()
        {
            return GetChildrenAabbBounds().CreateBoundsGeometry();
        }
        #endregion

        #region Transform Overrides

        public override void Translate(float dx, float dy, bool commit = true)
        {
            foreach (var child in GetDrawableChildren())
            {
                child.Translate(dx, dy, commit);
            }

            base.Translate(dx, dy, commit);
        }

        public override void Scale(float scaleX, float scaleY, SKPoint anchor, float directionRad = 0f, bool commit = false)
        {
            foreach (var child in GetDrawableChildren())
            {
                child.Scale(scaleX, scaleY, anchor, directionRad, commit);
            }

            base.Scale(scaleX, scaleY, anchor, directionRad, commit);
        }

        public override void Rotate(float deltaAngle, SKPoint center, bool commit = false)
        {
            foreach (var child in GetDrawableChildren())
            {
                child?.Rotate(deltaAngle, center, commit);
            }

            base.Rotate(deltaAngle, center, commit);
        }

        public override void Skew(float tanSkewX, float tanSkewY, SKPoint anchor, bool commit = false)
        {
            foreach (var child in GetDrawableChildren())
            {
                child?.Skew(tanSkewX, tanSkewY, anchor, commit);
            }

            base.Skew(tanSkewX, tanSkewY, anchor, commit);
        }

        protected override SKRect ComputeCommittedAabbBounds()
        {
            return GetDrawableChildren().GetUnionAABB();
        }

        /// <summary>
        /// 旋转群组设置尺寸时，必须在群组局部坐标系内缩放子图形，
        /// 而非沿世界轴缩放（基类 ApplyScaling 是世界轴缩放，旋转后会导致选择框偏移）。
        /// </summary>
        internal override bool TryApplyDimension(float targetWidth, float targetHeight)
        {
            GetDrawableChildren().ToList().ForEach(child =>
            {
                child.TryApplyDimension(targetWidth, targetHeight);
            });

            return true;
        }
        #endregion

    }
}
