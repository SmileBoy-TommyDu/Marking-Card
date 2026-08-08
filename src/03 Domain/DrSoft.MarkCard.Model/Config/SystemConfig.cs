using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.Config
{
    /// <summary>
    /// 配置系统相关的参数
    /// </summary>
    public class SystemConfig
    {
        //是否启用日志
        public bool EnableLogging { get; set; } = true;

        //日志文件路径
        public string LogFilePath { get; set; } = "logs";

        public string DrMarkPath { get; set; } = "C:\\Program Files (x86)\\DRMark";

        //是否启用自动下载到缓冲区
        public bool EnableDownloadToBuffer { get; set; } = true;

        //自动下载到缓冲区时间间隔（s）
        public int DownloadToBufferInterval { get; set; } = 5;

        //格点X方向间距（mm）
        public double GridSpacingX { get; set; } = 10;
        public double GridSpacingY { get; set; } = 10;

        /// <summary>
        /// 曲线采样精度
        /// </summary>
        public double Resolution { get; set; } = 0.02;

        //微调X方向步长（mm）
        public double MicroAdjustStepX { get; set; } = 0.1;

        //微调Y方向步长（mm）
        public double MicroAdjustStepY { get;set; } = 0.1;

        //是否启用加工方向箭头显示
        public bool EnableDirectionArrow { get; set; } = false;

        //是否启用加工路径（跳扫虚线）显示
        public bool EnableJumpLine { get; set; } = false;
    }
}
