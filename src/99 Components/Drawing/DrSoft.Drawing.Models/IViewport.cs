using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Model
{
    public interface IViewport
    {
        float OffsetX { get;}
        float OffsetY { get;}
        float Scale { get;}
        void SetCanvasSize(int width, int height);
        void Pan(float dx, float dy);
        void ZoomAt(float factor, float sx, float sy);
        void Reset();
        void SetState(float scale, float offsetX, float offsetY);
        void ZoomToFitRect(SKRect worldRect, float margin = 0.1f);

        SKPoint ScreenToWorld(float sx, float sy);
        SKPoint ScreenToWorld(SKPoint screenPoint, float? customZoom = null);
        public SKRect ScreenToWorldRect(SKImageInfo info, SKRect sKRect);
        SKPoint WorldToScreen(SKPoint w);
    }
}
