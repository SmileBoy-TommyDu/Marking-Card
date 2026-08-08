using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DrSoft.MarkCard.Model.Enum;

namespace DrSoft.MarkCard.Model.Config
{
    public class ScanHeadConfig
    {
        /// <summary>
        /// 打标卡号
        /// </summary>
        public uint CardNo { get; set; } = 1;

        /// <summary>
        /// 扫描头序号
        /// </summary>
        public uint ScanHeadNo { get; set; } = 1;

        /// <summary>
        /// 通信协议
        /// </summary>
        public ScanHeadProtocol Protocol { get; set; } = ScanHeadProtocol.SL2_100;

        /// <summary>
        /// 加工幅面X方向(mm)     
        /// </summary>
        public double ProcessingAreaX { get; set; }

        /// <summary>
        /// 加工幅面Y方向(mm)     
        /// </summary>
        public double ProcessingAreaY { get; set; }

        /// <summary>
        /// 旋转角度
        /// </summary>
        public double RotationAngle { get; set; }

        /// <summary>
        /// 最大速度
        /// </summary>
        public double MaxSpeed { get; set; }

        /// <summary>
        /// 最大温度
        /// </summary>
        public double MaxTemperature { get; set; }

        /// <summary>
        /// 场景焦距
        /// </summary>
        public double FocalLength { get; set; }

        /// <summary>
        /// 启用PSO
        /// </summary>
        public bool EnablePSO { get; set; }

        /// <summary>
        /// PSO参数-间距
        /// </summary>
        public double PSOSpacing { get; set; }

        /// <summary>
        /// PSO参数-脉宽
        /// </summary>
        public double PSOPulseWidth { get; set; }

        //原点X方向
        public double OriginX { get; set; }

        //原点Y方向
        public double OriginY { get; set; }

        

        /// <summary>
        /// 水平镜像
        /// </summary>
        public bool MirrorX { get; set; }

        /// <summary>
        /// 垂直镜像
        /// </summary>
        public bool MirrorY { get; set; }

        /// <summary>
        /// XY轴反转
        /// </summary>
        public bool ReverseXY { get; set; }

        /// <summary>
        /// 角度偏移
        /// </summary>
        public float AngleOffset { get; set; }

        /// <summary>
        /// 整体偏移X方向(mm)
        /// </summary>
        public int OffsetX { get; set; }

        /// <summary>
        /// 整体偏移Y方向(mm)
        /// </summary>
        public int OffsetY { get; set; }

        public string HeadFilePath { get; set; } = string.Empty;
    }
}
