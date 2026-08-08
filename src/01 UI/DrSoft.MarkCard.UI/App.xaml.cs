using DrSoft.Drawing.Controls.Consts;
using DrSoft.Drawing.Controls.ViewModels;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Registration;
using DrSoft.Drawing.Utility;
using DrSoft.FlowControl.Service;
using DrSoft.MarkCard.BoChu;
using DrSoft.MarkCard.EasternLogic;
using DrSoft.MarkCard.Impl;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.Model.Enum;
using DrSoft.MarkCard.RTC;
using DrSoft.MarkCard.Service;
using DrSoft.MarkCard.UI.Models;
using DrSoft.MarkCard.UI.UIConfig;
using DrSoft.MarkCard.UI.ViewModes;
using DrSoft.MarkCard.UI.ViewModes.Config;
using DrSoft.MarkCard.UI.ViewModes.EditMenu;
using DrSoft.MarkCard.UI.ViewModes.Parameter;
using DrSoft.MarkCard.UI.ViewModes.Tools;
using EnumsNET;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace DrSoft.MarkCard.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static App Instance { get; private set; }

        public IServiceProvider? Services { get; private set; }

        public static T GetService<T>() where T : class
        {
            return Instance.Services.GetService<T>();
        }

        public static T GetRequiredService<T>() where T : class
        {
            if (Instance?.Services == null)
                throw new InvalidOperationException("App services have not been initialized.");
            return Instance.Services.GetRequiredService<T>();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 生成 markcards.drlic 到 %LOCALAPPDATA%\DrSoft\
            GenerateMarkCardsDrLic();

            // 注册全局未捕获异常处理，尽早注册以防在初始化阶段抛出未处理异常
            RegisterGlobalExceptionHandlers();

            try
            {
                var services = new ServiceCollection();

                services.RegisterDrawingTools();

                // 1. 配置 DI 容器
                ConfigureServices(services);

                //Ml.SetIsSWJ(false);
                //FeatureDesignerNet.Ps.BuildAutofacServiceProvider<App>(_ => { });

                Services = services.BuildServiceProvider();

                // force-create UI-only singletons that need to subscribe to EventBus (they won't be created otherwise)
                // e.g. ColorPickerHandler subscribes to ColorPickerRequestEvent in its ctor
                Services.GetRequiredService<ColorPickerHandler>();
                Services.GetRequiredService<CopyLayerHandler>();

                // set global instance so other code can access the service provider
                Instance = this;

                // 2. 显示主窗体
                var mainWindow = Services.GetRequiredService<MainWindow>();
                mainWindow.Show();

                // 3. 启动初始化工作
                InitializeHardware();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用程序启动失败");
                MessageBox.Show("系统初始化失败，请查看日志。");
                Shutdown();
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // --- 1. 基础组件 (日志、配置) ---
            services.AddLogging(builder => builder.AddSerilog());
            Config config = null;
            try
            {
                config = LoadConfiguration();
                ConfigureGlobalLogger(config);
                services.AddSingleton(config);

                // 同步系统配置中的画布显示选项到 DocumentContext
                if (config?.SystemConfig != null)
                {
                    var ctx = DrSoft.Drawing.Controls.Models.DocumentContext.Instance;
                    ctx.ShowDirectionArrow = config.SystemConfig.EnableDirectionArrow;
                    ctx.ShowJumpLine = config.SystemConfig.EnableJumpLine;
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "加载配置失败");
                MessageBox.Show("加载配置失败，请确保配置文件存在且格式正确。");
                Environment.Exit(0);
            }

            CanvasSystemConfig UIconfig = null;
            try
            {
                UIconfig = LoadUIConfiguration();
                services.AddSingleton(UIconfig);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "加载UI配置失败");
                MessageBox.Show("加载UI配置失败，请确保配置文件存在且格式正确。");
                Environment.Exit(0);
            }

            // --- 2. 绘图引擎 ---

            // --- 3. 硬件适配器
            // 使用工厂模式注册不同的打标卡
            var cardConfig = config.CardConfigs.Find(x => x.IsActive == true);
            if (cardConfig == null)
            {
                throw new Exception($"没有配置打标卡");
            }
            if (cardConfig.MarkCardType == MarkCardType.RTC6)
            {
                services.AddSingleton<IMarkCardAdapter, RTC6Adapter>();
            }
            else if (cardConfig.MarkCardType == MarkCardType.PMC6)
            {
                services.AddSingleton<IMarkCardAdapter, PMC6Adapter>();
            }
            else if (cardConfig.MarkCardType == MarkCardType.BoChu)
            {
                services.AddSingleton<IMarkCardAdapter, BCGAdapter>();
            }
            else
            {
                throw new Exception($"不支持的打标卡类型: {cardConfig.MarkCardType}");
            }

            // --- 4. 领域层 ---
            services.AddSingleton<IMarkingParam, MarkingParam>();

            // --- 5. 业务逻辑与应用层 ---
            services.AddSingleton<MarkService>();
            services.AddSingleton<IMarkController, MarkController>();

            services.AddSingleton<MarkParamService>();

            services.AddSingleton<CalibrationService>();

            services.AddSingleton<SystemParaForGalvoService>();

            //--流程处理--
            services.AddSingleton<ProcessService>();

            // --- 6. UI 层 ---
            services.AddSingleton<ScanHeadConfigViewModel>();

            // Register FileViewModel so it can be injected into MainWindow/MainViewModel
            #region 编辑界面弹框界面            
            services.AddSingleton<AlignPopupViewModel>();
            services.AddSingleton<ConvertToPointCirclePopupViewModel>();
            services.AddSingleton<DistributionPopupViewModel>();
            services.AddSingleton<ExtendHeadTailPopupViewModel>();
            services.AddSingleton<InversePopupViewModel>();
            services.AddSingleton<JumpPointPopupViewModel>();
            services.AddSingleton<MoveNodePopupViewModel>();
            services.AddSingleton<PartitionPopupViewModel>();
            services.AddSingleton<SeparateNodePopupViewModel>();
            services.AddSingleton<SkyWritingPopupViewModel>();
            #endregion

            services.AddSingleton<FileViewModel>();
            services.AddSingleton<EditMenuViewModel>();

            services.AddSingleton<ShareTextModel>();

            // 属性
            ServiceCollectionExtensions.AddBaseParamViewModels(services, Assembly.GetExecutingAssembly());
            services.AddSingleton<ParametersTabViewModel>();
            services.AddSingleton<ShapeParamViewModel>();
            services.AddSingleton<EngravingToolViewModel>();
            services.AddSingleton<LaserTestViewModel>();
            services.AddSingleton<GroupParamViewModel>();
            services.AddSingleton<LayerInputIOViewModel>();
            services.AddSingleton<LayerOutputIOViewModel>();
            // UI handler for color picker requests from controls in drawing.controls
            services.AddSingleton<ColorPickerHandler>();
            // UI handler for copy layer requests (input dialog + parameter copying)
            services.AddSingleton<CopyLayerHandler>();

            // 位置工具栏
            services.AddSingleton<PositionViewModel>();
            services.AddSingleton<SizeToolbarViewModel>();

            services.AddSingleton<TextToolViewModel>();
            services.AddSingleton<EditPathNodesToolViewModel>();
            services.AddSingleton<VectorToolViewModel>();
            services.AddSingleton<ToolbarViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<MainViewModel>();
        }

        private static void ConfigureGlobalLogger(Config config)
        {
            string logPath = "logs/{0}/log-.txt";
            if (config != null && !string.IsNullOrEmpty(config.SystemConfig.LogFilePath))
            {
                logPath = config.SystemConfig.LogFilePath + "/{0}/log-.txt";
            }

            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Map(
                keySelector: logEvent => logEvent.Level,
                configure: (level, wt) => wt.File(
                    path: string.Format(logPath, level),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    retainedFileCountLimit: 30)
            )
            .WriteTo.Console()
            .CreateLogger();
        }

        private static Config LoadConfiguration()
        {

            string jsonPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config.json");
            if (!File.Exists(jsonPath))
            {
                var assembly = typeof(IMarkCardAdapter).Assembly;
                var resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("config_template.json", StringComparison.OrdinalIgnoreCase));

                if (resourceName != null)
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    using var reader = new StreamReader(stream);
                    File.WriteAllText(jsonPath, reader.ReadToEnd());
                }
                else
                {
                    throw new Exception("未找到默认配置文件，可用资源: " + string.Join(", ", assembly.GetManifestResourceNames()));

                    //return null;
                }
            }

            var config = JsonSerializer.Deserialize<Config>(File.ReadAllText(jsonPath));
            if (config != null)
            {
                GlobalVariableManagement.SetResolution(config.SystemConfig.Resolution);
            }
            // todo 读取配置文件并解析成 Config 对象
            return config;
        }

        /// <summary>
        /// 将 MarkCardType 枚举转换为 markcards.drlic 格式并存储到 %LOCALAPPDATA%\DrSoft\
        /// </summary>
        private static void GenerateMarkCardsDrLic()
        {
            try
            {
                string localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string drSoftPath = Path.Combine(localAppDataPath, "DrSoft");

                if (!Directory.Exists(drSoftPath))
                {
                    Directory.CreateDirectory(drSoftPath);
                }

                string filePath = Path.Combine(drSoftPath, "markcards.drlic");

                var options = new List<object>();
                foreach (MarkCardType type in Enum.GetValues(typeof(MarkCardType)))
                {
                    var fieldInfo = type.GetType().GetField(type.ToString());
                    var attr = fieldInfo?.GetCustomAttribute<EnumValueAttriute>();
                    if (string.IsNullOrEmpty(attr?.Description))
                    {
                        throw new Exception("打标卡枚举未配置中文");
                    }

                    options.Add(new
                    {
                        value = attr.Value,
                        label = attr.Description
                    });
                }

                var drLicData = new
                {
                    key = AppConsts.AppName,
                    name = "打标卡",
                    order = 1,
                    required = false,
                    multiple = true,
                    options
                };
                string json = Newtonsoft.Json.JsonConvert.SerializeObject(drLicData);
                File.WriteAllText(filePath, json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "生成 markcards.drlic 失败");
            }
        }

        private static CanvasSystemConfig LoadUIConfiguration()
        {

            string jsonPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "UIConfig.json");
            if (File.Exists(jsonPath))
            {

                string jsonString = File.ReadAllText(jsonPath);
                var config = JsonSerializer.Deserialize<CanvasSystemConfig>(File.ReadAllText(jsonPath));
                // todo 读取配置文件并解析成 Config 对象
                return config;
            }
            else
            {
                return new CanvasSystemConfig();
                //throw new Exception("未找到默认配置文件");
            }
        }

        private void InitializeHardware()
        {
            Task.Delay(3000).ContinueWith(_ =>
            {
                // 验证激活的打标卡
                var config = Services.GetService<Config>();
                var cardConfig = config.CardConfigs.FirstOrDefault(x => x.IsActive);
                if (cardConfig == null)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"没有激活的打标卡", ToastType.Error));
                    return;
                }
                // 获取授权 cardRights 是位运算后的结果
                //FeatureCommon.Tprc.Rkf.TryGetLicenseExtension(AppConsts.AppName, out int cardRights);
                //var attr = cardConfig.MarkCardType.GetType().GetField(cardConfig.MarkCardType.ToString()).GetAttribute<EnumValueAttriute>();
                //bool hasCardPermission = (cardRights & attr.Value) != 0;
                //if (!hasCardPermission)
                //{
                //    EventBus.Instance.Publish(new ToastMessageEvent($"打标卡没有授权", ToastType.Error));
                //    return;
                //}

                // 获取硬件实例并初始化
                var markServce = Services?.GetService<MarkService>();
                var errorCode = markServce?.Initialize();
                if (errorCode == MarkErrorCode.None)
                {
                    EventBus.Instance.Publish(new ToastMessageEvent("打标初始化成功", ToastType.Info));

                    markServce.OnMarkingEnd += (uint cardNo, MarkingState state) =>
                    {
                        if (state == MarkingState.MarkEnd)
                        {
                            EventBus.Instance.Publish(new ToastMessageEvent($"卡{cardNo}打标完成", ToastType.Info));
                        }
                    };

                }
                else
                {
                    EventBus.Instance.Publish(new ToastMessageEvent($"打标初始化失败: {errorCode.GetDescription()}", ToastType.Error));
                }
            });
        }

        private void MarkServce_OnMarkingEnd(uint arg1, MarkingState arg2)
        {
            throw new NotImplementedException();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 确保日志刷新并关闭
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        // 注册全局异常处理
        private void RegisterGlobalExceptionHandlers()
        {
            // UI 线程未捕获异常
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // 非 UI 线程未捕获异常
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Task 未观察到的异常
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                Log.Fatal(e.Exception, "未处理的 UI 线程异常");
                MessageBox.Show($"发生未处理的错误: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true; // 尝试阻止应用崩溃
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "处理 DispatcherUnhandledException 时出错");
            }
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = e.ExceptionObject as Exception;
                Log.Fatal(ex, "未处理的域异常");

                // 在 UI 线程上显示消息框（如果可用）
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MessageBox.Show($"发生未处理的异常: {ex?.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch { }
                }));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "处理 CurrentDomain_UnhandledException 时出错");
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            try
            {
                Log.Fatal(e.Exception, "未观察到的任务异常");

                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        MessageBox.Show($"任务中发生未处理的异常: {e.Exception.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch { }
                }));

                e.SetObserved();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "处理 UnobservedTaskException 时出错");
            }
        }
    }

    static class ServiceCollectionExtensions
    {
        public static void AddBaseParamViewModels(this IServiceCollection services, Assembly assembly)
        {
            // 定义你要查找的开放泛型基类
            var openGenericType = typeof(BaseParamViewModel<>);

            var implementations = assembly.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => new
                {
                    ServiceType = GetBaseGenericType(t, openGenericType),
                    ImplementationType = t
                })
                .Where(x => x.ServiceType != null);

            foreach (var item in implementations)
            {
                // 方式 A：以它对应的封闭泛型基类注册 (推荐)
                // 这样你可以通过 GetService<BaseParamViewModel<EngravingParameter>>() 拿到它
                services.AddSingleton(item.ServiceType, item.ImplementationType);

                // 方式 B：同时以它自己的类型注册
                // 这样你可以直接通过 GetService<A>() 拿到它
                services.AddSingleton(item.ImplementationType);

                Console.WriteLine($"[IOC] 注册: {item.ImplementationType.Name} -> {item.ServiceType.Name}");
            }
        }

        // 辅助工具：向上查找指定的开放泛型基类
        private static Type GetBaseGenericType(Type type, Type openGeneric)
        {
            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == openGeneric)
                {
                    return type;
                }
                type = type.BaseType;
            }
            return null;
        }
    }
}
