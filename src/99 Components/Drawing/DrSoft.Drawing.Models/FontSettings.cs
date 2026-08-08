using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.Drawing.Model
{
    [Flags]
    public enum FontSettingsFields
    {
        None = 0,
        FontFamily = 1 << 0,
        FontSize = 1 << 1,
        IsBold = 1 << 2,
        IsItalic = 1 << 3,
        IsUnderline = 1 << 4,
        IsVerticalLayout = 1 << 5,
        HorizontalAlign = 1 << 6,
        VerticalAlign = 1 << 7,
        LineHeight = 1 << 8,
        CharacterSpacing = 1 << 9,
        TextColor = 1 << 10,
        All = FontFamily | FontSize | IsBold | IsItalic | IsUnderline | IsVerticalLayout | HorizontalAlign | VerticalAlign | LineHeight | CharacterSpacing | TextColor
    }

    public class FontSettings
    {
        /// <summary>
        /// 字体 (Font)
        /// </summary>
        public string FontFamily { get; set; } = "微软雅黑";
        /// <summary>
        /// 字号 (Font Size)
        /// </summary>
        public float FontSize
        {
            get;
            set;
        } = 10;
        /// <summary>
        /// 加粗 (Bold)
        /// </summary>
        public bool IsBold { get; set; } = false;
        /// <summary>
        /// 倾斜 (Italic)
        /// </summary>
        public bool IsItalic { get; set; } = false;
        /// <summary>
        /// 底线 (Underline)
        /// </summary>
        public bool IsUnderline { get; set; } = false;
        public bool IsVerticalLayout { get; set; } = false;
        /// <summary>
        /// 文字水平对齐 (Horizontal Text Align)
        /// </summary>
        public SKTextAlign HorizontalAlign { get; set; } = SKTextAlign.Left;

        /// <summary>
        /// 文字垂直对齐 (Vertical Text Align)，对应 DXF group 74：
        /// 0=Baseline, 1=Bottom, 2=Middle, 3=Top
        /// 默认 2 (Middle) 保持与现有绘制行为一致。
        /// </summary>
        public int VerticalAlign { get; set; } = 2;

        /// <summary>
        /// 行距 (Line Height)
        /// </summary>
        public float LineHeight { get; set; } = 0;
        /// <summary>
        /// 字距 (Character Spacing)
        /// </summary>
        public float CharacterSpacing
        {
            get;
            set;
        } = 0;

        public SKColor TextColor { get; set; } = SKColors.Black;
    }
}
