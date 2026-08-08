using SkiaSharp;

namespace DrSoft.Drawing.Model
{
    public class TextModel
    {
        /// <summary>
        /// 输入文字
        /// </summary>
        public string Text { get; set; }

        public FontSettings FontSettings { get; set; } = new FontSettings();

        public float CenterX { get; set; } = 0;
        public float CenterY { get; set; } = 0;
        public float ScaleX { get; set; } = 1.0f;
        public float ScaleY { get; set; } = 1.0f;
    }
}
