using System.Windows;
using System.Windows.Controls;
using Tamarillo.Core;

namespace Tamarillo.Views
{
    /// <summary>Renders a shortcut as keycaps: [Ctrl] + [Alt] + [Shift]. Design-system tokens only.</summary>
    public sealed class Keycaps : StackPanel
    {
        public static readonly DependencyProperty ShortcutProperty = DependencyProperty.Register(
            nameof(Shortcut), typeof(Shortcut), typeof(Keycaps), new PropertyMetadata(null, (d, e) => ((Keycaps)d).Rebuild()));

        public Keycaps()
        {
            Orientation = Orientation.Horizontal;
            VerticalAlignment = VerticalAlignment.Center;
        }

        public Shortcut Shortcut
        {
            get => (Shortcut)GetValue(ShortcutProperty);
            set => SetValue(ShortcutProperty, value);
        }

        /// <summary>Background of each cap (Surface3 on cards, Surface4 inside dialogs).</summary>
        public string CapBrushKey { get; set; } = "Brush.Surface3";

        private void Rebuild()
        {
            Children.Clear();
            if (Shortcut == null)
            {
                return;
            }
            bool first = true;
            foreach (string cap in Shortcut.Keycaps)
            {
                if (!first)
                {
                    Children.Add(new TextBlock
                    {
                        Text = "+",
                        Style = (Style)FindResource("Text.Caption"),
                        Margin = new Thickness(6, 0, 6, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                }
                first = false;
                Children.Add(new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource(CapBrushKey),
                    CornerRadius = (CornerRadius)FindResource("Radius.Sm"),
                    Padding = new Thickness(8, 3, 8, 4),
                    MinWidth = 26,
                    Child = new TextBlock
                    {
                        Text = cap,
                        Style = (Style)FindResource("Text.BodyStrong"),
                        FontSize = (double)FindResource("FontSize.Caption"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                    },
                });
            }
        }
    }
}
