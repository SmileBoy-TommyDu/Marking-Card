using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.Event;
using DrSoft.MarkCard.Interface;
using DrSoft.MarkCard.Model.Config;
using DrSoft.MarkCard.UI.UserControls;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using ConfigModel = DrSoft.MarkCard.Model.Config.Config;

namespace DrSoft.MarkCard.UI.ViewModes.Config
{
    public partial class ConfigDialogViewModel : ObservableObject
    {
        private readonly ConfigModel _config;
        private readonly bool _originalEnableDirectionArrow;
        private readonly bool _originalEnableJumpLine;
        private readonly string _originalConfigSnapshot;
        IEventBus? eventBus => EventBus.Instance;

        public SystemConfigViewModel SystemVm { get; }
        public MarkCardConfigViewModel MarkCardVm { get; }
        public ScanHeadConfigViewModel ScanHeadVm { get; }
        public LaserConfigViewModel LaserVm { get; }
        public IOConfigViewModel IOVm { get; }
        public PowerMeterConfigViewModel PowerMeterVm { get; }
        public ImportExportConfigViewModel ImportExportVm { get; }

        [ObservableProperty]
        private object? _selectedContent;

        [ObservableProperty]
        private string _selectedItemName = string.Empty;

        public ConfigDialogViewModel(ConfigModel config)
        {
            _config = config;

            SystemVm = new SystemConfigViewModel(config.SystemConfig);
            MarkCardVm = new MarkCardConfigViewModel(config.CardConfigs);
            ScanHeadVm = new ScanHeadConfigViewModel(config.ScanHeadConfigs, config.CardConfigs);
            LaserVm = new LaserConfigViewModel(config.LaserConfigs, config.CardConfigs);
            IOVm = new IOConfigViewModel(config.IOConfigs, config.CardConfigs);
            PowerMeterVm = new PowerMeterConfigViewModel(config.PowerMeterConfig);
            ImportExportVm = new ImportExportConfigViewModel(config);

            _originalEnableDirectionArrow = config.SystemConfig.EnableDirectionArrow;
            _originalEnableJumpLine = config.SystemConfig.EnableJumpLine;
            _originalConfigSnapshot = SerializeConfigForComparison(config);

            // 默认选中系统-日志设置
            SelectedItemName = "日志设置";
            SelectedContent = SystemVm.LogSettingsVm;
        }

        [RelayCommand]
        private void SelectNode(string nodeName)
        {
            var actualNodeName = ResolveNodeName(nodeName);
            SelectedItemName = actualNodeName;

            if (actualNodeName == "扫描头")
            {
                ScanHeadVm.RefreshMarkCardBindingData();
            }
            else if (actualNodeName == "激光器")
            {
                LaserVm.RefreshMarkCardBindingData();
            }
            else if (actualNodeName == "输入")
            {
                IOVm.InputVm.RefreshMarkCardBindingData();
            }
            else if (actualNodeName == "输出")
            {
                IOVm.OutputVm.RefreshMarkCardBindingData();
            }

            SelectedContent = actualNodeName switch
            {
                "系统设置" => SystemVm.LogSettingsVm,
                "格点与微调" => SystemVm.GridMicroAdjustVm,
                "自动化流程" => SystemVm.AutomationProcessVm,
                "打标卡" => MarkCardVm,
                "扫描头" => ScanHeadVm,
                "激光器" => LaserVm,
                "输入" => IOVm.InputVm,
                "输出" => IOVm.OutputVm,
                "功率计" => PowerMeterVm,
                "导入/导出" => ImportExportVm,
                _ => null
            };
        }

        private static string ResolveNodeName(string nodeName)
        {
            return nodeName switch
            {
                "系统" => "日志设置",
                "输入输出" => "输入",
                _ => nodeName
            };
        }

        [RelayCommand]
        private void Apply()
        {
            //序列化_config，保存到软件根目录下的 config.json 文件中
            try
            {
                if (SelectedContent != null && _config != null)
                {
                    GlobalVariableManagement.SetResolution(_config.SystemConfig.Resolution);
                    _config?.SaveToFile();
                    eventBus?.Publish<MarkCardConfigEvent<ConfigModel>>(new MarkCardConfigEvent<ConfigModel> { Data = _config });

                    // 同步画布显示选项到 DocumentContext（即时生效，无需重启）
                    if (_config?.SystemConfig != null)
                    {
                        var ctx = DrSoft.Drawing.Controls.Models.DocumentContext.Instance;

                        if (ctx.ShowDirectionArrow != _config.SystemConfig.EnableDirectionArrow)
                        {
                            ctx.ShowDirectionArrow = _config.SystemConfig.EnableDirectionArrow;
                            ctx.IsPartialRender = false;
                            ctx?.RequestRedraw();
                        }

                        if (ctx.ShowJumpLine != _config.SystemConfig.EnableJumpLine)
                        {
                            ctx.ShowJumpLine = _config.SystemConfig.EnableJumpLine;
                            ctx.IsPartialRender = false;
                            ctx?.RequestRedraw();
                        }

                        bool displayChanged =
                            _originalEnableDirectionArrow != _config.SystemConfig.EnableDirectionArrow ||
                            _originalEnableJumpLine != _config.SystemConfig.EnableJumpLine;

                        bool otherChanged = _originalConfigSnapshot != SerializeConfigForComparison(_config);

                        if (displayChanged && !otherChanged)
                        {
                            eventBus?.Publish(new ToastMessageEvent("保存配置成功", ToastType.Info));
                        }
                        else
                        {
                            eventBus?.Publish(new ToastMessageEvent("保存配置成功，重启生效", ToastType.Info));
                        }
                    }
                }
            }catch(Exception ex)
            {
                eventBus?.Publish(new ToastMessageEvent("修改参数失败，"+ex.Message, ToastType.Error));
            }
  
        }

        [RelayCommand]
        private void Cancel()
        {
           
            // 关闭窗口
            foreach (var window in Application.Current.Windows)
            {
                if (window is Window w && w.DataContext == this)
                {
                    w.Close();
                    break;
                }
            }
        }

        /// <summary>
        /// 序列化配置用于对比（排除即时生效的显示选项）
        /// </summary>
        private static string SerializeConfigForComparison(ConfigModel config)
        {
            // 临时保存并清空显示选项，确保对比不受显示选项干扰
            var origArrow = config.SystemConfig.EnableDirectionArrow;
            var origJumpLine = config.SystemConfig.EnableJumpLine;
            config.SystemConfig.EnableDirectionArrow = false;
            config.SystemConfig.EnableJumpLine = false;
            var json = JsonSerializer.Serialize(config);
            config.SystemConfig.EnableDirectionArrow = origArrow;
            config.SystemConfig.EnableJumpLine = origJumpLine;
            return json;
        }
    }
}
