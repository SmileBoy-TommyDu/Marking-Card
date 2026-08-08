using SkiaSharp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DrSoft.MarkCard.UI.Models
{
    public class ShareTextModel:INotifyPropertyChanged
    {
        private string _text;
        private string _currentFontFamily;
        private double _fontSize;
        private bool _isBold;
        private bool _isItalic;
        private bool _isUnderline;
        private bool _isVerticalLayout;
        private int _textAlign;
        private double _lineHeight;
        private double _charSpacing;

        public string Text 
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged();
            }
        }

        // 字体与样式
        public string CurrentFontFamily 
        {
            get => _currentFontFamily;
            set
            {
                _currentFontFamily = value;
                OnPropertyChanged();
            }
        }

        // 排版参数
        public double FontSize
        {
            get => _fontSize;
            set
            {
                _fontSize = value;
                OnPropertyChanged();
            }
        }
        public bool IsBold
        {
            get => _isBold;
            set
            {
                _isBold = value;
                OnPropertyChanged();
            }
        }
        /// <summary>
        /// 倾斜 (Italic)
        /// </summary>
        public bool IsItalic
        {
            get => _isItalic;
            set
            {
                _isItalic = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 底线 (Underline)
        /// </summary>
        public bool IsUnderline
        {
            get => _isUnderline;
            set
            {
                _isUnderline = value;
                OnPropertyChanged();
            }
        }
        public bool IsVerticalLayout
        {
            get => _isVerticalLayout;
            set
            {
                _isVerticalLayout = value;
                OnPropertyChanged();
            }
        }
        /// <summary>
        /// 文字对齐 (Text Align)
        /// </summary>
        public int TextAlign
        {
            get => _textAlign;
            set
            {
                _textAlign = value;
                OnPropertyChanged();
            }
        }

        public double LineHeight
        {
            get => _lineHeight;
            set
            {
                _lineHeight = value;
                OnPropertyChanged();
            }
        }
        public double CharSpacing
        {
            get => _charSpacing;
            set
            {
                _charSpacing = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
