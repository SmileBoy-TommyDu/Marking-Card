using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    public enum MarkCommandType
    {
        /// <summary>
        /// 点
        /// </summary>
        MarkPoint = 0,

        /// <summary>
        /// 线
        /// </summary>
        MarkLine = 1,

        /// <summary>
        /// 圆
        /// </summary>
        MarkCircle = 2,

        MarkEllipse = 3,



        //下发功率
        ModifyPower = 100,

      

        //下发速度（打标速度、跳转速度）
        ModifySpeed = 101,

        //下发激光延时（开光延时、关光延时）
        ModifyLaserDelay = 102,

        /// <summary>
        /// 下发扫描延时（打标延时、跳转延时、转角延时）
        /// </summary>
        ModifyScannerDelay = 103,

        ModifyFrequencyAndPulsesWidth = 104,

        //跳转命令
        JumpCommand = 201,

        SkyWritingCommand = 202,


        //虚线打标命令
        MarkDashedLineCommand = 203,

        /// <summary>
        /// 位图打标命令
        /// </summary>
        MarkBitmapCommand = 204,

    }
}
