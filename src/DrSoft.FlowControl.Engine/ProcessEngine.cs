using DrSoft.FlowControl.ProcessComponent;

namespace DrSoft.FlowControl.Engine
{
    /// <summary>
    /// 流程状态接口（状态模式）
    /// </summary>
    public interface IProcessState
    {
        string StateName { get; }
        Task EnterAsync(ProcessEngine engine);
        Task ExecuteAsync(ProcessEngine engine);
        Task PauseAsync(ProcessEngine engine);
        Task ResumeAsync(ProcessEngine engine);
        Task StopAsync(ProcessEngine engine);
        Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm);
        Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId);
    }

    /// <summary>
    /// 空闲状态
    /// </summary>
    public class IdleState : IProcessState
    {
        public string StateName => "空闲";

        public async Task EnterAsync(ProcessEngine engine)
        {
            engine.Context.State = ProcessState.Idle;
            engine.RaiseStateChanged(StateName);
        }

        public async Task ExecuteAsync(ProcessEngine engine)
        {
            await engine.TransitionToAsync(new RunningState());
            await engine.StartExecutionAsync();
        }

        public async Task PauseAsync(ProcessEngine engine) { }
        public async Task ResumeAsync(ProcessEngine engine) { }
        public async Task StopAsync(ProcessEngine engine) { }
        public async Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm) { }
        public async Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId) { }
    }

    /// <summary>
    /// 运行状态
    /// </summary>
    public class RunningState : IProcessState
    {
        public string StateName => "运行中";
        private Task _executionTask;

        public async Task EnterAsync(ProcessEngine engine)
        {
            engine.Context.State = ProcessState.Running;
            engine.RaiseStateChanged(StateName);
        }

        public async Task ExecuteAsync(ProcessEngine engine)
        {
            // 已经在执行中
        }

        public async Task PauseAsync(ProcessEngine engine)
        {
            await engine.TransitionToAsync(new PausedState());
        }

        public async Task ResumeAsync(ProcessEngine engine) { }

        public async Task StopAsync(ProcessEngine engine)
        {
            await engine.TransitionToAsync(new CancelledState());
            engine.CancelExecution();
        }

        public async Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm)
        {
            engine.Context.AddAlarm(alarm);
            await engine.TransitionToAsync(new AlarmState());
            engine.PauseExecution();
        }

        public async Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId) { }
    }

    /// <summary>
    /// 暂停状态
    /// </summary>
    public class PausedState : IProcessState
    {
        public string StateName => "已暂停";

        public async Task EnterAsync(ProcessEngine engine)
        {
            engine.Context.State = ProcessState.Paused;
            engine.RaiseStateChanged(StateName);
        }

        public async Task ExecuteAsync(ProcessEngine engine) { }

        public async Task PauseAsync(ProcessEngine engine) { }

        public async Task ResumeAsync(ProcessEngine engine)
        {
            await engine.TransitionToAsync(new RunningState());
            engine.ResumeExecution();
        }

        public async Task StopAsync(ProcessEngine engine)
        {
            await engine.TransitionToAsync(new CancelledState());
            engine.CancelExecution();
        }

        public async Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm)
        {
            engine.Context.AddAlarm(alarm);
            await engine.TransitionToAsync(new AlarmState());
        }

        public async Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId) { }
    }

    /// <summary>
    /// 报警状态
    /// </summary>
    public class AlarmState : IProcessState
    {
        public string StateName => "报警暂停";

        public async Task EnterAsync(ProcessEngine engine)
        {
            engine.RaiseStateChanged(StateName);
            engine.RaiseAlarmOccurred(engine.Context.ActiveAlarms);
        }

        public async Task ExecuteAsync(ProcessEngine engine) { }

        public async Task PauseAsync(ProcessEngine engine) { }

        public async Task ResumeAsync(ProcessEngine engine) { }

        public async Task StopAsync(ProcessEngine engine)
        {
            await engine.TransitionToAsync(new CancelledState());
            engine.CancelExecution();
        }

        public async Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm)
        {
            engine.Context.AddAlarm(alarm);
        }

        public async Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId)
        {
            if (string.IsNullOrEmpty(alarmId))
            {
                engine.Context.ClearAllAlarms("Operator");
            }
            else
            {
                engine.Context.ClearAlarm(alarmId, "Operator");
            }

            if (engine.Context.ActiveAlarms.Count == 0)
            {
                await engine.TransitionToAsync(new RunningState());
                engine.ResumeExecution();
            }
        }
    }

    /// <summary>
    /// 完成状态
    /// </summary>
    public class CompletedState : IProcessState
    {
        public string StateName => "已完成";

        public async Task EnterAsync(ProcessEngine engine)
        {
            engine.Context.State = ProcessState.Completed;
            engine.RaiseStateChanged(StateName);
            engine.RaiseProcessCompleted(engine.ProcessResult);
        }

        public async Task ExecuteAsync(ProcessEngine engine) { }
        public async Task PauseAsync(ProcessEngine engine) { }
        public async Task ResumeAsync(ProcessEngine engine) { }
        public async Task StopAsync(ProcessEngine engine) { }
        public async Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm) { }
        public async Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId) { }
    }

    /// <summary>
    /// 取消状态
    /// </summary>
    public class CancelledState : IProcessState
    {
        public string StateName => "已取消";

        public async Task EnterAsync(ProcessEngine engine)
        {
            engine.Context.State = ProcessState.Cancelled;
            engine.RaiseStateChanged(StateName);
            engine.RaiseProcessCancelled();
        }

        public async Task ExecuteAsync(ProcessEngine engine) { }
        public async Task PauseAsync(ProcessEngine engine) { }
        public async Task ResumeAsync(ProcessEngine engine) { }
        public async Task StopAsync(ProcessEngine engine) { }
        public async Task AlarmAsync(ProcessEngine engine, AlarmInfo alarm) { }
        public async Task AcknowledgeAlarmAsync(ProcessEngine engine, string alarmId) { }
    }

    /// <summary>
    /// 流程执行引擎（状态机）
    /// </summary>
    public class ProcessEngine
    {
        private IProcessState _currentState;
        private readonly CancellationTokenSource _cts;
        private Task _executionTask;

        public ProcessContext Context { get; }
        public IProcessStep RootFlow { get; set; }
        public StepResult ProcessResult { get; private set; }

        // 事件
        public event Action<string> OnStateChanged;
        public event Action<List<AlarmInfo>> OnAlarmOccurred;
        public event Action<StepResult> OnProcessCompleted;
        public event Action OnProcessCancelled;
        public event Action<StepProgress> OnProgress;

        // 事件触发方法（事件只能由声明它的类触发）
        internal void RaiseStateChanged(string state) => OnStateChanged?.Invoke(state);
        internal void RaiseAlarmOccurred(List<AlarmInfo> alarms) => OnAlarmOccurred?.Invoke(alarms);
        internal void RaiseProcessCompleted(StepResult result) => OnProcessCompleted?.Invoke(result);
        internal void RaiseProcessCancelled() => OnProcessCancelled?.Invoke();

        public ProcessEngine()
        {
            Context = new ProcessContext();
            _cts = new CancellationTokenSource();
            _currentState = new IdleState();

            // 设置上下文事件
            Context.OnAlarmTriggered += async (code, message) =>
            {
                await AlarmAsync(new AlarmInfo
                {
                    Code = code,
                    Message = message,
                    Level = AlarmLevel.Error,
                    OccurTime = DateTime.Now
                });
            };
        }

        public ProcessEngine(ProcessContext context)
        {
            Context = context;
            _cts = new CancellationTokenSource();
            _currentState = new IdleState();

            // 设置上下文事件
            Context.OnAlarmTriggered += async (code, message) =>
            {
                await AlarmAsync(new AlarmInfo
                {
                    Code = code,
                    Message = message,
                    Level = AlarmLevel.Error,
                    OccurTime = DateTime.Now
                });
            };
        }

        public async Task TransitionToAsync(IProcessState newState)
        {
            await _currentState?.EnterAsync(this);
            _currentState = newState;
            await _currentState.EnterAsync(this);
        }

        public async Task StartAsync(IProcessStep flow)
        {
            if (_currentState.StateName != "空闲")
                throw new InvalidOperationException($"无法启动，当前状态: {_currentState.StateName}");

            RootFlow = flow;
            await _currentState.ExecuteAsync(this);
        }

        internal async Task StartExecutionAsync()
        {
            Context.CancellationToken = _cts.Token;

            _executionTask = Task.Run(async () =>
            {
                try
                {
                    ProcessResult = await RootFlow.ExecuteAsync(Context,
                        new Progress<StepProgress>(p => OnProgress?.Invoke(p)));

                    if (ProcessResult.Success)
                    {
                        await TransitionToAsync(new CompletedState());
                    }
                    else if (ProcessResult.IsAlarm)
                    {
                        // 报警已由上下文处理
                    }
                    else
                    {
                        await TransitionToAsync(new CancelledState());
                    }
                }
                catch (OperationCanceledException)
                {
                    await TransitionToAsync(new CancelledState());
                }
                catch (Exception ex)
                {
                    await AlarmAsync(new AlarmInfo
                    {
                        Code = "EXCEPTION",
                        Message = ex.Message,
                        Level = AlarmLevel.Error,
                        OccurTime = DateTime.Now
                    });
                }
            });
        }

        public async Task PauseAsync()
        {
            await _currentState.PauseAsync(this);
        }

        internal void PauseExecution()
        {
            // 执行引擎会在下次循环检查暂停状态时自动暂停
        }

        public async Task ResumeAsync()
        {
            await _currentState.ResumeAsync(this);
        }

        internal void ResumeExecution()
        {
            // 执行引擎继续执行
        }

        public async Task StopAsync()
        {
            await _currentState.StopAsync(this);
        }

        internal void CancelExecution()
        {
            _cts.Cancel();
        }

        public async Task AlarmAsync(AlarmInfo alarm)
        {
            await _currentState.AlarmAsync(this, alarm);
        }

        public async Task AcknowledgeAlarmAsync(string alarmId = null)
        {
            await _currentState.AcknowledgeAlarmAsync(this, alarmId);
        }

        public async Task WaitForCompletionAsync()
        {
            if (_executionTask != null)
            {
                await _executionTask;
            }
        }

        public ProcessState GetCurrentState() => Context.State;
    }

    // 接口定义
    public interface IMotionController
    {
        Task MoveToAsync(double x, double y, double z);
        bool IsInPosition(double x, double y, double tolerance);
        double GetCurrentX();
        double GetCurrentY();
    }

    //public interface IMarkingController
    //{
    //    void SetPower(double power);
    //    void SetFrequency(double frequency);
    //    void SetSpeed(double speed);
    //    bool LoadMarkingData(byte[] data);
    //    bool StartMarking();
    //    bool IsBusy { get; }
    //    double GetProgress();
    //    MarkingResult GetResult();
    //}

    public interface IVisionSystem
    {
        Task<VisionResult> InspectAsync(string templateName);
    }

    //public class MarkingResult
    //{
    //    public bool Success { get; set; }
    //    public string ErrorMessage { get; set; }
    //    public double Duration { get; set; }
    //}

    public class VisionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public double Score { get; set; }
        public object Data { get; set; }
    }
}
