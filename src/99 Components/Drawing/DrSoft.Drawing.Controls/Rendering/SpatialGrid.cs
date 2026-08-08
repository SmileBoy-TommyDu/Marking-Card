using DrSoft.Drawing.Controls.DrawShapes;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace DrSoft.Drawing.Controls.Models
{
    /// <summary>
    /// 均匀网格空间索引，支持百万级图形的 O(k) 视口裁剪查询。
    /// <para>
    /// 原理：将世界空间划分为 Dim×Dim 个等大格子，构建时将每个图形插入其包围盒
    /// 覆盖的所有格子；查询时只枚举视口覆盖的格子，通过 UId 去重后返回候选集。
    /// </para>
    /// <para>
    /// 性能：Build O(n)，Query O(k)。k 为视口内格子包含的对象数量，当视口远小于
    /// 全局范围时 k ≪ n，查询速度比全量遍历快几个数量级。
    /// </para>
    /// </summary>
    public sealed class SpatialGrid
    {
        private readonly struct Entry
        {
            public Entry(DrawObject obj, SKRect bounds)
            {
                Object = obj;
                Bounds = bounds;
            }

            public DrawObject Object { get; }
            public SKRect Bounds { get; }
        }

        // 每轴格子数：128×128 = 16 384 个格子
        // 增大可提升查询精度但增加内存；减小可降低 Build 时跨格插入次数。
        private const int Dim = 128;

        // 每个格子存储与其相交的图形
        private readonly List<Entry>?[] _cells = new List<Entry>?[Dim * Dim];

        // 跨越过多格子的"大对象"单独存储，查询时做精确 AABB 检测
        private readonly List<Entry> _oversized = new();

        // 包围盒为空（点状或退化）的对象，无法参与空间剔除，始终返回
        private readonly List<DrawObject> _noBox = new();

        // 世界坐标原点（所有对象包围盒的最小角）
        private float _originX, _originY;

        // 格子尺寸的倒数（1 / 格子宽高），用于快速映射坐标→格子索引
        private float _invCellW, _invCellH;

        private volatile bool _built;
        // DrawObject stores the last query stamp it has seen. The stamp must be
        // process-wide, not per SpatialGrid instance, because indexes are rebuilt
        // while the same DrawObject instances remain alive.
        private static long s_queryStamp;

        /// <summary>空间索引是否已构建完毕。</summary>
        public bool IsBuilt => _built;

        // ──────────────────────────────────────────────────────────────────
        //  Build
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 从可见对象集合构建空间索引。
        /// 调用前需确保集合已完整填充（<see cref="DrawingCanvas.GetVisibleDrawObjects"/>
        /// 构建缓存后同步调用此方法）。
        /// </summary>
        public void Build(IEnumerable<DrawObject> objects)
        {
            ClearInternal();

            // Pass 1：收集所有对象的包围盒，同时计算全局包围盒
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            int entryCapacity = objects switch
            {
                ICollection<DrawObject> collection => collection.Count,
                IReadOnlyCollection<DrawObject> readOnlyCollection => readOnlyCollection.Count,
                _ => 65536
            };
            var entries = new List<Entry>(Math.Max(entryCapacity, 1024));

            foreach (var obj in objects)
            {
                var bb = obj.GetAABB();

                // 包围盒为空或退化为点 → 无法空间索引，加入 _noBox
                if (bb.IsEmpty)
                {
                    _noBox.Add(obj);
                    continue;
                }

                entries.Add(new Entry(obj, bb));

                if (bb.Left   < minX) minX = bb.Left;
                if (bb.Top    < minY) minY = bb.Top;
                if (bb.Right  > maxX) maxX = bb.Right;
                if (bb.Bottom > maxY) maxY = bb.Bottom;
            }

            if (entries.Count == 0)
            {
                _built = true;
                return;
            }

            // 在包围盒四周扩充 1%（最少 1 个世界单位），防止浮点误差导致索引越界
            float pw = Math.Max((maxX - minX) * 0.01f, 1f);
            float ph = Math.Max((maxY - minY) * 0.01f, 1f);

            _originX  = minX - pw;
            _originY  = minY - ph;
            float totalW = (maxX - minX) + pw * 2f;
            float totalH = (maxY - minY) + ph * 2f;

            _invCellW = Dim / totalW;
            _invCellH = Dim / totalH;

            // Pass 2：插入对象
            // 跨格数 > Dim/4 的大对象存入 _oversized，避免单个对象占满大量格子
            int oversizeThreshold = Dim / 4;
            int initialCellCapacity = Math.Clamp(entries.Count / (Dim * Dim), 8, 128);

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var obj = entry.Object;
                var bb = entry.Bounds;
                int c0 = CellX(bb.Left);
                int c1 = CellX(bb.Right);
                int r0 = CellY(bb.Top);
                int r1 = CellY(bb.Bottom);

                if ((c1 - c0 + 1) > oversizeThreshold ||
                    (r1 - r0 + 1) > oversizeThreshold)
                {
                    _oversized.Add(entry);
                    continue;
                }

                for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    int idx = r * Dim + c;
                    (_cells[idx] ??= new List<Entry>(initialCellCapacity)).Add(entry);
                }
            }

            _built = true;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Query
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// 查询与 <paramref name="worldRect"/> 相交的所有对象，并应用 LOD 剔除。
        /// </summary>
        /// <param name="worldRect">世界坐标查询矩形（通常为当前视口对应的世界矩形）。</param>
        /// <param name="scale">当前视口缩放比例，用于 LOD 屏幕尺寸计算。</param>
        /// <param name="lodMinScreenPixels">
        ///   LOD 最小屏幕尺寸（像素）。对象包围盒在屏幕上的最大维度（宽或高）
        ///   小于此值时将被跳过，以减少渲染开销。传 0 禁用 LOD 剔除。
        /// </param>
        /// <param name="maxCandidates">
        ///   渲染候选上限。大场景缩小时按网格均匀抽样，避免一次返回数百万对象。
        ///   传 0 表示不限制。
        /// </param>
        public List<DrawObject> Query(
            SKRect worldRect,
            float scale,
            float lodMinScreenPixels = 0.5f,
            int maxCandidates = 0)
        {
            if (!_built)
                return new List<DrawObject>();

            // 将查询矩形映射到格子索引范围
            int c0 = CellX(worldRect.Left);
            int c1 = CellX(worldRect.Right);
            int r0 = CellY(worldRect.Top);
            int r1 = CellY(worldRect.Bottom);

            long queryStamp = Interlocked.Increment(ref s_queryStamp);

            int queriedCells = Math.Max(1, (c1 - c0 + 1) * (r1 - r0 + 1));
            int maxPerCell = maxCandidates > 0
                ? Math.Max(1, maxCandidates / queriedCells)
                : int.MaxValue;

            int resultCapacity = maxCandidates > 0 ? Math.Min(maxCandidates, 20000) : 20000;
            if (_oversized.Count > 0)
                resultCapacity += Math.Min(_oversized.Count, 4096);
            if (_noBox.Count > 0)
                resultCapacity += Math.Min(_noBox.Count, 1024);

            var result = new List<DrawObject>(resultCapacity);

            // 大对象：优先补充精确 AABB 相交检测，避免候选被小对象填满后漏绘边框类对象。
            foreach (var entry in _oversized)
            {
                if (maxCandidates > 0 && result.Count >= maxCandidates)
                    break;

                var obj = entry.Object;
                if (!obj.TryMarkSpatialQuery(queryStamp)) continue;
                if (PassViewportFilters(entry.Bounds, worldRect, scale, lodMinScreenPixels))
                    result.Add(obj);
            }

            // 枚举视口覆盖的格子；每格达到采样数后立即停止扫该格，避免缩小全图时遍历百万级对象。
            for (int r = r0; r <= r1; r++)
            {
                if (maxCandidates > 0 && result.Count >= maxCandidates)
                    break;

                for (int c = c0; c <= c1; c++)
                {
                    if (maxCandidates > 0 && result.Count >= maxCandidates)
                        break;

                    var cell = _cells[r * Dim + c];
                    if (cell == null) continue;

                    int takenFromCell = 0;
                    foreach (var entry in cell)
                    {
                        if (maxCandidates > 0 &&
                            (result.Count >= maxCandidates || takenFromCell >= maxPerCell))
                        {
                            break;
                        }

                        var obj = entry.Object;
                        if (!obj.TryMarkSpatialQuery(queryStamp)) continue; // 跨格去重
                        if (!PassViewportFilters(entry.Bounds, worldRect, scale, lodMinScreenPixels))
                            continue;

                        result.Add(obj);
                        takenFromCell++;
                    }
                }
            }

            // 无包围盒对象无法空间剔除，只在候选上限内补充。
            foreach (var obj in _noBox)
            {
                if (maxCandidates > 0 && result.Count >= maxCandidates)
                    break;

                if (obj.TryMarkSpatialQuery(queryStamp))
                    result.Add(obj);
            }

            return result;
        }

        // ──────────────────────────────────────────────────────────────────
        //  Clear
        // ──────────────────────────────────────────────────────────────────

        /// <summary>清空索引，标记为未构建状态。</summary>
        public void Clear() => ClearInternal();

        private void ClearInternal()
        {
            for (int i = 0; i < _cells.Length; i++)
                _cells[i]?.Clear();
            _oversized.Clear();
            _noBox.Clear();
            _built = false;
        }

        // ──────────────────────────────────────────────────────────────────
        //  私有辅助
        // ──────────────────────────────────────────────────────────────────

        /// <summary>世界 X 坐标 → 格子列索引（已 Clamp）。</summary>
        private int CellX(float wx)
            => Math.Clamp((int)((wx - _originX) * _invCellW), 0, Dim - 1);

        /// <summary>世界 Y 坐标 → 格子行索引（已 Clamp）。</summary>
        private int CellY(float wy)
            => Math.Clamp((int)((wy - _originY) * _invCellH), 0, Dim - 1);

        /// <summary>
        /// 视口精确过滤：包围盒相交 + LOD。
        /// </summary>
        private static bool PassViewportFilters(SKRect bb, SKRect worldRect, float scale, float minPx)
        {
            if (!bb.IsEmpty)
            {
                if (!bb.IntersectsWith(worldRect))
                    return false;

                if (minPx > 0f)
                {
                    float screenSize = Math.Max(bb.Width, bb.Height) * scale;
                    if (screenSize < minPx)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// LOD 通过检测：对象包围盒在屏幕上的最大维度是否 ≥ <paramref name="minPx"/>。
        /// </summary>
        private static bool PassLod(DrawObject obj, float scale, float minPx)
        {
            if (minPx <= 0f) return true;
            var bb = obj.GetAABB();
            if (bb.IsEmpty) return true;
            float screenSize = Math.Max(bb.Width, bb.Height) * scale;
            return screenSize >= minPx;
        }
    }
}
