using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model
{
    public enum MarkErrorCode
    {
        None,

        [Description("打标卡初始化中")]
        MarkCardInitializing,

        [Description("打标卡未连接")]
        MarkCardNotConnected,

        [Description("打标卡初始化失败")]
        MarkCardInitializationFailed,

        [Description("打标卡忙")]
        MarkCardBusy,

        [Description("无效参数")]
        InvalidParameter,

        //打标卡未初始化
        [Description("打标卡未初始化")]
        Uninitialized,

        //打标卡号不匹配
        [Description("打标卡号不匹配")]
        UnmatchedMarkCardNo,

        [Description("超时错误")]
        TimeoutError,

        /// <summary>
        /// 未发现扫描头配置
        /// </summary>
        [Description("未发现扫描头配置")]
        UnFoundScanHeadConfigError,

        [Description("文件错误")]
        FileError,

        [Description("内存错误")]
        MemoryError,

        [Description("文件打开错误")]
        FileOpenError,

        [Description("DSP内存错误")]
        DspMemoryError,

        [Description("PCI下载错误")]
        DownloadError,

 

        [Description("板卡驱动未找到或访问被拒绝")]
        DriverOrAccessDenied,


        [Description("警告：选择了 3D 校正表或 Dim=3，但未启用 3D 选项，系统将按 2D 运行")]
        Option3DNotEnabledWarning,


        [Description("PCI上传错误（仅下载校验时）")]
        UploadError,

        [Description("校验错误")]
        VerifyError,

        [Description("校正库未授权")]
        CalibrationLibraryUnauthorized,

        [Description("加载原始校正档失败")]
        LoadOriginalCalibrationFileFailed,

        [Description("生成校正档失败")]
        GenerateCalibrationFileFailed,

        [Description("等待IO触发超时")]
        WaitIOTriggerTimeout,

        [Description("未连接")]
        NotConnected,

        [Description("导入配置错误")]
        ImportConfigError,

        [Description("未发现打标卡")]
        UnFoundMarkCard,

        [Description("命令未转发")]
        CommandNotForwarded,

        [Description("列表处理未激活")]
        ListProcessingNotActive,

        [Description("非法输入指针")]
        IllegalInputPointer,

        [Description("列表命令转换为List_nop")]
        ListCommandConvertedToNop,

        [Description("DSP版本过旧")]
        DSPVersionOld,

        [Description("版本错误")]
        VersionError,

        [Description("Flash错误")]
        FlashError,

        [Description("不支持的Windows版本")]
        UnsupportedWindowsVersion,

        [Description("未知错误")]
        UnknownError,

        [Description("未导入校正档")]
        UnLoadCalibration,

        [Description("导入校正档失败")]
        LoadCalibrationFailed,

        // 以下为外部接口/底层库错误码（显式指定数值）
        [Description("传入参数错误")]
        ParamError = 1001,

        [Description("重复创建")]
        RepeatCreate = 1002,

        [Description("执行失败")]
        ExecuteFailed = 1003,

        [Description("获取资源失败")]
        GetResourcesFailed = 1004,

        [Description("执行缓存指令失败")]
        ExecuteCacheCommandFailed = 1005,

        [Description("文件名找不到")]
        FileNameNotFound = 1006,

        [Description("文件打开失败")]
        FileOpenFailed = 1007,

        [Description("无效的工艺参数")]
        InvalidProcessParameter = 1008,

        [Description("查找配置节点失败")]
        FindConfigNodeFailed = 1009,

        [Description("连接失败")]
        ConnectFailed = 1051,

        [Description("断开连接异常")]
        DisconnectError = 1052,

        [Description("系统没有初始化")]
        SystemNotInitialized = 1101,

        [Description("振镜校正原始数据与理论值偏差过大")]
        CorrectionDataDeviationTooLarge = 1201,

        [Description("振镜校正阶数小于 3")]
        CorrectionOrderTooSmall = 1202,

        [Description("振镜校正幅面小于 10")]
        CorrectionSidelengthTooSmall = 1203,

        [Description("振镜校正原始数据的点位数不等于阶数的平方")]
        CorrectionPointCountMismatch = 1204,

        [Description("校正点位计算失败")]
        CorrectionPointCalculationFailed = 1205,

        [Description("比例因子小于等于 0")]
        FactorNonPositive = 1206,

        [Description("桶形数据小于等于 0")]
        BarrelDataNonPositive = 1207,

        [Description("数据采集错误")]
        DataAcquisitionError = 1301,

        [Description("循环采样中")]
        SamplingLooping = 1302,

        [Description("存满，暂停中")]
        BufferFullPaused = 1303,

        [Description("等待触发状态")]
        WaitingTrigger = 1304,

        [Description("正在采样")]
        SamplingInProgress = 1305,

        [Description("参数设置失败")]
        TriggerParamSetFailed = 1306,

        [Description("未传入图形数据")]
        NoGraphicData = 2001,

        [Description("图元数据异常")]
        //图元数据异常
        GraphicPrimitiveDataError=2002,

        //未找到图元工艺参数
        [Description("未找到图元工艺参数")]
        UnFoundGraphicPrimitiveProcessParam=2003,


        //不支持该功能
        [Description("不支持该功能")]
        UnsupportedFunction=2004



    }
}
