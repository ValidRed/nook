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

    // Eased mouse-wheel scrolling: each notch sets a target and the view glides there instead of jumping.
    public static class SmoothScroll
    {
        public static void Attach(ScrollViewer sv)
        {
            double target = 0;
            bool running = false;
            TimeSpan last = TimeSpan.Zero;
            EventHandler tick = null;
            tick = (s, e) =>
            {
                var now = ((System.Windows.Media.RenderingEventArgs)e).RenderingTime;
                double dt = last == TimeSpan.Zero ? 16 : Math.Max(1, (now - last).TotalMilliseconds);
                last = now;
                double cur = sv.VerticalOffset, diff = target - cur;
                if (Math.Abs(diff) < 0.5)
                {
                    sv.ScrollToVerticalOffset(target);
                    System.Windows.Media.CompositionTarget.Rendering -= tick;
                    running = false;
                    last = TimeSpan.Zero;
                    return;
                }
                sv.ScrollToVerticalOffset(cur + diff * (1 - Math.Exp(-dt / 70.0))); // same feel at any refresh rate
            };
            sv.PreviewMouseWheel += (s, e) =>
            {
                if (sv.ScrollableHeight <= 0) return;
                if (!running) target = sv.VerticalOffset;
                target = Math.Max(0, Math.Min(sv.ScrollableHeight, target - e.Delta * 1.1));
                e.Handled = true;
                if (!running)
                {
                    running = true;
                    System.Windows.Media.CompositionTarget.Rendering += tick;
                }
            };
            sv.ScrollChanged += (s, e) => { if (!running) target = sv.VerticalOffset; };
        }
    }

    // Drag items of an ItemsControl to reorder them (the items source must be a list, like ObservableCollection).
    public static class Reorder
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(Reorder), new PropertyMetadata(false, OnEnabled));

        public static bool GetEnabled(DependencyObject d) { return (bool)d.GetValue(EnabledProperty); }
        public static void SetEnabled(DependencyObject d, bool v) { d.SetValue(EnabledProperty, v); }

        static object item;
        static Point start;
        static bool dragging;

        static void OnEnabled(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ic = d as ItemsControl;
            if (ic == null || !(bool)e.NewValue) return;
            ic.PreviewMouseLeftButtonDown += (s, a) =>
            {
                var c = ItemsControl.ContainerFromElement(ic, a.OriginalSource as DependencyObject) as FrameworkElement;
                item = c == null ? null : ic.ItemContainerGenerator.ItemFromContainer(c);
                start = a.GetPosition(ic);
                dragging = false;
            };
            ic.PreviewMouseMove += (s, a) =>
            {
                if (item == null || a.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
                var p = a.GetPosition(ic);
                if (!dragging)
                {
                    if (Math.Abs(p.X - start.X) < 5 && Math.Abs(p.Y - start.Y) < 5) return;
                    dragging = true;
                    ic.CaptureMouse();
                }
                var list = ic.ItemsSource as System.Collections.IList;
                if (list == null) return;
                int from = list.IndexOf(item), to = IndexAt(ic, p);
                if (from >= 0 && to >= 0 && to != from)
                {
                    list.RemoveAt(from);
                    list.Insert(to, item);
                }
                var held = ic.ItemContainerGenerator.ContainerFromItem(item) as UIElement;
                if (held != null) held.Opacity = 0.55;
            };
            System.Windows.Input.MouseButtonEventHandler up = (s, a) =>
            {
                if (dragging)
                {
                    var held = ic.ItemContainerGenerator.ContainerFromItem(item) as UIElement;
                    if (held != null) held.Opacity = 1;
                    ic.ReleaseMouseCapture();
                    a.Handled = true;
                }
                dragging = false;
                item = null;
            };
            ic.PreviewMouseLeftButtonUp += up;
        }

        static int IndexAt(ItemsControl ic, Point p)
        {
            for (int i = 0; i < ic.Items.Count; i++)
            {
                var c = ic.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (c == null) continue;
                var r = new Rect(c.TranslatePoint(new Point(0, 0), ic), new Size(c.ActualWidth, c.ActualHeight));
                if (r.Contains(p)) return i;
            }
            return -1;
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
