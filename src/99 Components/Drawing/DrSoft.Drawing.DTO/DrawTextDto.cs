using DrSoft.Drawing.Model;


namespace DrSoft.Drawing.DTO
{

    public class DrawTextDto : DrawObjectDto
    {
        
        public string? Text { get; set; }

        /// <summary>
        /// 字体 (Font)
        /// </summary>
     
        public string FontFamily { get; set; }
        /// <summary>
        /// 字号 (Font Size)
        /// </summary>

        public float FontSize { get; set; }
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
     
        public bool IsUnderline { get; set; }
     
        public bool IsVerticalLayout { get; set; }
        /// <summary>
        /// 文字对齐 (Text Align)
        /// </summary>
    
        public int HorizontalAlign { get; set; }

        /// <summary>
        /// 行距 (Line Height)
        /// </summary>

        public float LineHeight { get; set; } 
        /// <summary>
        /// 字距 (Character Spacing)
        /// </summary>

        public float CharacterSpacing { get; set; } 


 
        public Point2D CurrentCenterPoint { get; set; }

    }
}
