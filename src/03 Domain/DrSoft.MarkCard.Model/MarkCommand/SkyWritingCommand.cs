using DrSoft.MarkCard.Model.EditMenu;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.Model.MarkCommand
{
    /// <summary>
    /// SkyWriting 命令
    /// </summary>
    public class SkyWritingCommand : IMarkCommand
    {
        public MarkCommandType MarkCommandType => MarkCommandType.SkyWritingCommand;


        public uint SkyWritingModel { get; set; }

        /// <summary>
        /// 激活Sky Writing功能
        /// </summary>
        //public bool Enabled { get; set; }    

        /// <summary>
        /// Sky Writing parameter.单位微秒
        /// </summary>
        public float Timelag { get; set; }

        /// <summary>
        /// 开光延时（正值：表示延迟）单位微秒
        /// </summary>
        public float LaserOnShift { get; set; }

        /// <summary>
        /// Run-in delay (positive value: delay)单位微秒
        /// </summary>
        public float Nprev { get; set; }

        /// <summary>
        /// Run-out delay (positive value: delay)单位微秒
        /// </summary>
        public float Npost { get; set; }

        /// <summary>
        /// 角度限制，单位为度，小于该角度不启用Sky Writing功能
        /// </summary>
        public float AngleLimit {  get; set; }


        public override bool Equals(Object? settings)
        {
            if (settings == null) return false;
            if (settings is not SkyWritingCommand other) return false;
            return this.SkyWritingModel == other.SkyWritingModel
                && Math.Abs(this.Timelag - other.Timelag) < 0.01
                && Math.Abs(this.LaserOnShift - other.LaserOnShift) < 0.01
                && Math.Abs(this.Nprev - other.Nprev) < 0.01
                && Math.Abs(this.Npost - other.Npost) < 0.01
                && Math.Abs(this.AngleLimit - other.AngleLimit) < 0.01;
        }

    }
}
