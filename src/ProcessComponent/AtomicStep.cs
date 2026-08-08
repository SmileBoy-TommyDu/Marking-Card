using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace DrSoft.FlowControl.ProcessComponent
{
    /// <summary>
    /// 流程步骤接口（组合模式）
    /// </summary>
    public interface IProcessStep
    {
        string Id { get; }
        string Name { get; set; }
        StepType Type { get; }
        Task<StepResult> ExecuteAsync(ProcessContext context, IProgress<StepProgress> progress = null);
        IProcessStep Clone();
        Dictionary<string, object> GetParameters();
        void SetParameters(Dictionary<string, object> parameters);
    }

    /// <summary>
    /// 步骤类型枚举
    /// </summary>
    public enum StepType
    {
        Atomic,         // 原子步骤
        Sequence,       // 顺序组合
        Parallel,       // 并行组合
        Conditional,    // 条件分支
        Loop,           // 循环
        SubProcess,     // 子流程
        Delay,          // 延迟
        Inspection      // 检测
    }

    /// <summary>
    /// 原子步骤基类（模板方法模式）
    /// </summary>
    public abstract class AtomicStep : IProcessStep
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public string Name { get; set; }
        public StepType Type => StepType.Atomic;
        public bool IsOptional { get; set; } = false;
        public TimeSpan EstimatedDuration { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();


        protected AtomicStep()
        {
        }

        /// <summary>
        /// 模板方法：定义执行流程骨架
        /// </summary>
        public async Task<StepResult> ExecuteAsync(ProcessContext context, IProgress<StepProgress> progress = null)
        {
            var result = new StepResult
            {
                StepId = Id,
                StepName = Name,
                StartTime = DateTime.Now
            };

            try
            {
                // 检查取消
                context.CancellationToken.ThrowIfCancellationRequested();

                // 检查是否跳过
                if (context.ShouldSkip(Id))
                {
                    result.Success = true;
                    result.Message = "步骤已跳过";
                    result.IsSkipped = true;
                    return result;
                }

                // 前置检查
                if (!await OnBeforeExecuteAsync(context))
                {
                    result.Success = false;
                    result.Message = "前置条件不满足";
                    return result;
                }

                // 报告开始
                progress?.Report(new StepProgress
                {
                    StepId = Id,
                    StepName = Name,
                    Percentage = 0,
                    Status = "开始执行"
                });

                // 执行核心逻辑
                var internalResult = await OnExecuteAsync(context, progress);

                result.Success = internalResult.Success;
                result.Message = internalResult.Message;
                result.Data = internalResult.Data;
                result.Duration = internalResult.Duration;

                // 报告完成
                progress?.Report(new StepProgress
                {
                    StepId = Id,
                    StepName = Name,
                    Percentage = 100,
                    Status = "执行完成"
                });

                // 后置处理
                await OnAfterExecuteAsync(context, result);

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "已取消";
                result.IsCancelled = true;
                return result;
            }
            catch (AlarmException ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                result.IsAlarm = true;
                result.AlarmCode = ex.AlarmCode;

                // 触发报警事件
                context.OnAlarmTriggered?.Invoke(ex.AlarmCode, ex.Message);

                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                result.EndTime = DateTime.Now;
                result.Duration = result.EndTime - result.StartTime;
            }
        }

        /// <summary>
        /// 前置检查（可重写）
        /// </summary>
        protected virtual Task<bool> OnBeforeExecuteAsync(ProcessContext context) => Task.FromResult(true);

        /// <summary>
        /// 核心执行逻辑（必须实现）
        /// </summary>
        protected abstract Task<StepResult> OnExecuteAsync(ProcessContext context, IProgress<StepProgress> progress = null);

        /// <summary>
        /// 后置处理（可重写）
        /// </summary>
        protected virtual Task OnAfterExecuteAsync(ProcessContext context, StepResult result) => Task.CompletedTask;

        public abstract IProcessStep Clone();

        public virtual Dictionary<string, object> GetParameters() => Parameters;

        public virtual void SetParameters(Dictionary<string, object> parameters)
        {
            Parameters = parameters;
        }
    }

    /// <summary>
    /// 步骤执行结果
    /// </summary>
    public class StepResult
    {
        public string StepId { get; set; }
        public string StepName { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public object Data { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsSkipped { get; set; }
        public bool IsCancelled { get; set; }
        public bool IsAlarm { get; set; }
        public string AlarmCode { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }

        public static StepResult Succeeded(string message = null, object data = null) =>
            new StepResult { Success = true, Message = message, Data = data };

        public static StepResult Failed(string message) =>
            new StepResult { Success = false, Message = message };

        public static StepResult Alarm(string alarmCode, string message) =>
            new StepResult { Success = false, IsAlarm = true, AlarmCode = alarmCode, Message = message };
    }

    /// <summary>
    /// 步骤进度信息
    /// </summary>
    public class StepProgress
    {
        public string StepId { get; set; }
        public string StepName { get; set; }
        public double Percentage { get; set; }
        public string Status { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 流程上下文
    /// </summary>
    public class ProcessContext
    {
        private readonly ConcurrentDictionary<string, object> _data = new ConcurrentDictionary<string, object>();
        private readonly HashSet<string> _skippedSteps = new HashSet<string>();
        private readonly List<ExecutionSnapshot> _snapshots = new List<ExecutionSnapshot>();

        public string ProcessId { get; set; } = Guid.NewGuid().ToString();
        public CancellationToken CancellationToken { get; set; }
        public ProcessState State { get; set; } = ProcessState.Idle;
        public List<AlarmInfo> ActiveAlarms { get; set; } = new List<AlarmInfo>();
        public Dictionary<string, object> GlobalVariables { get; set; } = new Dictionary<string, object>();
        public int CurrentStepIndex { get; set; }
        public string CurrentStepId { get; set; }

        // 事件
        public Action<string, string> OnAlarmTriggered { get; set; }
        public Action<string> OnAlarmCleared { get; set; }
        public Action<StepResult> OnStepCompleted { get; set; }

        public T GetData<T>(string key) => _data.TryGetValue(key, out var value) ? (T)value : default;
        public void SetData(string key, object value) => _data[key] = value;
        public bool HasData(string key) => _data.ContainsKey(key);
        public void RemoveData(string key) => _data.TryRemove(key, out _);

        public void SkipStep(string stepId) => _skippedSteps.Add(stepId);
        public bool ShouldSkip(string stepId) => _skippedSteps.Contains(stepId);
        public void ClearSkipped() => _skippedSteps.Clear();

        public double CurrentX { get; set; }
        public double CurrentY { get; set; }
        public double CurrentZ { get; set; }


        // ==================== 服务容器 ====================

        /// <summary>
        /// 服务存储字典（线程安全）
        /// </summary>
        private readonly ConcurrentDictionary<Type, object> _services = new ConcurrentDictionary<Type, object>();

        /// <summary>
        /// 服务工厂字典（用于延迟创建）
        /// </summary>
        private readonly ConcurrentDictionary<Type, Func<object>> _serviceFactories = new ConcurrentDictionary<Type, Func<object>>();

        /// <summary>
        /// 注册服务实例
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <param name="service">服务实例</param>
        public void RegisterService<T>(T service) where T : class
        {
            var serviceType = typeof(T);
            _services[serviceType] = service;

            // 同时注册接口的所有基接口
            foreach (var interfaceType in serviceType.GetInterfaces())
            {
                _services[interfaceType] = service;
            }
        }

        /// <summary>
        /// 注册服务工厂（延迟创建）
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <param name="factory">服务工厂</param>
        public void RegisterServiceFactory<T>(Func<T> factory) where T : class
        {
            _serviceFactories[typeof(T)] = () => factory();
        }

        /// <summary>
        /// 获取服务
        /// </summary>
        /// <typeparam name="T">服务类型</typeparam>
        /// <returns>服务实例，不存在则返回null</returns>
        public T GetService<T>() where T : class
        {
            var serviceType = typeof(T);

            // 1. 先查找已注册的实例
            if (_services.TryGetValue(serviceType, out var service))
            {
                return service as T;
            }

            // 2. 尝试使用工厂创建
            if (_serviceFactories.TryGetValue(serviceType, out var factory))
            {
                var newService = factory();
                _services[serviceType] = newService;
                return newService as T;
            }

            return null;
        }

        /// <summary>
        /// 获取必需服务（不存在时抛出异常）
        /// </summary>
        public T GetRequiredService<T>() where T : class
        {
            var service = GetService<T>();
            if (service == null)
            {
                throw new InvalidOperationException($"服务 {typeof(T).Name} 未注册");
            }
            return service;
        }

        /// <summary>
        /// 检查服务是否已注册
        /// </summary>
        public bool IsServiceRegistered<T>() where T : class
        {
            return _services.ContainsKey(typeof(T)) || _serviceFactories.ContainsKey(typeof(T));
        }

        /// <summary>
        /// 移除服务
        /// </summary>
        public bool RemoveService<T>() where T : class
        {
            return _services.TryRemove(typeof(T), out _);
        }

        /// <summary>
        /// 获取所有已注册的服务类型
        /// </summary>
        public IEnumerable<Type> GetRegisteredServiceTypes()
        {
            return _services.Keys;
        }

        /// <summary>
        /// 清除所有服务
        /// </summary>
        public void ClearServices()
        {
            _services.Clear();
            _serviceFactories.Clear();
        }

        public void SaveSnapshot(string name)
        {
            _snapshots.Add(new ExecutionSnapshot
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                Timestamp = DateTime.Now,
                StepIndex = CurrentStepIndex,
                StepId = CurrentStepId,
                Data = new Dictionary<string, object>(_data)
            });
        }

        public ExecutionSnapshot GetLastSnapshot() => _snapshots.LastOrDefault();

        public void RestoreSnapshot(ExecutionSnapshot snapshot)
        {
            if (snapshot?.Data != null)
            {
                _data.Clear();
                foreach (var kv in snapshot.Data)
                {
                    _data[kv.Key] = kv.Value;
                }
                CurrentStepIndex = snapshot.StepIndex;
                CurrentStepId = snapshot.StepId;
            }
        }

        public void AddAlarm(AlarmInfo alarm)
        {
            ActiveAlarms.Add(alarm);
            State = ProcessState.Alarm;
            OnAlarmTriggered?.Invoke(alarm.Code, alarm.Message);
        }

        public void ClearAlarm(string alarmId, string clearedBy)
        {
            var alarm = ActiveAlarms.FirstOrDefault(a => a.Id == alarmId);
            if (alarm != null)
            {
                alarm.Clear(clearedBy);
                ActiveAlarms.Remove(alarm);
                OnAlarmCleared?.Invoke(alarmId);

                if (ActiveAlarms.Count == 0 && State == ProcessState.Alarm)
                {
                    State = ProcessState.Running;
                }
            }
        }

        public void ClearAllAlarms(string clearedBy)
        {
            foreach (var alarm in ActiveAlarms)
            {
                alarm.Clear(clearedBy);
            }
            ActiveAlarms.Clear();
            OnAlarmCleared?.Invoke("All");

            if (State == ProcessState.Alarm)
            {
                State = ProcessState.Running;
            }
        }
    }

    /// <summary>
    /// 执行快照（备忘录模式）
    /// </summary>
    public class ExecutionSnapshot
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime Timestamp { get; set; }
        public int StepIndex { get; set; }
        public string StepId { get; set; }
        public Dictionary<string, object> Data { get; set; }
    }

    /// <summary>
    /// 报警信息
    /// </summary>
    public class AlarmInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Code { get; set; }
        public string Message { get; set; }
        public AlarmLevel Level { get; set; }
        public DateTime OccurTime { get; set; }
        public bool IsAcknowledged { get; set; }
        public DateTime? AcknowledgedTime { get; set; }
        public string AcknowledgedBy { get; set; }
        public bool IsCleared { get; set; }
        public DateTime? ClearedTime { get; set; }
        public string ClearedBy { get; set; }

        public void Acknowledge(string by)
        {
            IsAcknowledged = true;
            AcknowledgedTime = DateTime.Now;
            AcknowledgedBy = by;
        }

        public void Clear(string by)
        {
            IsCleared = true;
            ClearedTime = DateTime.Now;
            ClearedBy = by;
        }
    }

    public enum AlarmLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public enum ProcessState
    {
        Idle,       // 空闲
        Running,    // 运行中
        Paused,     // 手动暂停
        Alarm,      // 报警暂停
        Completed,  // 已完成
        Cancelled   // 已取消
    }

    /// <summary>
    /// 报警异常
    /// </summary>
    public class AlarmException : Exception
    {
        public string AlarmCode { get; }
        public AlarmLevel Level { get; }

        public AlarmException(string alarmCode, string message, AlarmLevel level = AlarmLevel.Error)
            : base(message)
        {
            AlarmCode = alarmCode;
            Level = level;
        }
    }
}


