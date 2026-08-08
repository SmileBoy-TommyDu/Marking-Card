using DrSoft.Drawing.Model;
using SkiaSharp;


namespace DrSoft.Drawing.Controls.Models;

public class Viewport:IViewport
{
    private float _offsetX = 0;
    private float _offsetY = 0;
    private float _scale = 1.0f;
    private int _canvasWidth = 0;
    private int _canvasHeight = 0;
    private bool _hasValidCanvasSize = false;
    private DocumentContext _context = DocumentContext.Instance;
    public float OffsetX
    {
        get => _offsetX;
        private set => _offsetX = value;
    }

    public float OffsetY
    {
        get => _offsetY;
        private set => _offsetY = value;
    }

    public float Scale
    {
        get => _scale;
        private set => _scale = Math.Clamp(value, 0.05f, 50.0f);
    }

    public void SetCanvasSize(int width, int height)
    {        
        // 只在画布尺寸真正改变时才更新
        if (_canvasWidth != width || _canvasHeight != height)
        {
            _canvasWidth = width;
            _canvasHeight = height;
            
            if (width > 0 && height > 0)
            {
                // 始终将坐标原点居中到画布中心,标尺宽度为24像素
                _offsetX = width  / 2f + 24;
                _offsetY = height / 2f + 24;

                // 只在首次设置画布尺寸时计算默认缩放比例，后续 resize 保持用户缩放
                if (!_hasValidCanvasSize)
                {
                    _hasValidCanvasSize = true;

                    // 自动计算合适的缩放比例，使默认机台范围(-50到50)占据画布的1/3
                    // 机台范围总宽度：100单位
                    // 我们希望它占据画布宽度的1/3
                    float targetWidthInPixels = width / 2.5f;
                    _scale = targetWidthInPixels / Math.Max(_context.ActiveCanvas.MachineBounds.Width, _context.ActiveCanvas.MachineBounds.Height); // 100是机台范围的总宽度

                    // 确保缩放比例在有效范围内
                    _scale = Math.Clamp(_scale, 0.05f, 500.0f);
                    //保存初始值比例
                    DocumentContext.Instance.ActiveCanvas?.InitZoomPercent = _scale > 0?_scale:1;
                }
            }
        }
        else if (width > 0 && height > 0 && (_offsetX == 0 || _offsetY == 0))
        {
            _offsetX = width / 2f;
            _offsetY = height / 2f;
        }
    }

    public void Pan(float dx, float dy)
    {
        _offsetX += dx;
        _offsetY += dy;
    }

    public void ZoomAt(float factor, float sx, float sy)
    {
        // 计算新的缩放比例
        var newScale = _scale * factor;

        // 检查网格区域是否会小于画布范围
        // 当缩小时（factor < 1），检查是否应该停止缩放
        if (factor < 1.0)
        {
            // 获取基于网格范围的最小缩放比例
            float minScaleForGrid = GetMinScaleForGrid(50000f, 0.02f);

            // 确保新缩放比例不低于最小限制
            newScale = Math.Max(newScale, minScaleForGrid);
        }

        // 应用标准的缩放限制（0.05到50.0）
        newScale = (float)Math.Clamp(newScale, 0.005, 5000.0);

        // 如果缩放比例没有变化（因为达到了限制），则直接返回
        if (Math.Abs(newScale - _scale) < 0.0001f)
            return;

        var r = newScale / _scale;
        _offsetX = (float)(sx + (_offsetX - sx) * r);
        _offsetY = (float)(sy + (_offsetY - sy) * r);
        _scale = (float)newScale;
    }

    public SKRect ScreenToWorldRect(SKImageInfo info,SKRect sKRect)
    {
        // 替代方案：如果没有 Rect 转换方法
        var corners = new SKPoint[] {
            ScreenToWorld(new SKPoint(0, 0)),
            ScreenToWorld(new SKPoint(info.Width, 0)),
            ScreenToWorld(new SKPoint(0, info.Height)),
            ScreenToWorld(new SKPoint(info.Width, info.Height))
        };
        float minX = corners.Min(p => p.X);
        float maxX = corners.Max(p => p.X);
        float minY = corners.Min(p => p.Y);
        float maxY = corners.Max(p => p.Y);
        var worldBounds = SKRect.Create(minX, minY, maxX - minX, maxY - minY);
        return worldBounds;
    }

    public void Reset()
    {
        if (_canvasWidth > 0 && _canvasWidth > 0)
        {
            _offsetX = _canvasWidth / 2f;
            _offsetY = _canvasHeight / 2f;

            // 自动计算合适的缩放比例，使默认机台范围(-50到50)占据画布的1/3
            // 机台范围总宽度：100单位
            // 我们希望它占据画布宽度的1/3
            float targetWidthInPixels = _canvasWidth / 2.5f;
            _scale = targetWidthInPixels / Math.Max(_context.ActiveCanvas.MachineBounds.Width, _context.ActiveCanvas.MachineBounds.Height); // 100是机台范围的总宽度

            // 确保缩放比例在有效范围内
            _scale = Math.Clamp(_scale, 0.05f, 500.0f);
            //保存初始值比例
            DocumentContext.Instance.ActiveCanvas?.InitZoomPercent = _scale > 0 ? _scale : 1;
        }
    }

    public void ResetWithDefaultZoom(float targetScale = 3.0f)
    {
        Reset();
        _scale = targetScale;
    }

    // 计算基于网格范围的最小缩放比例
    // gridExtent: 网格从原点到边缘的距离（世界坐标）
    // margin: 边距比例（0-1），0表示网格刚好填满画布，0.2表示留20%边距
    public float GetMinScaleForGrid(float gridExtent = 50000f, float margin = 0.02f)
    {
        if (_canvasWidth == 0 || _canvasHeight == 0)
            return 0.05f; // 返回默认最小值

        // 计算网格在屏幕上的最小尺寸
        float gridTotalWidth = gridExtent * 2; // 网格总宽度
        float gridTotalHeight = gridExtent * 2; // 网格总高度

        // 计算在画布上显示完整网格所需的最小缩放比例
        float minScaleX = (_canvasWidth * (1 - margin)) / gridTotalWidth;
        float minScaleY = (_canvasHeight * (1 - margin)) / gridTotalHeight;

        return Math.Max(minScaleX, minScaleY);
    }

    public SKPoint WorldToScreen(SKPoint w) =>
        new((float)(w.X * Scale + OffsetX), (float)(-w.Y * Scale + OffsetY));
    public SKPoint ScreenToWorld(float sx, float sy) =>
        new((sx - OffsetX) / Scale, -(sy - OffsetY) / Scale);
    public SKPoint ScreenToWorld(SKPoint screenPoint, float? customZoom = null)
    {
        float currentZoom = customZoom ?? Scale;
        return new SKPoint(
            (screenPoint.X - (float)OffsetX) / currentZoom,
            -(screenPoint.Y - (float)OffsetY) / currentZoom);
    }

    /// <summary>
    /// 直接设置视口状态（用于缩放回退等场景）
    /// </summary>
    public void SetState(float scale, float offsetX, float offsetY)
    {
        _scale = Math.Clamp(scale, 0.05f, 500.0f);
        _offsetX = offsetX;
        _offsetY = offsetY;
    }

    /// <summary>
    /// 缩放到适合指定的世界坐标矩形区域，带边距
    /// </summary>
    /// <param name="worldRect">世界坐标矩形</param>
    /// <param name="margin">边距比例（0~1），例如 0.1 表示留 10% 边距</param>
    public void ZoomToFitRect(SKRect worldRect, float margin = 0.5f)
    {
        if (_canvasWidth <= 0 || _canvasHeight <= 0) return;
        if (worldRect.Width <= 0 && worldRect.Height <= 0) return;

        // 计算适配缩放比例
        float scaleX = _canvasWidth * (1 - margin) / Math.Max(worldRect.Width, 0.001f);
        float scaleY = _canvasHeight * (1 - margin) / Math.Max(worldRect.Height, 0.001f);
        float newScale = Math.Min(scaleX, scaleY);
        newScale = Math.Clamp(newScale, 0.05f, 500.0f);

        // 计算矩形中心的世界坐标
        float centerX = (worldRect.Left + worldRect.Right) / 2f;
        float centerY = (worldRect.Top + worldRect.Bottom) / 2f;

        // 设置新的缩放和偏移，使矩形居中
        _scale = newScale;
        _offsetX = _canvasWidth / 2f - centerX * _scale;
        _offsetY = _canvasHeight / 2f + centerY * _scale; // Y轴翻转
    }

    public float ScaleF(double w) => (float)(w * Scale);
}
