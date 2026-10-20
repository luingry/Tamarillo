using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tamarillo.Engines;
using Tamarillo.Jobs;

namespace Tamarillo.Ui
{
    /// <summary>
    /// One job in the overlay. Running: progress ring around the action icon. Done: the ring becomes a green
    /// check, the title is the output name (click = open) and the detail its folder (click = show in Explorer).
    /// Failed: alert disc and the reason. The corner X cancels a running job and dismisses a finished one;
    /// finished cards also leave by themselves, the timer paused while the pointer is on the card.
    /// </summary>
    public partial class JobCard
    {
        private readonly DispatcherTimer _autoDismiss;
        private bool _leaving;

        public JobCard(Job job, TimeSpan? autoDismissAfter)
        {
            Resources.MergedDictionaries.Add(Theme.Resources);
            InitializeComponent();
            Job = job;
            ActionIcon.Icon = job.Node.Icon;
            if (autoDismissAfter.HasValue)
            {
                _autoDismiss = new DispatcherTimer { Interval = autoDismissAfter.Value };
                _autoDismiss.Tick += (s, e) => Leave();
                MouseEnter += (s, e) => _autoDismiss.Stop();
                MouseLeave += (s, e) =>
                {
                    if (IsFinished && !_leaving)
                    {
                        _autoDismiss.Start();
                    }
                };
            }
            job.PropertyChanged += OnJobChanged;
            Update(animate: false);
            if (IsFinished)
            {
                ShowFinished(animate: false);
            }
        }

        public Job Job { get; }

        private bool IsFinished => Job.State == JobState.Done || Job.State == JobState.Failed;

        /// <summary>Raised once the exit animation has finished: remove the card.</summary>
        public event Action<JobCard> Left;

        public void Enter()
        {
            Duration duration = Theme.Duration("Motion.Overlay.In");
            IEasingFunction ease = Theme.Ease("Ease.Emphasized");
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
            CardMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, duration) { EasingFunction = ease });
        }

        public void Leave()
        {
            if (_leaving)
            {
                return;
            }
            _leaving = true;
            _autoDismiss?.Stop();
            Job.PropertyChanged -= OnJobChanged;
            Duration duration = Theme.Duration("Motion.Overlay.Out");
            IEasingFunction ease = Theme.Ease("Ease.In");
            var fade = new DoubleAnimation(0, duration) { EasingFunction = ease };
            fade.Completed += (s, e) => Left?.Invoke(this);
            BeginAnimation(OpacityProperty, fade);
            CardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, duration) { EasingFunction = ease });
            CardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, duration) { EasingFunction = ease });
            if (!Theme.Animate)
            {
                Left?.Invoke(this);
            }
        }

        private void OnJobChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Job.State):
                    if (Job.State == JobState.Cancelled)
                    {
                        Leave();
                    }
                    else if (IsFinished)
                    {
                        Update(animate: true);
                        ShowFinished(animate: true);
                    }
                    break;
                default:
                    Update(animate: true);
                    break;
            }
        }

        private void Update(bool animate)
        {
            TitleText.Text = Job.Title;
            DetailText.Text = Job.Detail;
            if (Job.State == JobState.Running || Job.State == JobState.Queued)
            {
                if (Job.Progress < 0)
                {
                    if (Arc.Tag == null)
                    {
                        Arc.Data = RadialGeometry.Arc(new Point(18, 18), 16.5, 0.25);
                        Arc.Tag = "spin";
                        ArcSpin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
                    }
                }
                else
                {
                    if (Arc.Tag != null)
                    {
                        Arc.Tag = null;
                        ArcSpin.BeginAnimation(RotateTransform.AngleProperty, null);
                        ArcSpin.Angle = 0;
                    }
                    Arc.Data = RadialGeometry.Arc(new Point(18, 18), 16.5, Math.Max(0.02, Job.Progress));
                }
            }
        }

        private void ShowFinished(bool animate)
        {
            ArcSpin.BeginAnimation(RotateTransform.AngleProperty, null);
            CloseButton.ToolTip = "Fechar";
            bool done = Job.State == JobState.Done;
            FrameworkElement disc = done ? DoneDisc : FailedDisc;
            disc.Visibility = Visibility.Visible;
            Duration fast = Theme.Duration("Motion.Fast");
            Running.BeginAnimation(OpacityProperty, new DoubleAnimation(0, animate ? fast : new Duration(TimeSpan.Zero)));

            if (done)
            {
                bool hasOutput = Job.Outputs.Count > 0;
                TitleLink.IsHitTestVisible = hasOutput;
                DetailLink.IsHitTestVisible = hasOutput;
                TitleLink.ToolTip = hasOutput ? (Job.PrimaryOutput != null ? "Abrir" : "Mostrar no Explorador") : null;
                DetailLink.ToolTip = hasOutput ? "Mostrar no Explorador" : null;
                if (animate)
                {
                    var pop = new DoubleAnimation(0.4, 1, Theme.Duration("Motion.Check")) { EasingFunction = Theme.Ease("Ease.Pop") };
                    DoneScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                    DoneScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
                    DoneDisc.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fast));
                }
            }
            else
            {
                DetailText.TextWrapping = TextWrapping.Wrap;
                DetailText.MaxHeight = 34;
                DetailText.ToolTip = Job.Detail;
            }
            if (!IsMouseOver)
            {
                _autoDismiss?.Start();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            if (IsFinished)
            {
                Leave();
            }
            else
            {
                Job.Cancel();
            }
        }

        private void OnOpen(object sender, RoutedEventArgs e)
        {
            if (Job.PrimaryOutput != null && File.Exists(Job.PrimaryOutput))
            {
                ShellEngine.Open(Job.PrimaryOutput);
            }
            else
            {
                OnReveal(sender, e);
            }
        }

        private void OnReveal(object sender, RoutedEventArgs e)
        {
            string[] existing = Job.Outputs.Where(o => File.Exists(o) || Directory.Exists(o)).ToArray();
            if (existing.Length > 0)
            {
                ShellEngine.Reveal(existing);
            }
        }
    }
}
