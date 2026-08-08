using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using DrSoft.Docking.Enum;

namespace DrSoft.Docking.Interface
{
    public interface IDropWindow
    {
        void Hide();
        void Show();
        void Close();
        void Update(Point mouseP);
    }
}