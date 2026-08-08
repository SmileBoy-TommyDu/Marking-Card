namespace DrSoft.Drawing.Contracts
{
    /// <summary>
    /// 图形组件通用错误码
    /// 按"失败原因"分类，而非按接口分类
    /// </summary>
    public enum GraphicErrorCode
    {
        // ── 0 成功 ──────────────────────────────────────────────────
        None = 0,

        // ── 1xx 调用方问题（参数 / 前置条件）────────────────────────
        /// <summary>参数为 null、格式非法、超出范围</summary>
        InvalidArgument = 100,

        /// <summary>需要先选中图形才能操作</summary>
        NothingSelected = 101,

        /// <summary>选中数量不足（如群组需要 ≥2 个）</summary>
        InsufficientSelection = 102,

        /// <summary>图形类型不符合操作要求（如非闭合路径无法填充）</summary>
        ShapeTypeMismatch = 103,

        /// <summary>目标对象不处于该操作要求的状态（如已是群组、已锁定）</summary>
        InvalidState = 104,

        /// <summary>当前上下文不支持此操作（如只读画布）</summary>
        OperationNotSupported = 105,

        // ── 2xx 资源不存在 ──────────────────────────────────────────
        /// <summary>图形对象不存在</summary>
        ShapeNotFound = 200,

        /// <summary>画布不存在或未就绪</summary>
        CanvasNotFound = 201,

        /// <summary>图层不存在</summary>
        LayerNotFound = 202,

        /// <summary>剪贴板为空或数据不可用</summary>
        ClipboardNotAvailable = 203,

        // ── 3xx 权限 / 保护 ─────────────────────────────────────────
        /// <summary>图层已锁定</summary>
        LayerLocked = 300,

        /// <summary>图形已锁定</summary>
        ShapeLocked = 301,

        /// <summary>图层不可见，拒绝写操作</summary>
        LayerNotVisible = 302,

        // ── 4xx 执行失败（业务计算层）───────────────────────────────
        /// <summary>操作执行失败，部分成功（如批量删除中部分被保护）</summary>
        PartialFailure = 400,

        /// <summary>计算结果为空（如图形太小导致填充无线段输出）</summary>
        EmptyResult = 401,

        /// <summary>几何计算失败（如路径自相交导致布尔运算异常）</summary>
        GeometryError = 402,

        // ── 5xx 系统 / 内部错误 ─────────────────────────────────────
        /// <summary>未知内部错误，附 InnerException 查看详情</summary>
        UnknownError = 500,

        /// <summary>功能尚未实现</summary>
        NotImplemented = 501,

        // ── 9xx 扩展（其他上位机自定义，从 900 起追加）─────────────
        VendorSpecificBase = 900,
    }

    /// <summary>
    /// 图形组件操作统一返回结果（无返回值版本）
    /// </summary>
    public sealed class GraphicResult
    {
        public bool IsSuccess => ErrorCode == GraphicErrorCode.None;
        public GraphicErrorCode ErrorCode { get; }
        public string Message { get; }          // 人类可读描述，可本地化
        public Exception? InnerException { get; } // 原始异常，仅调试用，可为 null

        private GraphicResult(GraphicErrorCode code, string message, Exception? ex = null)
        {
            ErrorCode = code;
            Message = message;
            InnerException = ex;
        }

        public static GraphicResult Ok() =>
            new(GraphicErrorCode.None, string.Empty);

        public static GraphicResult Fail(GraphicErrorCode code, string message = "", Exception? ex = null) =>
            new(code, message, ex);

        public override string ToString() =>
            IsSuccess ? "Success" : $"[{ErrorCode}] {Message}";
    }

    /// <summary>
    /// 图形组件操作统一返回结果（带返回值版本）
    /// </summary>
    public sealed class GraphicResult<T>
    {
        public bool IsSuccess => ErrorCode == GraphicErrorCode.None;
        public GraphicErrorCode ErrorCode { get; }
        public string Message { get; }
        public T? Value { get; }                 // 成功时的返回值
        public Exception? InnerException { get; }

        private GraphicResult(GraphicErrorCode code, string message, T? value, Exception? ex = null)
        {
            ErrorCode = code;
            Message = message;
            Value = value;
            InnerException = ex;
        }

        public static GraphicResult<T> Ok(T value) =>
            new(GraphicErrorCode.None, string.Empty, value);

        public static GraphicResult<T> Fail(GraphicErrorCode code, string message, Exception? ex = null) =>
            new(code, message, default, ex);
    }
}
