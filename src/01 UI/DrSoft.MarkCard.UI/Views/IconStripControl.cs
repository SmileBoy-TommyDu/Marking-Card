using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DrSoft.MarkCard.UI.Views
{
    public class IconStripControl : FrameworkElement
    {
        public static readonly DependencyProperty OrientationProperty =
            DependencyProperty.Register(nameof(Orientation), typeof(Orientation),
                typeof(IconStripControl),
                new FrameworkPropertyMetadata(Orientation.Vertical,
                    FrameworkPropertyMetadataOptions.AffectsRender));

        public Orientation Orientation
        {
            get => (Orientation)GetValue(OrientationProperty);
            set => SetValue(OrientationProperty, value);
        }

        // Box dimensions
        private const double BoxSize    = 32.0;   // outer box size (32×32)
        private const double InnerSize  = 20.0;   // inner dark square size
        private const double BoxSpacing = 34.0;   // centre-to-centre gap (32 + 2px margin)
        private const double PadStart   = 2.0;    // leading margin before first box centre

        // Brushes / pens (static for performance)
        private static readonly Brush OuterBg    = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC));
        private static readonly Brush InnerFill   = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
        private static readonly Pen   OuterBorder = new Pen(
            new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), 1.0);

        static IconStripControl()
        {
            // Freeze brushes for rendering performance
            OuterBg.Freeze();
            InnerFill.Freeze();
            OuterBorder.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            bool isHorizontal = Orientation == Orientation.Horizontal;
            double totalLength = isHorizontal ? w : h;
            double crossCentre = isHorizontal ? h / 2.0 : w / 2.0;

            double pos = PadStart + BoxSize / 2.0;

            while (pos + BoxSize / 2.0 <= totalLength)
            {
                double cx = isHorizontal ? pos : crossCentre;
                double cy = isHorizontal ? crossCentre : pos;

                // Outer box
                var outerRect = new Rect(
                    cx - BoxSize  / 2.0,
                    cy - BoxSize  / 2.0,
                    BoxSize, BoxSize);
                dc.DrawRectangle(OuterBg, OuterBorder, outerRect);

                // Inner dark square
                var innerRect = new Rect(
                    cx - InnerSize / 2.0,
                    cy - InnerSize / 2.0,
                    InnerSize, InnerSize);
                dc.DrawRectangle(InnerFill, null, innerRect);

                pos += BoxSpacing;
            }
        }
    }
}
