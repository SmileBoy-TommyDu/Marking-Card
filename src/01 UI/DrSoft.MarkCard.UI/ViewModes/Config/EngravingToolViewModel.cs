using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DrSoft.Drawing.Event;
using DrSoft.Drawing.Utility;
using DrSoft.MarkCard.Model.DTO;
using DrSoft.MarkCard.Service;
using Microsoft.Extensions.Logging;

namespace DrSoft.MarkCard.UI.ViewModes.Config;

public partial class EngravingToolViewModel : ObservableObject
{

    private readonly MarkParamService _markParamService;
    private readonly MarkService _markService;
    private readonly ILogger<EngravingToolViewModel> _logger;
    private readonly SystemParaForGalvoService _forGalvoService;

    public EngravingToolViewModel(MarkParamService markParamService, MarkService markService,ILogger<EngravingToolViewModel> logger, SystemParaForGalvoService forGalvoService)
    {
        _logger = logger;
        _markParamService = markParamService;
        _markService = markService;
        _forGalvoService = forGalvoService;
        _markService.OnMarkingEnd += (cardNo, state) =>
        {
            // 在打标结束时更新完成量和时间
            UpdateCompletedCountAndTime();

            EventBus.Instance.Publish(new ToastMessageEvent("打标完成！", ToastType.Info));

        };
     }

    private void UpdateCompletedCountAndTime()
    {
        var galvo = _forGalvoService.GetGalvoParas();
        if (galvo == null)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("未选择打标卡", ToastType.Warning));
            return;
        }

        var errCode = _markService.GetRealExecTime(galvo.MarkCardNo, out int execTime);
        if (errCode == Model.MarkErrorCode.None)
        {
            TotalTime = execTime/1000d;
        }
        else
        {
            _logger.LogError($"获取实际执行时间失败: {errCode.GetDescription()}");
        }
    }

    /// <summary>
    /// 雕刻数量 - 完成量
    /// </summary>
    [ObservableProperty]
    private int _completedCount = 0;

    /// <summary>
    /// 雕刻数量 - 总完成量
    /// </summary>
    [ObservableProperty]
    private int _totalCompletedCount = 0;

    /// <summary>
    /// 雕刻时间 - 本次
    /// </summary>
    [ObservableProperty]
    private double _currentTime = 0;

    /// <summary>
    /// 雕刻时间 - 全部
    /// </summary>
    [ObservableProperty]
    private double _totalTime = 0;

    [ObservableProperty]
    private double _estimatedTime = 0;

    /// <summary>
    /// 自动设定 Shutter
    /// </summary>
    [ObservableProperty]
    private bool _autoSetShutter = false;

    /// <summary>
    /// 自动设定 Lamp
    /// </summary>
    [ObservableProperty]
    private bool _autoSetLamp = false;

    /// <summary>
    /// 自动设定 Align
    /// </summary>
    [ObservableProperty]
    private bool _autoSetAlign = false;

    /// <summary>
    /// 自动雕刻 - 启动
    /// </summary>
    [ObservableProperty]
    private bool _autoEngraveEnabled = false;

    /// <summary>
    /// 显示雕刻时间与次数
    /// </summary>
    [ObservableProperty]
    private bool _showEngraveTimeAndCount = false;

    /// <summary>
    /// 延迟时间（秒）
    /// </summary>
    [ObservableProperty]
    private float _delaySeconds = 0;

    /// <summary>
    /// 雕刻次数
    /// </summary>
    [ObservableProperty]
    private int _engraveCount = 1;

    /// <summary>
    /// 自动原点复归 - 启动
    /// </summary>
    [ObservableProperty]
    private bool _autoOriginEnabled = false;

    /// <summary>
    /// 自动原点复归 - 旋转轴
    /// </summary>
    [ObservableProperty]
    private bool _rotationAxisEnabled = false;

    /// <summary>
    /// C值
    /// </summary>
    [ObservableProperty]
    private double _cValue = 0;

    /// <summary>
    /// 雕刻模式 - 是否全部
    /// </summary>
    [ObservableProperty]
    private bool _engraveModeAll = true;

    /// <summary>
    /// 雕刻模式 - 是否已选取
    /// </summary>
    [ObservableProperty]
    private bool _engraveModeSelected = false;

    /// <summary>
    /// 手动 Shutter 命令
    /// </summary>
    [RelayCommand]
    private void ManualShutter()
    {
        // TODO: 实现手动Shutter逻辑
    }

    /// <summary>
    /// 手动 Align 命令
    /// </summary>
    [RelayCommand]
    private void ManualAlign()
    {
        // TODO: 实现手动Align逻辑
    }

    /// <summary>
    /// 手动 Lamp 命令
    /// </summary>
    [RelayCommand]
    private void ManualLamp()
    {
        // TODO: 实现手动Lamp逻辑
    }


    /// <summary>
    /// 预览命令
    /// </summary>
    [RelayCommand]
    private async Task LoadMarkData()
    {
        if(RuntimeContext.ActiveCanvasId == null)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("没有激活的画布，无法预览", ToastType.Error));
            return;
        }

        var galvo = _forGalvoService.GetGalvoParas();
        if (galvo == null)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("未选择打标卡", ToastType.Warning));
            return;
        }

        MarkingJobDto markData = await _markParamService.BuildMarkingJobAsync(
            RuntimeContext.ActiveCanvasId,
            EngraveModeSelected ? RuntimeContext.Selections : null);
        if (markData != null)
        {
            markData.ProcessTimes = EngraveCount;
            //var err = _markService.SetOffsetScale(galvo.MarkCardNo,galvo.LensNo, galvo.OffsetX, galvo.OffsetY, galvo.Rotation, galvo.ScaleX, galvo.ScaleY);
            //if(err != Model.MarkErrorCode.None)
            //{
            //    EventBus.Instance.Publish(new ToastMessageEvent($"设置偏移缩放失败: {err.GetDescription()}", ToastType.Error));
            //    return;
            //}
            var errCode = _markService.LoadMarkData(galvo.MarkCardNo, markData);
            if (errCode == Model.MarkErrorCode.None)
            {
                EventBus.Instance.Publish(new ToastMessageEvent("下发打标数据成功", ToastType.Info));
                EstimatedTime = _markService.GetEstimatedExecTime(galvo.MarkCardNo) / 1000d;
            }
            else
            {
                 EventBus.Instance.Publish(new ToastMessageEvent($"下发打标数据失败: {errCode.GetDescription()}", ToastType.Error));
            }
        }

    }

    /// <summary>
    /// 执行命令
    /// </summary>
    [RelayCommand]
    private async Task Execute()
    {
        var galvo = _forGalvoService.GetGalvoParas();
        if (galvo == null)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("未选择打标卡", ToastType.Warning));
            return;
        }

        if(DelaySeconds>0) await Task.Delay((int)(DelaySeconds*1000));

        var errorCode = _markService.StartMarking(galvo.MarkCardNo);
        if (errorCode == Model.MarkErrorCode.None)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("下发打标成功", ToastType.Info));
        }
        else
        {
            EventBus.Instance.Publish(new ToastMessageEvent($"下发打标失败: {errorCode.GetDescription()}", ToastType.Error));
        }
    }

    /// <summary>
    /// 暂停命令
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private void Stop()
    {
        var galvo = _forGalvoService.GetGalvoParas();
        if (galvo == null)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("未选择打标卡", ToastType.Warning));
            return;
        }
        var errorCode = _markService.StopMarking(galvo.MarkCardNo);
        if (errorCode == Model.MarkErrorCode.None)
        {
            EventBus.Instance.Publish(new ToastMessageEvent("停止打标成功", ToastType.Info));
        }
        else
        {
            EventBus.Instance.Publish(new ToastMessageEvent($"停止打标失败: {errorCode.GetDescription()}", ToastType.Error));
        }
    }

    /// <summary>
    /// 套用命令
    /// </summary>
    //[RelayCommand]
    //private void Apply()
    //{
    //    // TODO: 实现套用配置逻辑
    //}
}
