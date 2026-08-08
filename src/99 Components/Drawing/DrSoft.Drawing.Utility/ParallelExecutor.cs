namespace DrSoft.Drawing.Utility
{

    /// <summary>
    /// 并行执行器：封装 .NET 原生并行，支持控制线程数、数据量阈值、分批执行
    /// </summary>
    public static class ParallelExecutor
    {
        /// <summary>
        /// 默认阈值：数据量超过此值才启用并行
        /// </summary>
        public const int DefaultParallelThreshold = 1000;

        /// <summary>
        /// 默认每批大小
        /// </summary>
        public const int DefaultBatchSize = 500;

        // ══════════════════════════════════════════════════════
        //  ForEach：同步 Action
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 对集合执行 Action，数据量超过阈值时自动启用并行分批处理
        /// </summary>
        /// <param name="source">数据源</param>
        /// <param name="action">执行体</param>
        /// <param name="options">执行选项</param>
        public static void ForEach<T>(
            IReadOnlyList<T> source,
            Action<T> action,
            ParallelExecutorOptions? options = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (action == null) throw new ArgumentNullException(nameof(action));

            options ??= ParallelExecutorOptions.Default;

            if (source.Count <= options.ParallelThreshold)
            {
                // 数据量未达阈值，串行执行
                foreach (var item in source)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    action(item);
                }
                return;
            }

            // 并行分批执行
            var parallelOptions = BuildParallelOptions(options);
            var batches = Batch(source, options.BatchSize);

            Parallel.ForEach(batches, parallelOptions, batch =>
            {
                foreach (var item in batch)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    action(item);
                }
            });
        }

        /// <summary>
        /// 对集合执行 Action，带索引，数据量超过阈值时自动启用并行分批处理
        /// </summary>
        public static void ForEach<T>(
            IReadOnlyList<T> source,
            Action<T, int> action,
            ParallelExecutorOptions? options = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (action == null) throw new ArgumentNullException(nameof(action));

            options ??= ParallelExecutorOptions.Default;

            if (source.Count <= options.ParallelThreshold)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    action(source[i], i);
                }
                return;
            }

            var parallelOptions = BuildParallelOptions(options);
            var batches = BatchWithIndex(source, options.BatchSize);

            Parallel.ForEach(batches, parallelOptions, batch =>
            {
                foreach (var (item, index) in batch)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    action(item, index);
                }
            });
        }

        // ══════════════════════════════════════════════════════
        //  Select：有返回值，保序
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 对集合执行转换，数据量超过阈值时自动并行，结果保持原始顺序
        /// </summary>
        public static TResult[] Select<T, TResult>(
            IReadOnlyList<T> source,
            Func<T, TResult> selector,
            ParallelExecutorOptions? options = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            options ??= ParallelExecutorOptions.Default;

            var results = new TResult[source.Count];

            if (source.Count <= options.ParallelThreshold)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    results[i] = selector(source[i]);
                }
                return results;
            }

            // 并行写入固定槽位，天然保序
            var parallelOptions = BuildParallelOptions(options);
            var batches = BatchWithIndex(source, options.BatchSize);

            Parallel.ForEach(batches, parallelOptions, batch =>
            {
                foreach (var (item, index) in batch)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    results[index] = selector(item);
                }
            });

            return results;
        }

        // ══════════════════════════════════════════════════════
        //  ForEachAsync：异步 Func<Task>
        // ══════════════════════════════════════════════════════

        /// <summary>
        /// 异步版本：对集合执行异步 Action，数据量超过阈值时启用并行（控制并发度）
        /// </summary>
        public static async Task ForEachAsync<T>(
            IReadOnlyList<T> source,
            Func<T, CancellationToken, Task> action,
            ParallelExecutorOptions? options = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (action == null) throw new ArgumentNullException(nameof(action));

            options ??= ParallelExecutorOptions.Default;

            if (source.Count <= options.ParallelThreshold)
            {
                foreach (var item in source)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    await action(item, options.CancellationToken).ConfigureAwait(false);
                }
                return;
            }

            // .NET 6+ 原生 Parallel.ForEachAsync，天然支持并发度控制
            var parallelOptions = BuildParallelOptions(options);
            var batches = Batch(source, options.BatchSize);

            await Parallel.ForEachAsync(batches, parallelOptions, async (batch, ct) =>
            {
                foreach (var item in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    await action(item, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 异步版本：对集合执行转换，结果保持原始顺序
        /// </summary>
        public static async Task<TResult[]> SelectAsync<T, TResult>(
            IReadOnlyList<T> source,
            Func<T, CancellationToken, Task<TResult>> selector,
            ParallelExecutorOptions? options = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (selector == null) throw new ArgumentNullException(nameof(selector));

            options ??= ParallelExecutorOptions.Default;

            var results = new TResult[source.Count];

            if (source.Count <= options.ParallelThreshold)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    results[i] = await selector(source[i], options.CancellationToken).ConfigureAwait(false);
                }
                return results;
            }

            var parallelOptions = BuildParallelOptions(options);
            var batches = BatchWithIndex(source, options.BatchSize);

            await Parallel.ForEachAsync(batches, parallelOptions, async (batch, ct) =>
            {
                foreach (var (item, index) in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    results[index] = await selector(item, ct).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);

            return results;
        }

        // ══════════════════════════════════════════════════════
        //  内部辅助
        // ══════════════════════════════════════════════════════

        private static ParallelOptions BuildParallelOptions(ParallelExecutorOptions options) =>
            new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism > 0
                    ? options.MaxDegreeOfParallelism
                    : Environment.ProcessorCount,
                CancellationToken = options.CancellationToken,
            };

        /// <summary>
        /// 将列表按 batchSize 分批，返回每批的片段（不复制数据）
        /// </summary>
        private static IEnumerable<ArraySegment<T>> Batch<T>(IReadOnlyList<T> source, int batchSize)
        {
            // 转成数组以支持 ArraySegment（避免复制，直接引用原数组）
            var array = source is T[] arr ? arr : source.ToArray();
            for (int i = 0; i < array.Length; i += batchSize)
                yield return new ArraySegment<T>(array, i, Math.Min(batchSize, array.Length - i));
        }

        /// <summary>
        /// 分批并携带全局索引（用于保序写入结果数组）
        /// </summary>
        private static IEnumerable<(T Item, int Index)[]> BatchWithIndex<T>(IReadOnlyList<T> source, int batchSize)
        {
            int total = source.Count;
            for (int start = 0; start < total; start += batchSize)
            {
                int end = Math.Min(start + batchSize, total);
                var batch = new (T, int)[end - start];
                for (int i = start; i < end; i++)
                    batch[i - start] = (source[i], i);
                yield return batch;
            }
        }
    }

    // ══════════════════════════════════════════════════════
    //  选项类
    // ══════════════════════════════════════════════════════

    /// <summary>
    /// 并行执行选项
    /// </summary>
    public sealed class ParallelExecutorOptions
    {
        /// <summary>
        /// 启用并行的数据量阈值，低于此值串行执行（默认 1000）
        /// </summary>
        public int ParallelThreshold { get; init; } = ParallelExecutor.DefaultParallelThreshold;

        /// <summary>
        /// 每批数量（默认 500）
        /// </summary>
        public int BatchSize { get; init; } = ParallelExecutor.DefaultBatchSize;

        /// <summary>
        /// 最大并行线程数，0 或负数表示使用 CPU 核心数（默认 0）
        /// </summary>
        public int MaxDegreeOfParallelism { get; init; } = 0;

        /// <summary>
        /// 取消令牌
        /// </summary>
        public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

        public static readonly ParallelExecutorOptions Default = new();

        /// <summary>
        /// 快速构建选项
        /// </summary>
        public static ParallelExecutorOptions Create(
            int parallelThreshold = ParallelExecutor.DefaultParallelThreshold,
            int batchSize = ParallelExecutor.DefaultBatchSize,
            int maxDegreeOfParallelism = 0,
            CancellationToken cancellationToken = default) =>
            new ParallelExecutorOptions
            {
                ParallelThreshold = parallelThreshold,
                BatchSize = batchSize,
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken,
            };
    }
}