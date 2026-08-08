using DrSoft.Drawing.Model;

namespace DrSoft.Drawing.DTO
{
    public class FontSettingsDto
    {
        /// <summary>
        /// 字体 (Font)
        /// </summary>
        public string FontFamily { get; set; } = "微软雅黑";

        /// <summary>
        /// 字号 (Font Size)
        /// </summary>
        public float FontSize { get; set; } = 10;

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
        public int HorizontalAlign { get; set; } = 0;

        /// <summary>
        /// 文字垂直对齐 (Vertical Text Align)，0=Baseline, 1=Bottom, 2=Middle, 3=Top
        /// </summary>
        public int VerticalAlign { get; set; } = 2;

        /// <summary>
        /// 行距 (Line Height)
        /// </summary>
        public float LineHeight { get; set; } = 1.2f;

        /// <summary>
        /// 字距 (Character Spacing)
        /// </summary>
        public float CharacterSpacing { get; set; } = 0;

        public FontSettingsFields UpdatedFields { get; set; } = FontSettingsFields.All;
    }
}
