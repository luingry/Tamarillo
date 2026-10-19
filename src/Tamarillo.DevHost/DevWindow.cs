using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SwissTechKnife.Sdk;

namespace Tamarillo.DevHost
{
    /// <summary>Host stand-in: the module page, and the settings tab framed like the host's details dialog.</summary>
    internal sealed class DevWindow : Window
    {
        public DevWindow(TamarilloModule module, DevContext context)
        {
            Title = "Tamarillo · DevHost";
            Width = 1120;
            Height = 820;
            Background = (Brush)FindResource("Brush.Canvas");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var toast = new TextBlock { Style = (Style)FindResource("Text.Caption"), Margin = new Thickness(28, 0, 28, 14) };
            context.Notified += (message, kind) => toast.Text = kind + ": " + message;

            var settingsCard = new Border
            {
                Style = (Style)FindResource("Card"),
                Background = (Brush)FindResource("Brush.Surface2"),
                Width = 548,
                Padding = new Thickness(24),
                Margin = new Thickness(0, 8, 28, 28),
                VerticalAlignment = VerticalAlignment.Top,
                Child = module.CreateSettingsView(),
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock { Text = "Tamarillo", Style = (Style)FindResource("Text.Display"), Margin = new Thickness(28, 24, 28, 12) };
            grid.Children.Add(header);

            FrameworkElement page = module.CreateView();
            Grid.SetRow(page, 1);
            grid.Children.Add(page);
            var settingsScroll = new ScrollViewer { Content = settingsCard };
            Grid.SetRow(settingsScroll, 1);
            Grid.SetColumn(settingsScroll, 1);
            grid.Children.Add(settingsScroll);
            Grid.SetRow(toast, 2);
            Grid.SetColumnSpan(toast, 2);
            grid.Children.Add(toast);
            Content = grid;

            Loaded += (s, e) => (page as IModuleViewLifecycle)?.OnViewShown();
        }
    }
}
