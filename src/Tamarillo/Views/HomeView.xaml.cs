using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SwissTechKnife.Sdk;
using Tamarillo.Core;
using Tamarillo.Ui;

namespace Tamarillo.Views
{
    /// <summary>The module's page: what it is, how to use it, a place to try it, and what this PC supports.</summary>
    public partial class HomeView : IModuleViewLifecycle
    {
        private readonly TamarilloController _controller;
        private bool _visible;

        public HomeView(TamarilloController controller)
        {
            _controller = controller;
            Resources.MergedDictionaries.Add(Ui.Theme.Resources);
            InitializeComponent();
            controller.StatusChanged += OnStatusChanged;
            controller.Settings.Changed += OnSettingsChanged;
            Refresh();
        }

        public void OnViewShown()
        {
            _visible = true;
            Refresh();
        }

        public void OnViewHidden() => _visible = false;

        private void OnStatusChanged() => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_visible || !IsLoaded)
            {
                Refresh();
            }
        }));

        private void OnSettingsChanged(TamarilloSettings settings) => Dispatcher.BeginInvoke(new Action(Refresh));

        private void Refresh()
        {
            TamarilloSettings settings = _controller.Settings.Current;
            HeroKeys.Shortcut = settings.Shortcut;
            StepHold.Text = "2  Segure " + settings.Shortcut;
            SelectionTip.Visibility = settings.SelectionTrigger ? Visibility.Visible : Visibility.Collapsed;

            bool ok = _controller.HookActive;
            StatusText.Text = ok ? "Pronto" : "Atalho indisponível";
            Brush strong = (Brush)FindResource(ok ? "Brush.Success" : "Brush.Danger");
            StatusText.Foreground = strong;
            StatusDot.Fill = strong;
            StatusChip.Background = (Brush)FindResource(ok ? "Brush.Success.Soft" : "Brush.Danger.Soft");

            DetectingText.Visibility = _controller.IsDetecting ? Visibility.Visible : Visibility.Collapsed;
            Engines.ItemsSource = EngineRows.Build(_controller.Capabilities);
        }

        // ═══ Try-it zone ═══

        private void OnZoneDragEnter(object sender, DragEventArgs e)
        {
            UpdateEffects(e);
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(1, Ui.Theme.Duration("Motion.Fast")));
                DropIcon.Foreground = (Brush)FindResource("Brush.Accent");
            }
        }

        private void OnZoneDragOver(object sender, DragEventArgs e) => UpdateEffects(e);

        private void OnZoneDragLeave(object sender, DragEventArgs e) => ResetZone();

        private void OnZoneDrop(object sender, DragEventArgs e)
        {
            ResetZone();
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                // After the drop completes, so the drag source is released first.
                Dispatcher.BeginInvoke(new Action(() => _controller.ShowForFiles(paths)), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private static void UpdateEffects(DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy & e.AllowedEffects : DragDropEffects.None;
            e.Handled = true;
        }

        private void ResetZone()
        {
            DropGlow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Ui.Theme.Duration("Motion.Normal")));
            DropIcon.Foreground = (Brush)FindResource("Brush.Text.Tertiary");
        }
    }
}
