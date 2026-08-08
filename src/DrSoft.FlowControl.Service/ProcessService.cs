using DrSoft.FlowControl.Builder;
using DrSoft.FlowControl.Engine;
using DrSoft.FlowControl.ProcessComponent;
using DrSoft.MarkCard.Interface;
using Microsoft.Extensions.Logging;

namespace DrSoft.FlowControl.Service
{
    public class ProcessService
    {
        private readonly ILogger _logger;
        private readonly ProcessContext context;
        private readonly IMarkController _markController;
        /// <summary>
        /// ProcessService构造函数，注入必要的服务和依赖
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="markingController"></param>
        public ProcessService(ILogger logger, IMarkController markingController)
        {
            _logger = logger;

            // 创建上下文
            context = new ProcessContext();
            _markController = markingController;
        }

        /// <summary>
        /// 打标流程示例
        /// </summary>
        public async void MarkingProcess()
        {
            var markingData = new byte[1024]; // 实际从文件加载

            // 1. 创建流程
            var process = new ProcessBuilder("校准流程", _logger)
                .AddMarking("打标二维码", markingData, speed: 800, power: 85)
                .AddDelay("稳定延时", 100)
                .Build();

            context.ClearServices();
            // 2. 往上下文注册全新的服务
            context.RegisterService<IMarkController>(_markController);

            // 3. 创建执行引擎
            var engine = new ProcessEngine(context);
            engine.RootFlow = process;
            // 4. 启动流程
            await engine.StartAsync(process);
        }

        public async void DemoProcess()
        {
            //1.创建打标数据
            var markingData = new byte[1024]; // 实际从文件加载

            // 2. 使用构建器创建流程
            var process = new ProcessBuilder("激光打标流程", _logger)
                .AddPosition("移动到起点", 100, 200)
                .AddDelay("稳定延时", 100)
                .AddMarking("打标二维码", markingData, speed: 800, power: 85)
                .AddInspection("检测二维码", "QRCode_Template", tolerance: 0.95)
                .AddConditional("质量判断",
                    async ctx =>
                    {
                        var score = ctx.GetData<double>("InspectionScore");
                        return score >= 0.95;
                    },
                    buildTrueBranch: builder => builder
                        .AddMarking("OK标记", new byte[128], speed: 500, power: 50)
                        .AddDelay("冷却", 200),
                    buildFalseBranch: builder => builder
                        .AddLoop("重试循环", loopBuilder =>
                        {
                            loopBuilder
                                .AddMarking("重试打标", markingData, speed: 600, power: 90)
                                .AddInspection("重检", "QRCode_Template", tolerance: 0.95);
                        }, iterations: 3)
                        .AddConditional("最终判断",
                            async ctx => ctx.GetData<double>("InspectionScore") >= 0.95,
                            buildTrueBranch: b => b.AddMarking("OK标记", new byte[128]),
                            buildFalseBranch: b => b.AddPosition("移动到废品区", 0, 0))
                )
                .AddPosition("移动到下料位", 500, 500)
                .Build();

            // 3. 创建执行引擎
            var engine = new ProcessEngine();
            engine.RootFlow = process;

            // 订阅事件
            engine.OnStateChanged += state => Console.WriteLine($"状态变更: {state}");
            engine.OnAlarmOccurred += alarms =>
            {
                foreach (var alarm in alarms)
                    Console.WriteLine($"报警: {alarm.Code} - {alarm.Message}");
            };
            engine.OnProgress += progress =>
                Console.WriteLine($"进度: [{progress.StepName}] {progress.Percentage:F1}% - {progress.Status}");
            engine.OnProcessCompleted += result =>
                Console.WriteLine($"流程完成: {result.Success}, 耗时: {result.Duration.TotalMilliseconds}ms");

            // 4. 启动流程
            await engine.StartAsync(process);

            // 5. 模拟用户操作
            await Task.Delay(1000);

            // 模拟暂停
            await engine.PauseAsync();
            Console.WriteLine("流程已暂停，按任意键继续...");
            Console.ReadKey();
            await engine.ResumeAsync();

            // 等待完成
            await engine.WaitForCompletionAsync();

            Console.WriteLine("流程执行完成，按任意键退出");
            Console.ReadKey();
        }
    }
}
