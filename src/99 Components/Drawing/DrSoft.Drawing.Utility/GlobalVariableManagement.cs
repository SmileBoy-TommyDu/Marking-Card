using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Utility
{
    public static class GlobalVariableManagement
    {

        /// <summary>
        /// 曲线采样精度，值越小，精度越高，标定越平滑，耗时越久
        /// </summary>
        public static double Resolution { get; private set; } = 0.02;

        public static void SetResolution(double resolution)
        {
            if (resolution < 0.0001)
            {
                throw new Exception("精度不能超过0.0001mm");
            }

            if (resolution > 1)
            {
                throw new Exception("精度不能小于1mm");
            }

            Resolution = resolution;
        }


    }
}
