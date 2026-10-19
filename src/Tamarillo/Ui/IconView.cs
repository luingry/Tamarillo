using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Tamarillo.Ui
{
    /// <summary>Lookup for the Tamarillo icon set (Theme/Icons.xaml), loaded once.</summary>
    public static class Icons
    {
        private static ResourceDictionary _dictionary;
        private static readonly Dictionary<string, Geometry> Cache = new Dictionary<string, Geometry>(StringComparer.Ordinal);

        public static Geometry Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return Geometry.Empty;
            }
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out Geometry cached))
                {
                    return cached;
                }
                if (_dictionary == null)
                {
                    _dictionary = new ResourceDictionary { Source = new Uri("/Tamarillo.Module;component/Theme/Icons.xaml", UriKind.Relative) };
                }
                Geometry geometry = _dictionary[key] as Geometry ?? Geometry.Empty;
                if (!geometry.IsFrozen && geometry.CanFreeze)
                {
                    geometry.Freeze();
                }
                Cache[key] = geometry;
                return geometry;
            }
        }
    }

    /// <summary>
    /// Draws one icon of the set: a 24-grid stroke geometry scaled to the element's size, 1.8 stroke at 24 px
    /// (scaled with it), round caps and joins. Cheap: one frozen pen per brush, one draw call.
    /// </summary>
    public sealed class IconView : FrameworkElement
    {
        public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
            nameof(Data), typeof(Geometry), typeof(IconView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
            nameof(Icon), typeof(string), typeof(IconView), new PropertyMetadata(null, (d, e) => ((IconView)d).Data = Icons.Get((string)e.NewValue)));

        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(IconView),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits, (d, e) => ((IconView)d)._pen = null));

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            nameof(StrokeThickness), typeof(double), typeof(IconView), new FrameworkPropertyMetadata(1.8, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((IconView)d)._pen = null));

        private Pen _pen;

        public IconView()
        {
            Width = 20;
            Height = 20;
            IsHitTestVisible = false;
            SnapsToDevicePixels = false;
        }

        public Geometry Data
        {
            get => (Geometry)GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public string Icon
        {
            get => (string)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            Geometry data = Data;
            if (data == null || data.IsEmpty())
            {
                return;
            }
            if (_pen == null)
            {
                _pen = new Pen(Foreground, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                // Freezing a pen freezes its brush too: only do it when the brush is already frozen, so
                // callers can keep animating theirs (the wheel animates icon colours).
                if (_pen.CanFreeze && (Foreground == null || Foreground.IsFrozen))
                {
                    _pen.Freeze();
                }
            }
            double scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(null, _pen, data);
            dc.Pop();
        }
    }
}
