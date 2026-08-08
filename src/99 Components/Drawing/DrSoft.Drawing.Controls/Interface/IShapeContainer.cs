using DrSoft.Drawing.Controls.DrawShapes;
using DrSoft.Drawing.Model;
using SkiaSharp;

namespace DrSoft.Drawing.Controls.Interface
{
    internal interface IContainer
    {
        abstract ChildCollection Children { get; init; }
        int ChildCount => Children.Count;
    }
}
