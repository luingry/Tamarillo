using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Tamarillo.Native;

namespace Tamarillo.Ui
{
    /// <summary>
    /// An image pinned above everything ("Fixar na tela"): drag to move, scroll to resize, double-click or
    /// the close button to remove. Never takes focus.
    /// </summary>
    public sealed class FloatWindow : Window
    {
        private readonly Border _frame;
        private readonly Button _close;
        private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
        private readonly double _aspect;

        public FloatWindow(BitmapSource image, string title)
        {
            Resources.MergedDictionaries.Add(Theme.Resources);
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Title = title;
            UseLayoutRounding = true;
            _aspect = (double)image.PixelWidth / Math.Max(1, image.PixelHeight);

            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            NativeMethods.GetMonitorAt(cursor, out NativeMethods.RECT work, out double dpi);
            double maxW = (work.Right - work.Left) / dpi * 0.38, maxH = (work.Bottom - work.Top) / dpi * 0.42;
            double w = Math.Min(maxW, Math.Min(image.PixelWidth / dpi, maxH * _aspect));
            w = Math.Max(120, w);
            double margin = 16;

            var picture = new Image { Source = image, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            _frame = new Border
            {
                CornerRadius = (CornerRadius)Resources["Radius.Lg"],
                Background = (Brush)Resources["Brush.Surface2"],
                Effect = (System.Windows.Media.Effects.Effect)Resources["Elevation.Popup"],
                Margin = new Thickness(margin),
                Child = picture,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = _scale,
            };
            picture.Loaded += (s, e) => picture.Clip = new RectangleGeometry(new Rect(0, 0, picture.ActualWidth, picture.ActualHeight), 14, 14);
            picture.SizeChanged += (s, e) => picture.Clip = new RectangleGeometry(new Rect(0, 0, picture.ActualWidth, picture.ActualHeight), 14, 14);

            _close = new Button
            {
                Style = (Style)Resources["Tama.RoundButton"],
                Width = 28,
                Height = 28,
                Background = (Brush)Resources["Brush.Tama.Ring"],
                Content = new IconView { Icon = "Icon.Close", Width = 14, Height = 14, Foreground = (Brush)Resources["Brush.Text.Primary"] },
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, margin + 8, margin + 8, 0),
                ToolTip = "Desafixar",
                Opacity = 0,
            };
            _close.Click += (s, e) => Unpin();

            var root = new Grid();
            root.Children.Add(_frame);
            root.Children.Add(_close);
            Content = root;
            Width = w + 2 * margin;
            Height = w / _aspect + 2 * margin;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = cursor.X / dpi - Width / 2;
            Top = cursor.Y / dpi - Height / 2;

            MouseEnter += (s, e) => _close.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Theme.Duration("Motion.Fast")));
            MouseLeave += (s, e) => _close.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Theme.Duration("Motion.Normal")));
            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    Unpin();
                }
                else
                {
                    try
                    {
                        DragMove();
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }
            };
            MouseWheel += (s, e) =>
            {
                double factor = e.Delta > 0 ? 1.1 : 1 / 1.1;
                double newWidth = Math.Max(120, Math.Min(4000, (Width - 2 * margin) * factor));
                double cx = Left + Width / 2, cy = Top + Height / 2;
                Width = newWidth + 2 * margin;
                Height = newWidth / _aspect + 2 * margin;
                Left = cx - Width / 2;
                Top = cy - Height / 2;
            };
            SourceInitialized += (s, e) => NativeMethods.MakeToolWindow(new WindowInteropHelper(this).Handle);
            Loaded += (s, e) =>
            {
                var pop = new DoubleAnimation(0.9, 1, Theme.Duration("Motion.Wheel.In")) { EasingFunction = Theme.Ease("Ease.Pop") };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Theme.Duration("Motion.Normal")));
            };
        }

        private void Unpin()
        {
            var fade = new DoubleAnimation(0, Theme.Duration("Motion.Overlay.Out"));
            fade.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fade);
            if (!Theme.Animate)
            {
                Close();
            }
        }
    }
}
