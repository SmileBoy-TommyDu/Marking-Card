using SkiaSharp;

namespace DrSoft.Drawing.Controls.DrawShapes
{
    internal readonly record struct TransformCommandSnapshot(
        SKMatrix Matrix,
        float Rotation,
        float ScaleX,
        float ScaleY,
        float SkewX,
        float SkewY,
        SKPoint RotationCenter,
        SKPoint ScaleAnchorPoint,
        SKPoint RotationCenterLocal);

    public abstract partial class DrawObject
    {
        #region 撤销/重做快照

        internal TransformCommandSnapshot CaptureTransformCommandSnapshot()
        {
            return new TransformCommandSnapshot(
                Matrix,
                Rotation,
                ScaleX,
                ScaleY,
                SkewX,
                SkewY,
                RotationCenter,
                ScaleAnchorPoint,
                RotationCenterLocal);
        }

        internal void RestoreTransformCommandSnapshot(TransformCommandSnapshot snapshot)
        {
            _matrix = snapshot.Matrix;
            _deltaMatrix = SKMatrix.Identity;
            SyncCommittedBoundsFromMatrix();
            OnCommittedMatrixChanged();

            _scaleAnchorPoint = SKPoint.Empty;

            Rotation = snapshot.Rotation;
            ScaleX = snapshot.ScaleX;
            ScaleY = snapshot.ScaleY;
            SkewX = snapshot.SkewX;
            SkewY = snapshot.SkewY;
            ScaleAnchorPoint = snapshot.ScaleAnchorPoint;
            RotationCenterLocal = snapshot.RotationCenterLocal;
            SetRotationCenter(snapshot.RotationCenter);

            _worldRotationCenter = snapshot.RotationCenter;

            BoundingBoxInvalidated?.Invoke(this);
        }

        #endregion
    }
}
