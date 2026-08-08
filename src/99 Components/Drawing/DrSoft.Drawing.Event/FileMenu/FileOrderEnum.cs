using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Event
{
    public enum FileOrderEnum
    {
        /// <summary>
        /// 切换页面
        /// </summary>
        [Description("切换页面")]
        ChangePage,
        /// <summary>
        /// 确认弹框
        /// </summary>
        [Description("确认弹框")]
        PopUp,
        /// <summary>
        /// 存在画布
        /// </summary>
        [Description("存在画布")]
        Exist,
        /// <summary>
        /// 新建
        /// </summary>
        [Description("新建")]
        New,
        /// <summary>
        /// 打开
        /// </summary>
        [Description("打开")]
        Open,
        /// <summary>
        /// 关闭
        /// </summary>
        [Description("关闭")]
        Close,
        /// <summary>
        /// 保存
        /// </summary>
        [Description("保存")]
        Save,
        /// <summary>
        /// 另存为
        /// </summary>
        [Description("另存为")]
        SaveAs,
        /// <summary>
        /// 导入
        /// </summary>
        [Description("导入")]
        LoadDXF,
        /// <summary>
        /// 导出
        /// </summary>
        [Description("导出")]
        OutDXF,
        /// <summary>
        /// 导入/导出参数
        /// </summary>
        [Description("导入/导出参数")]
        ImportExportParams
    }
}
