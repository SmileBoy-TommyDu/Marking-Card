using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Controls.Interface;
using DrSoft.Drawing.Model;
using DrSoft.Drawing.Rendering;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Rendering
{
    /// <summary>
    /// 使用 SKShader 纹理填充的渲染器（GPU加速，性能最优）
    /// </summary>
    public class HatchShaderRenderer
    {
        private readonly Dictionary<string, ShaderCacheEntry> _shaderCache = new();
        private readonly object _cacheLock = new();

        private const int MAX_CACHE_SIZE = 80;
        private const int MAX_TEXTURE_SIZE = 256;
        private const int MIN_TEXTURE_SIZE = 32;
        private const float CACHE_SCALE_THRESHOLD = 0.25f;

        /// <summary>
        /// 使用 Shader 渲染填充
        /// </summary>
        public void RenderHatchWithShader(
            DrawRectangle rectangle,
            List<(SKPoint Start, SKPoint End)> lines,
            SKCanvas canvas,
            float viewportScale)
        {
            if (rectangle == null || lines == null || lines.Count == 0) return;

            canvas.Save();

            var matrix = rectangle.GetTransformMatrix();
            canvas.Concat(ref matrix);

            var shader = GetOrCreateShader(rectangle, lines, viewportScale);
            if (shader == null) return;

            using (var paint = new SKPaint())
            {
                paint.Shader = shader;
                paint.IsAntialias = false;
                paint.Style = SKPaintStyle.Fill;

                var rect = new SKRect(0, 0, (float)rectangle.Width, (float)rectangle.Height);
                canvas.DrawRect(rect, paint);
            }

            canvas.Restore();
        }

        /// <summary>
        /// 获取或创建 Shader（带缓存）
        /// </summary>
        private SKShader GetOrCreateShader(DrawRectangle rectangle, List<(SKPoint Start, SKPoint End)> lines, float scale)
        {
            string cacheKey = GenerateCacheKey(rectangle, scale);

            lock (_cacheLock)
            {
                if (_shaderCache.TryGetValue(cacheKey, out var entry))
                {
                    if (Math.Abs(entry.Scale - scale) / Math.Max(scale, 0.001f) <= CACHE_SCALE_THRESHOLD)
                    {
                        entry.LastAccessTime = DateTime.Now;
                        return entry.Shader;
                    }

                    entry.Shader?.Dispose();
                    _shaderCache.Remove(cacheKey);
                }
            }

            var newShader = CreateHatchShader(rectangle, lines, scale);
            if (newShader == null) return null;

            lock (_cacheLock)
            {
                if (_shaderCache.Count >= MAX_CACHE_SIZE)
                {
                    var toRemove = _shaderCache
                        .OrderBy(x => x.Value.LastAccessTime)
                        .Take(MAX_CACHE_SIZE / 4)
                        .ToList();

                    foreach (var item in toRemove)
                    {
                        item.Value.Shader?.Dispose();
                        _shaderCache.Remove(item.Key);
                    }
                }

                _shaderCache[cacheKey] = new ShaderCacheEntry
                {
                    Shader = newShader,
                    Scale = scale,
                    LastAccessTime = DateTime.Now
                };
            }

            return newShader;
        }

        /// <summary>
        /// 生成缓存键（修正版）
        /// </summary>
        private string GenerateCacheKey(DrawRectangle rectangle, float scale)
        {
            var hatchInfo = rectangle.HatchParamInfo;
            int discreteScale = (int)(scale * 10);

            // 使用 StringBuilder 高效构建
            var sb = new StringBuilder(256);
            sb.Append(rectangle.GetType().Name);
            sb.Append('_');
            sb.Append(hatchInfo.FillStyleIndex);
            sb.Append('_');
            sb.Append(hatchInfo.FillColor ?? "#000000");
            sb.Append('_');
            sb.Append(hatchInfo.LineSpacing);
            sb.Append('_');
            sb.Append(hatchInfo.StartAngle);
            sb.Append('_');
            sb.Append(hatchInfo.IncrementalAngle);
            sb.Append('_');
            sb.Append(hatchInfo.Margin);
            sb.Append('_');
            sb.Append(hatchInfo.Extension);
            sb.Append('_');
            sb.Append(hatchInfo.FillTypeIndex);
            sb.Append('_');
            sb.Append(hatchInfo.AverageDistribute);
            sb.Append('_');
            sb.Append(hatchInfo.InternalRings);
            sb.Append('_');
            sb.Append(hatchInfo.DirectionTypeIndex);
            sb.Append('_');
            sb.Append(hatchInfo.RelativeToAngle);
            sb.Append('_');
            sb.Append(hatchInfo.ReverseFillLine);
            sb.Append('_');
            sb.Append(discreteScale);

            return sb.ToString();
        }

        /// <summary>
        /// 创建填充纹理 Shader
        /// </summary>
        private SKShader CreateHatchShader(DrawRectangle rectangle, List<(SKPoint Start, SKPoint End)> lines, float scale)
        {
            try
            {
                var hatchInfo = rectangle.HatchParamInfo;

                var textureSize = CalculateTextureSize(lines, scale, hatchInfo);
                var patternBounds = CalculatePatternBounds(lines, textureSize);

                using (var bitmap = CreatePatternBitmap(lines, hatchInfo.FillColor, patternBounds, scale, hatchInfo.LineSpacing))
                {
                    if (bitmap == null) return null;

                    return SKShader.CreateBitmap(
                        bitmap,
                        SKShaderTileMode.Repeat,
                        SKShaderTileMode.Repeat);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"创建 Shader 失败: {ex.Message}");
                return null;
            }
        }

        private SKSize CalculateTextureSize(
            List<(SKPoint Start, SKPoint End)> lines,
            float scale,
            DTO.HatchParamDto hatchInfo)
        {
            float targetSize;

            if (hatchInfo.LineSpacing > 0)
            {
                float screenSpacing = (float)hatchInfo.LineSpacing * scale;
                targetSize = screenSpacing * 4f;
            }
            else
            {
                float avgGap = EstimateAverageGap(lines);
                float screenGap = avgGap * scale;
                targetSize = screenGap * 4f;
            }

            targetSize = Math.Clamp(targetSize, MIN_TEXTURE_SIZE, MAX_TEXTURE_SIZE);

            int pow2Size = 16;
            while (pow2Size < targetSize)
                pow2Size *= 2;

            pow2Size = Math.Clamp(pow2Size, MIN_TEXTURE_SIZE, MAX_TEXTURE_SIZE);

            return new SKSize(pow2Size, pow2Size);
        }

        private SKRect CalculatePatternBounds(List<(SKPoint Start, SKPoint End)> lines, SKSize textureSize)
        {
            if (lines.Count == 0)
                return new SKRect(0, 0, textureSize.Width, textureSize.Height);

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var line in lines)
            {
                minX = Math.Min(minX, Math.Min(line.Start.X, line.End.X));
                minY = Math.Min(minY, Math.Min(line.Start.Y, line.End.Y));
                maxX = Math.Max(maxX, Math.Max(line.Start.X, line.End.X));
                maxY = Math.Max(maxY, Math.Max(line.Start.Y, line.End.Y));
            }

            float width = maxX - minX;
            float height = maxY - minY;

            if (width > 100f || width < 0.1f)
                width = 20f;
            if (height > 100f || height < 0.1f)
                height = 20f;

            width = Math.Clamp(width, 5f, textureSize.Width);
            height = Math.Clamp(height, 5f, textureSize.Height);

            return new SKRect(0, 0, width, height);
        }

        private SKBitmap CreatePatternBitmap(
            List<(SKPoint Start, SKPoint End)> lines,
            string colorHex,
            SKRect patternBounds,
            float scale,
            double lineSpacing)
        {
            int texWidth = (int)Math.Ceiling(patternBounds.Width);
            int texHeight = (int)Math.Ceiling(patternBounds.Height);

            texWidth = Math.Clamp(texWidth, MIN_TEXTURE_SIZE, MAX_TEXTURE_SIZE);
            texHeight = Math.Clamp(texHeight, MIN_TEXTURE_SIZE, MAX_TEXTURE_SIZE);

            var bitmap = new SKBitmap(texWidth, texHeight, SKColorType.Rgba8888, SKAlphaType.Premul);

            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);

                float scaleX = texWidth / patternBounds.Width;
                float scaleY = texHeight / patternBounds.Height;
                canvas.Scale(scaleX, scaleY);
                canvas.Translate(-patternBounds.Left, -patternBounds.Top);

                SKColor color = ParseColor(colorHex);
                float strokeWidth = CalculateStrokeWidth(scale, lineSpacing);

                using (var paint = new SKPaint())
                {
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = strokeWidth;
                    paint.Color = color;
                    paint.IsAntialias = true;
                    paint.StrokeCap = SKStrokeCap.Square;

                    int drawnCount = 0;
                    foreach (var line in lines)
                    {
                        if (IsLineInBounds(line, patternBounds))
                        {
                            canvas.DrawLine(line.Start, line.End, paint);
                            drawnCount++;
                        }

                        if (drawnCount >= 500) break;
                    }
                }

                canvas.Flush();
            }

            return bitmap;
        }

        private float CalculateStrokeWidth(double scale, double lineSpacing)
        {
            float targetWidth = (float)(lineSpacing * 0.08f);
            float screenWidth = targetWidth * (float)scale;
            screenWidth = Math.Clamp(screenWidth, 0.5f, 3f);
            return screenWidth / (float)scale;
        }

        private SKColor ParseColor(string colorHex)
        {
            if (string.IsNullOrEmpty(colorHex))
                return SKColors.Black;

            try
            {
                if (colorHex.StartsWith("#"))
                    return SKColor.Parse(colorHex);
                else
                    return SKColor.Parse("#" + colorHex);
            }
            catch
            {
                return SKColors.Black;
            }
        }

        private float EstimateAverageGap(List<(SKPoint Start, SKPoint End)> lines)
        {
            if (lines.Count < 2) return 10f;

            int sampleCount = Math.Min(30, lines.Count - 1);
            float totalDist = 0;
            int validCount = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                var mid1 = new SKPoint(
                    (lines[i].Start.X + lines[i].End.X) * 0.5f,
                    (lines[i].Start.Y + lines[i].End.Y) * 0.5f);
                var mid2 = new SKPoint(
                    (lines[i + 1].Start.X + lines[i + 1].End.X) * 0.5f,
                    (lines[i + 1].Start.Y + lines[i + 1].End.Y) * 0.5f);

                float dx = mid2.X - mid1.X;
                float dy = mid2.Y - mid1.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                if (dist > 0.001f && dist < 1000f)
                {
                    totalDist += dist;
                    validCount++;
                }
            }

            return validCount > 0 ? totalDist / validCount : 10f;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsLineInBounds((SKPoint Start, SKPoint End) line, SKRect bounds)
        {
            float minX = Math.Min(line.Start.X, line.End.X);
            float maxX = Math.Max(line.Start.X, line.End.X);
            float minY = Math.Min(line.Start.Y, line.End.Y);
            float maxY = Math.Max(line.Start.Y, line.End.Y);

            return !(maxX < bounds.Left || minX > bounds.Right ||
                     maxY < bounds.Top || minY > bounds.Bottom);
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                foreach (var entry in _shaderCache.Values)
                {
                    entry.Shader?.Dispose();
                }
                _shaderCache.Clear();
            }
        }

        private class ShaderCacheEntry
        {
            public SKShader Shader { get; set; }
            public float Scale { get; set; }
            public DateTime LastAccessTime { get; set; }
        }
    }
}
