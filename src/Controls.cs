using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace WinGroups
{
    // A grid that fits as many equal-width columns as the space allows (like CSS auto-fill).
    public class AdaptiveGrid : Panel
    {
        public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
            "MinItemWidth", typeof(double), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
            "Spacing", typeof(double), typeof(AdaptiveGrid),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public double MinItemWidth { get { return (double)GetValue(MinItemWidthProperty); } set { SetValue(MinItemWidthProperty, value); } }
        public double Spacing { get { return (double)GetValue(SpacingProperty); } set { SetValue(SpacingProperty, value); } }

        int Columns(double width)
        {
            return Math.Max(1, (int)Math.Floor((width + Spacing) / (MinItemWidth + Spacing)));
        }

        double ItemWidth(double width, int cols)
        {
            return Math.Max(0, (width - Spacing * (cols - 1)) / cols);
        }

        protected override Size MeasureOverride(Size available)
        {
            double width = double.IsInfinity(available.Width) ? MinItemWidth * 4 + Spacing * 3 : available.Width;
            int cols = Columns(width);
            double iw = ItemWidth(width, cols);
            double total = 0, rowH = 0;
            int i = 0;
            foreach (UIElement c in InternalChildren)
            {
                c.Measure(new Size(iw, double.PositiveInfinity));
                rowH = Math.Max(rowH, c.DesiredSize.Height);
                i++;
                if (i % cols == 0) { total += rowH + Spacing; rowH = 0; }
            }
            if (i % cols != 0) total += rowH;
            else if (i > 0) total -= Spacing;
            return new Size(width, Math.Max(0, total));
        }

        protected override Size ArrangeOverride(Size final)
        {
            int cols = Columns(final.Width);
            double iw = ItemWidth(final.Width, cols);
            int count = InternalChildren.Count;
            double y = 0;
            for (int start = 0; start < count; start += cols)
            {
                double rowH = 0;
                for (int k = start; k < Math.Min(count, start + cols); k++) rowH = Math.Max(rowH, InternalChildren[k].DesiredSize.Height);
                for (int k = start; k < Math.Min(count, start + cols); k++)
                    InternalChildren[k].Arrange(new Rect((k - start) * (iw + Spacing), y, iw, rowH));
                y += rowH + Spacing;
            }
            return final;
        }
    }

    public class InverseBoolVis : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool && (bool)value ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
