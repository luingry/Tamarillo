using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Tamarillo.Core;
using Tamarillo.Input;
using Tamarillo.Native;
using Tamarillo.Operations;

namespace Tamarillo.Ui
{
    public enum WheelMode
    {
        Hidden,

        /// <summary>Shown transparent under a drag, waiting for DragEnter to learn the files.</summary>
        Armed,

        /// <summary>Following an OLE drag: drop to choose.</summary>
        Drag,

        /// <summary>Opened by the chord with Explorer's selection: release the chord to choose.</summary>
        Hold,

        /// <summary>Waiting for a click (after dropping or releasing on a group).</summary>
        Click,
    }

    /// <summary>
    /// The radial menu: one reusable layered window, created once and shown under the cursor.
    /// </summary>
    /// <remarks>
    /// Interaction model (a marking menu): the centre is "cancel"; anywhere outward selects by angle, so
    /// targets are huge and a flick is enough. Hovering a group opens its sub-ring at once. Selection only arms after the pointer has moved a few pixels,
    /// so a wheel that appears clamped to a screen edge never fires what happens to be under the cursor.
    /// Rendering: every element is a frozen geometry; motion is opacity and render transforms only.
    /// Children are not hit-testable: the window itself is the only drop target, so DragEnter/DragLeave fire
    /// once per entry and geometry decides what is under the pointer.
    /// </remarks>
    internal sealed class RadialWindow : Window
    {
        private const double ArmDistance = 6;

        private const double PathOpacity = 0.35;

        private readonly double _hub, _inner, _outer, _subInner, _subOuter, _thirdInner, _thirdOuter, _gap, _corner, _subItemAngleMax, _thirdItemAngleMax, _size;
        private double _subItemAngle, _thirdItemAngle;
        private readonly Point _center;
        private readonly Grid _root = new Grid { IsHitTestVisible = false };
        private readonly ScaleTransform _rootScale = new ScaleTransform(1, 1);
        private readonly Canvas _ring = new Canvas();
        private readonly Canvas _sub = new Canvas();
        private readonly Canvas _third = new Canvas();
        private readonly Path _highlight = new Path { Opacity = 0 };
        private readonly RotateTransform _highlightRotation = new RotateTransform();
        private readonly Path _subHighlight = new Path { Opacity = 0 };
        private readonly RotateTransform _subHighlightRotation = new RotateTransform();
        private readonly Path _thirdHighlight = new Path { Opacity = 0 };
        private readonly RotateTransform _thirdHighlightRotation = new RotateTransform();
        private readonly Image _thumbnail = new Image { Stretch = Stretch.UniformToFill, Opacity = 0 };
        private readonly IconView _hubIcon = new IconView { Width = 26, Height = 26 };
        private readonly TextBlock _hubTitle = new TextBlock();
        private readonly TextBlock _hubCaption = new TextBlock();
        private readonly StackPanel _hubText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly DispatcherTimer _armTimeout;
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<Slot> _subSlots = new List<Slot>();
        private readonly List<Slot> _thirdSlots = new List<Slot>();

        private IReadOnlyList<MenuNode> _items = Array.Empty<MenuNode>();
        private FileSet _files;
        private WheelMode _mode;
        private int _top = -1;
        private int _openGroup = -1;
        private int _subIndex = -1;
        private int _openSub = -1;
        private int _thirdIndex = -1;
        private Point? _origin;
        private bool _armed;
        private bool _hadMouse;
        private bool _closing;
        private IntPtr _hwnd;

        public RadialWindow()
        {
            Resources.MergedDictionaries.Add(Theme.Resources);
            _hub = Theme.Number("Radial.Hub");
            _inner = Theme.Number("Radial.Inner");
            _outer = Theme.Number("Radial.Outer");
            _subInner = Theme.Number("Radial.SubInner");
            _subOuter = Theme.Number("Radial.SubOuter");
            _thirdInner = Theme.Number("Radial.ThirdInner");
            _thirdOuter = Theme.Number("Radial.ThirdOuter");
            _gap = Theme.Number("Radial.Gap");
            _corner = Theme.Number("Radial.Corner");
            _subItemAngleMax = Theme.Number("Radial.SubItemAngle");
            _subItemAngle = _subItemAngleMax;
            _thirdItemAngleMax = Theme.Number("Radial.ThirdItemAngle");
            _thirdItemAngle = _thirdItemAngleMax;
            _size = 2 * (_thirdOuter + Theme.Number("Radial.Margin"));
            _center = new Point(_size / 2, _size / 2);

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            // Alpha 1/255: invisible, but hit-testable, so the whole square is a drop target.
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));
            ResizeMode = System.Windows.ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            AllowDrop = true;
            UseLayoutRounding = true;
            Width = _size;
            Height = _size;
            Title = "Tamarillo";

            BuildChrome();
            Content = _root;

            _armTimeout = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(700) };
            _armTimeout.Tick += (s, e) =>
            {
                _armTimeout.Stop();
                if (_mode == WheelMode.Armed)
                {
                    Diagnostics.Log?.Info("Armed wheel saw no drag; hiding");
                    Dismiss(animated: false);
                }
            };

            DragEnter += OnDragEnter;
            DragOver += OnDragOver;
            DragLeave += OnDragLeave;
            Drop += OnDrop;
            MouseMove += (s, e) =>
            {
                _hadMouse = true;
                OnPointer(e.GetPosition(this));
            };
            MouseLeftButtonUp += OnClick;
            MouseLeave += OnMouseLeave;

            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            NativeMethods.MakeToolWindow(_hwnd);
        }

        /// <summary>Builds the menu for files dropped/entered in drag mode.</summary>
        public Func<FileSet, IReadOnlyList<MenuNode>> MenuProvider { get; set; }

        /// <summary>Async thumbnail for the hub (may return null).</summary>
        public Func<string, System.Threading.Tasks.Task<BitmapSource>> ThumbnailProvider { get; set; }

        /// <summary>An action was chosen. Raised after the wheel has started closing.</summary>
        public event Action<MenuNode, FileSet> Committed;

        /// <summary>Raised whenever the wheel goes back to hidden (committed or not).</summary>
        public event Action Dismissed;

        /// <summary>Raised when <see cref="WantsKeyboard"/> may have changed.</summary>
        public event Action ModeChanged;

        public WheelMode Mode => _mode;

        public IntPtr Handle => _hwnd;

        /// <summary>True while the wheel is visible and wants Esc/Enter/arrows.</summary>
        public bool WantsKeyboard => _mode == WheelMode.Hold || _mode == WheelMode.Click;

        // ═══ Opening ═══

        /// <summary>Drag mode: become an invisible drop target under the cursor and wait for the drag to enter.</summary>
        public void Arm(NativeMethods.POINT cursor)
        {
            if (_mode == WheelMode.Drag)
            {
                return;
            }
            ResetState();
            _mode = WheelMode.Armed;
            PlaceAt(cursor);
            _root.Opacity = 0;
            Show();
            _armTimeout.Start();
            // The drag loop only re-hit-tests on mouse moves: make it notice the window right away.
            Dispatcher.BeginInvoke(new Action(NativeMethods.NudgeMouse), DispatcherPriority.Input);
        }

        /// <summary>Hold mode: show for a known selection; releasing the chord chooses.</summary>
        public void ShowHold(FileSet files, IReadOnlyList<MenuNode> items, NativeMethods.POINT cursor)
        {
            ResetState();
            PlaceAt(cursor);
            Populate(files, items);
            Show();
            SetMode(WheelMode.Hold);
            AnimateIn();
        }

        /// <summary>Click mode: show for known files and wait for a click (module page try-it area).</summary>
        public void ShowClick(FileSet files, IReadOnlyList<MenuNode> items, NativeMethods.POINT cursor)
        {
            ResetState();
            PlaceAt(cursor);
            Populate(files, items);
            Show();
            SetMode(WheelMode.Click);
            AnimateIn();
        }

        /// <summary>Dev/test hook: feeds a pointer position in window coordinates (snapshots, tests).</summary>
        internal void SimulatePointer(Point position)
        {
            _armed = true;
            OnPointer(position);
        }

        /// <summary>Dev/test hook: opens a group without moving the pointer.</summary>
        internal void SimulateOpenGroup(int index) => OpenGroup(index);

        internal Point Center => _center;

        internal FrameworkElement Stage => _root;

        private void SetMode(WheelMode mode)
        {
            _mode = mode;
            ModeChanged?.Invoke();
        }

        private void ResetState()
        {
            _armTimeout.Stop();
            _closing = false;
            _top = -1;
            _openGroup = -1;
            _subIndex = -1;
            _openSub = -1;
            _thirdIndex = -1;
            _origin = null;
            _armed = false;
            _hadMouse = false;
            _root.BeginAnimation(OpacityProperty, null);
            _highlight.BeginAnimation(OpacityProperty, null);
            _highlight.Opacity = 0;
            _subHighlight.BeginAnimation(OpacityProperty, null);
            _subHighlight.Opacity = 0;
            _thirdHighlight.BeginAnimation(OpacityProperty, null);
            _thirdHighlight.Opacity = 0;
            _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        private void PlaceAt(NativeMethods.POINT cursor)
        {
            NativeMethods.GetMonitorAt(cursor, out NativeMethods.RECT work, out double scale);
            int size = (int)Math.Round(_size * scale);
            int x = Math.Max(work.Left, Math.Min(cursor.X - size / 2, work.Right - size));
            int y = Math.Max(work.Top, Math.Min(cursor.Y - size / 2, work.Bottom - size));
            // Twice: the first move may cross monitors and trigger a DPI change that WPF answers by resizing.
            for (int pass = 0; pass < 2; pass++)
            {
                NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, x, y, size, size, NativeMethods.SWP_NOACTIVATE);
            }
        }

        // ═══ Building ═══

        private void BuildChrome()
        {
            var stage = new Canvas { Width = _size, Height = _size };

            // Soft contact shadow: a radial gradient is far cheaper than a blur effect on a layered window.
            var shadow = new Ellipse
            {
                Width = 2 * (_outer + 22),
                Height = 2 * (_outer + 22),
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(System.Windows.Media.Color.FromArgb(110, 0, 0, 0), 0.78),
                    new GradientStop(System.Windows.Media.Color.FromArgb(0, 0, 0, 0), 1),
                }),
            };
            Canvas.SetLeft(shadow, _center.X - shadow.Width / 2);
            Canvas.SetTop(shadow, _center.Y - shadow.Height / 2 + 6);
            stage.Children.Add(shadow);

            stage.Children.Add(_ring);
            stage.Children.Add(_sub);
            stage.Children.Add(_third);

            AddHighlight(_ring, _highlight, _highlightRotation);
            AddHighlight(_sub, _subHighlight, _subHighlightRotation);
            _subHighlight.Data = RadialGeometry.Sector(_center, _subInner, _subOuter, -90 - _subItemAngle / 2, -90 + _subItemAngle / 2, _gap, _corner);
            AddHighlight(_third, _thirdHighlight, _thirdHighlightRotation);

            // Hub
            var hub = new Grid { Width = 2 * _hub, Height = 2 * _hub };
            hub.Children.Add(new Ellipse { Fill = Theme.Brush("Brush.Tama.Hub") });
            var thumbHost = new Grid { Margin = new Thickness(4) };
            thumbHost.Clip = new EllipseGeometry(new Point(_hub - 4, _hub - 4), _hub - 4, _hub - 4);
            thumbHost.Children.Add(_thumbnail);
            hub.Children.Add(thumbHost);
            _hubIcon.Foreground = Theme.Brush("Brush.Text.Secondary");
            _hubIcon.Margin = new Thickness(0, 0, 0, 6);
            _hubTitle.Style = (Style)Resources["Tama.Text.HubTitle"];
            _hubTitle.MaxWidth = 2 * _hub - 22;
            _hubCaption.Style = (Style)Resources["Tama.Text.HubCaption"];
            _hubCaption.MaxWidth = 2 * _hub - 26;
            _hubCaption.Margin = new Thickness(0, 2, 0, 0);
            _hubText.Children.Add(_hubIcon);
            _hubText.Children.Add(_hubTitle);
            _hubText.Children.Add(_hubCaption);
            hub.Children.Add(_hubText);
            Canvas.SetLeft(hub, _center.X - _hub);
            Canvas.SetTop(hub, _center.Y - _hub);
            stage.Children.Add(hub);

            _root.RenderTransformOrigin = new Point(0.5, 0.5);
            _root.RenderTransform = _rootScale;
            _root.Children.Add(stage);
        }

        /// <summary>A ring's selection wedge: drawn pointing up, rotated round the centre onto the chosen item.</summary>
        private void AddHighlight(Canvas ring, Path highlight, RotateTransform rotation)
        {
            highlight.Fill = Theme.Brush("Brush.Accent");
            highlight.RenderTransform = rotation;
            rotation.CenterX = _center.X;
            rotation.CenterY = _center.Y;
            ring.Children.Add(highlight);
        }

        private void Populate(FileSet files, IReadOnlyList<MenuNode> items)
        {
            _files = files;
            _items = items ?? Array.Empty<MenuNode>();
            foreach (Slot slot in _slots)
            {
                _ring.Children.Remove(slot.Layer);
                _ring.Children.Remove(slot.Background);
            }
            _slots.Clear();
            ClearSub();

            int count = Math.Max(1, _items.Count);
            double step = RadialGeometry.SlotAngle(count);
            _highlight.Data = RadialGeometry.Sector(_center, _inner, _outer, -90 - step / 2, -90 + step / 2, _gap, _corner);
            _highlight.Opacity = 0;

            for (int i = 0; i < _items.Count; i++)
            {
                double angle = RadialGeometry.SlotCenter(i, count);
                Slot slot = CreateSlot(_items[i], angle, step, _inner, _outer, Theme.Number("Radial.IconSize"), showLabel: true);
                _ring.Children.Insert(_ring.Children.IndexOf(_highlight), slot.Background);
                _ring.Children.Add(slot.Layer);
                _slots.Add(slot);
            }

            ShowSummary();
            _thumbnail.Source = null;
            _thumbnail.Opacity = 0;
            LoadThumbnail(files);
        }

        private Slot CreateSlot(MenuNode node, double angle, double span, double inner, double outer, double iconSize, bool showLabel)
        {
            var slot = new Slot { Node = node, Angle = angle };
            slot.Background = new Path
            {
                Data = RadialGeometry.Sector(_center, inner, outer, angle - span / 2, angle + span / 2, _gap, _corner),
                Fill = Theme.Brush("Brush.Tama.Ring"),
            };
            slot.Content = new SolidColorBrush(Theme.Color("Color.Text.Primary"));
            slot.Secondary = new SolidColorBrush(Theme.Color("Color.Text.Secondary"));
            slot.Layer = new Canvas();

            double mid = (inner + outer) / 2;
            Point at = RadialGeometry.Polar(_center, mid, angle);
            FrameworkElement glyph;
            if (node.Badge != null)
            {
                glyph = new TextBlock { Text = node.Badge, Style = (Style)Resources["Tama.Text.Badge"], Foreground = slot.Content };
            }
            else
            {
                glyph = new IconView { Icon = node.Icon, Width = iconSize, Height = iconSize, Foreground = slot.Content };
            }
            var stack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
            glyph.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(glyph);
            if (showLabel && node.Badge == null)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = node.Label,
                    Style = (Style)Resources["Tama.Text.Label"],
                    Foreground = slot.Secondary,
                    Margin = new Thickness(0, iconSize < Theme.Number("Radial.IconSize") ? 2 : 4, 0, 0),
                    // Never wider than the segment's chord at mid-radius, so narrow sub-ring items trim instead of spilling.
                    MaxWidth = Math.Min(74, 2 * mid * Math.Sin(span / 2 * Math.PI / 180) - 2 * _gap - 6),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }
            stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(stack, at.X - stack.DesiredSize.Width / 2);
            Canvas.SetTop(stack, at.Y - stack.DesiredSize.Height / 2);
            slot.Layer.Children.Add(stack);

            if (node.IsGroup)
            {
                // A small notch on the outer edge says "there is more here", kept in the margin outside the content.
                double margin = (outer - inner - stack.DesiredSize.Height) / 2;
                Point dot = RadialGeometry.Polar(_center, outer - Math.Min(7, margin / 2), angle);
                var notch = new Ellipse { Width = 4, Height = 4, Fill = slot.Secondary };
                Canvas.SetLeft(notch, dot.X - 2);
                Canvas.SetTop(notch, dot.Y - 2);
                slot.Layer.Children.Add(notch);
            }

            var scale = new ScaleTransform(1, 1, _center.X, _center.Y);
            slot.Layer.RenderTransform = scale;
            slot.Background.RenderTransform = scale;
            slot.Scale = scale;
            return slot;
        }

        private void LoadThumbnail(FileSet files)
        {
            if (ThumbnailProvider == null || files == null || files.Count == 0 || files.Items[0].IsDirectory)
            {
                return;
            }
            FileSet requested = files;
            ThumbnailProvider(files.Items[0].Path).ContinueWith(task =>
            {
                if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || task.Result == null)
                {
                    return;
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ReferenceEquals(_files, requested) || _mode == WheelMode.Hidden)
                    {
                        return;
                    }
                    _thumbnail.Source = task.Result;
                    UpdateHub();
                }));
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        // ═══ Motion ═══

        private void AnimateIn()
        {
            Duration duration = Theme.Duration("Motion.Wheel.In");
            IEasingFunction pop = Theme.Ease("Ease.Pop");
            IEasingFunction ease = Theme.Ease("Ease.Emphasized");
            _root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Theme.Duration("Motion.Fast")));
            _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = pop });
            _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.86, 1, duration) { EasingFunction = pop });

            TimeSpan stagger = Theme.Milliseconds("Motion.Wheel.Stagger");
            for (int i = 0; i < _slots.Count; i++)
            {
                Slot slot = _slots[i];
                TimeSpan begin = TimeSpan.FromTicks(stagger.Ticks * i);
                HideUntilAnimated(slot, 0.9);
                var fade = new DoubleAnimation(0, 1, Theme.Duration("Motion.Normal")) { BeginTime = begin, EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
                slot.Layer.BeginAnimation(OpacityProperty, fade);
                slot.Background.BeginAnimation(OpacityProperty, fade);
                var grow = new DoubleAnimation(0.9, 1, duration) { BeginTime = begin, EasingFunction = ease };
                slot.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                slot.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }

        /// <summary>
        /// Gives a slot the values its entrance animation starts from. A staggered animation does not apply
        /// its From before its BeginTime, so without this the slot shows fully for the delay, then blinks.
        /// </summary>
        private static void HideUntilAnimated(Slot slot, double scale)
        {
            if (!Theme.Animate)
            {
                return;
            }
            slot.Layer.Opacity = 0;
            slot.Background.Opacity = 0;
            slot.Scale.ScaleX = scale;
            slot.Scale.ScaleY = scale;
        }

        public void Dismiss(bool animated = true) => Close(animated, chosen: null);

        private void Close(bool animated, Slot chosen)
        {
            if (_mode == WheelMode.Hidden || _closing)
            {
                return;
            }
            _closing = true;
            _armTimeout.Stop();
            WheelMode previous = _mode;
            _mode = WheelMode.Hidden;
            ModeChanged?.Invoke();

            if (!animated || previous == WheelMode.Armed || !Theme.Animate)
            {
                FinishClose();
                return;
            }
            Duration duration = Theme.Duration("Motion.Wheel.Out");
            IEasingFunction ease = Theme.Ease("Ease.In");
            if (chosen != null)
            {
                // The chosen slot lifts slightly while the rest fades: a quiet "got it".
                var lift = new DoubleAnimation(1, 1.06, Theme.Duration("Motion.Fast")) { AutoReverse = false, EasingFunction = Theme.Ease("Ease.Out") };
                chosen.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, lift);
                chosen.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, lift);
            }
            var fade = new DoubleAnimation(_root.Opacity, 0, duration) { EasingFunction = ease, BeginTime = chosen != null ? TimeSpan.FromMilliseconds(60) : TimeSpan.Zero };
            fade.Completed += (s, e) =>
            {
                if (_mode == WheelMode.Hidden)
                {
                    FinishClose();
                }
            };
            _root.BeginAnimation(OpacityProperty, fade);
            var shrink = new DoubleAnimation(1, 0.94, duration) { EasingFunction = ease, BeginTime = fade.BeginTime };
            _rootScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
            _rootScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        }

        private void FinishClose()
        {
            Hide();
            _closing = false;
            _thumbnail.Source = null;
            _files = null;
            Dismissed?.Invoke();
        }

        // ═══ Pointer → selection ═══

        private void OnPointer(Point position)
        {
            if (_mode != WheelMode.Drag && _mode != WheelMode.Hold && _mode != WheelMode.Click)
            {
                return;
            }
            if (_origin == null)
            {
                _origin = position;
            }
            if (!_armed)
            {
                if ((position - _origin.Value).Length < ArmDistance)
                {
                    return;
                }
                _armed = true;
            }

            Vector v = position - _center;
            double radius = v.Length;
            double angle = RadialGeometry.AngleOf(v);

            if (radius < _inner - 2)
            {
                Select(-1, -1);
                return;
            }
            if (_openGroup >= 0 && radius > _outer + _gap)
            {
                if (_openSub >= 0 && radius > _subOuter + _gap)
                {
                    int leaf = RadialGeometry.SubItemAt(angle, _subSlots[_openSub].Angle, _thirdSlots.Count, _thirdItemAngle);
                    if (leaf >= 0)
                    {
                        Select(_openGroup, _openSub, leaf);
                        return;
                    }
                }
                int child = RadialGeometry.SubItemAt(angle, _slots[_openGroup].Angle, _subSlots.Count, _subItemAngle);
                if (child >= 0)
                {
                    Select(_openGroup, child);
                    return;
                }
            }
            int top = _slots.Count == 0 ? -1 : RadialGeometry.SlotAt(angle, _slots.Count);
            Select(top, -1);
        }

        private void Select(int top, int sub, int third = -1)
        {
            bool moved = false;
            if (top != _top)
            {
                _top = top;
                moved = true;
                if (_openGroup >= 0 && _openGroup != top)
                {
                    CloseGroup();
                }
                // Hovering a group reveals its sub-ring at once: any delay here reads as lag.
                if (top >= 0 && _items[top].IsGroup)
                {
                    OpenGroup(top);
                }
            }
            if (sub != _subIndex)
            {
                _subIndex = sub;
                moved = true;
                if (_openSub >= 0 && _openSub != sub)
                {
                    CloseSubGroup();
                }
                if (sub >= 0 && _subSlots[sub].Node.IsGroup)
                {
                    OpenSubGroup(sub);
                }
            }
            if (third != _thirdIndex)
            {
                _thirdIndex = third;
                moved = true;
            }
            if (moved)
            {
                // The deepest choice is lit; the rings it came through stay dimmed under it.
                MoveHighlight(_highlight, _highlightRotation, _top >= 0 ? _slots[_top].Angle : (double?)null, _subIndex >= 0 ? PathOpacity : 1);
                MoveHighlight(_subHighlight, _subHighlightRotation, _subIndex >= 0 ? _subSlots[_subIndex].Angle : (double?)null, _thirdIndex >= 0 ? PathOpacity : 1);
                MoveHighlight(_thirdHighlight, _thirdHighlightRotation, _thirdIndex >= 0 ? _thirdSlots[_thirdIndex].Angle : (double?)null, 1);
            }
            for (int i = 0; i < _slots.Count; i++)
            {
                SetSlotState(_slots[i], i == _top && _subIndex < 0, i == _top && _subIndex >= 0);
            }
            for (int i = 0; i < _subSlots.Count; i++)
            {
                SetSlotState(_subSlots[i], i == _subIndex && _thirdIndex < 0, i == _subIndex && _thirdIndex >= 0);
            }
            for (int i = 0; i < _thirdSlots.Count; i++)
            {
                SetSlotState(_thirdSlots[i], i == _thirdIndex, false);
            }
            UpdateHub();
        }

        /// <summary>Glides a ring's wedge onto <paramref name="angle"/> (the short way round), or fades it out when null.</summary>
        private static void MoveHighlight(Path highlight, RotateTransform rotation, double? angle, double opacity)
        {
            if (angle == null)
            {
                highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Theme.Duration("Motion.Fast")));
                return;
            }
            double target = angle.Value + 90;
            double current = highlight.Opacity < 0.05 ? target : rotation.Angle;
            double delta = RadialGeometry.Normalize(target - current);
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(current, current + delta, Theme.Duration("Motion.Highlight")) { EasingFunction = Theme.Ease("Ease.Out") });
            highlight.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, Theme.Duration("Motion.Fast")));
        }

        private static void SetSlotState(Slot slot, bool active, bool onPath)
        {
            if (slot.Active == active && slot.OnPath == onPath)
            {
                return;
            }
            slot.Active = active;
            slot.OnPath = onPath;
            System.Windows.Media.Color content = active ? Theme.Color("Color.OnAccent") : Theme.Color("Color.Text.Primary");
            System.Windows.Media.Color secondary = active ? Theme.Color("Color.OnAccent") : onPath ? Theme.Color("Color.Text.Primary") : Theme.Color("Color.Text.Secondary");
            Duration duration = active ? Theme.Duration("Motion.Fast") : Theme.Duration("Motion.Normal");
            slot.Content.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(content, duration));
            slot.Secondary.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(secondary, duration));
        }

        // ═══ Sub-ring ═══

        private void OpenGroup(int index)
        {
            if (index < 0 || index >= _items.Count || !_items[index].IsGroup || _openGroup == index)
            {
                return;
            }
            ClearSub();
            _openGroup = index;
            IReadOnlyList<MenuNode> children = _items[index].Children;
            // Long lists tighten so the sub-ring never wraps past ~210 degrees.
            _subItemAngle = Math.Min(_subItemAngleMax, 210.0 / Math.Max(1, children.Count));
            Unfold(_sub, _subHighlight, _subSlots, children, _slots[index].Angle, _subItemAngle, _subInner, _subOuter);
            UpdateHub();
        }

        /// <summary>Opens the third ring of sub-ring item <paramref name="index"/>, centred on it.</summary>
        private void OpenSubGroup(int index)
        {
            if (index < 0 || index >= _subSlots.Count || !_subSlots[index].Node.IsGroup || _openSub == index)
            {
                return;
            }
            ClearThird();
            _openSub = index;
            IReadOnlyList<MenuNode> children = _subSlots[index].Node.Children;
            _thirdItemAngle = Math.Min(_thirdItemAngleMax, 210.0 / Math.Max(1, children.Count));
            Unfold(_third, _thirdHighlight, _thirdSlots, children, _subSlots[index].Angle, _thirdItemAngle, _thirdInner, _thirdOuter);
            UpdateHub();
        }

        /// <summary>Lays <paramref name="children"/> out on a ring around <paramref name="around"/>, unfolding from the parent outward.</summary>
        private void Unfold(Canvas ring, Path highlight, List<Slot> slots, IReadOnlyList<MenuNode> children, double around, double itemAngle, double inner, double outer)
        {
            highlight.Data = RadialGeometry.Sector(_center, inner, outer, -90 - itemAngle / 2, -90 + itemAngle / 2, _gap, _corner);
            IEasingFunction ease = Theme.Ease("Ease.Emphasized");
            TimeSpan stagger = Theme.Milliseconds("Motion.Wheel.Stagger");
            for (int i = 0; i < children.Count; i++)
            {
                double angle = RadialGeometry.SubItemCenter(around, i, children.Count, itemAngle);
                Slot slot = CreateSlot(children[i], angle, itemAngle, inner, outer, Theme.Number("Radial.SubIconSize"), showLabel: true);
                ring.Children.Insert(ring.Children.IndexOf(highlight), slot.Background);
                ring.Children.Add(slot.Layer);
                slots.Add(slot);

                // Items unfold from the group outward, nearest first.
                double distance = Math.Abs(i - (children.Count - 1) / 2.0);
                var begin = TimeSpan.FromTicks((long)(stagger.Ticks * distance));
                HideUntilAnimated(slot, 0.92);
                var fade = new DoubleAnimation(0, 1, Theme.Duration("Motion.SubRing.In")) { BeginTime = begin, EasingFunction = ease };
                slot.Layer.BeginAnimation(OpacityProperty, fade);
                slot.Background.BeginAnimation(OpacityProperty, fade);
                var grow = new DoubleAnimation(0.92, 1, Theme.Duration("Motion.SubRing.In")) { BeginTime = begin, EasingFunction = ease };
                slot.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
                slot.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            }
        }

        private void CloseGroup()
        {
            _openGroup = -1;
            _subIndex = -1;
            ClearSub();
            _subHighlight.BeginAnimation(OpacityProperty, null);
            _subHighlight.Opacity = 0;
        }

        private void CloseSubGroup()
        {
            _openSub = -1;
            _thirdIndex = -1;
            ClearThird();
            _thirdHighlight.BeginAnimation(OpacityProperty, null);
            _thirdHighlight.Opacity = 0;
        }

        private void ClearSub()
        {
            CloseSubGroup();
            Clear(_sub, _subSlots);
        }

        private void ClearThird() => Clear(_third, _thirdSlots);

        private static void Clear(Canvas ring, List<Slot> slots)
        {
            foreach (Slot slot in slots)
            {
                ring.Children.Remove(slot.Background);
                ring.Children.Remove(slot.Layer);
            }
            slots.Clear();
        }

        // ═══ Hub ═══

        private void ShowSummary()
        {
            _hubIcon.Icon = _files == null ? "Icon.Files" : FileKinds.Icon(_files.Kind);
            _hubTitle.Text = _files?.Summary ?? string.Empty;
            _hubCaption.Text = string.Empty;
        }

        private void UpdateHub()
        {
            MenuNode node = CurrentNode;
            bool hasThumb = _thumbnail.Source != null;
            if (node != null)
            {
                _hubIcon.Visibility = Visibility.Collapsed;
                _hubTitle.Text = node.Title;
                _hubCaption.Text = node.IsGroup ? "mais opções" : _files == null || _files.Count == 1 ? string.Empty : _files.Count + " itens";
                FadeThumb(hasThumb ? 0.14 : 0);
            }
            else
            {
                bool centre = _armed && _top < 0;
                _hubIcon.Visibility = hasThumb ? Visibility.Collapsed : Visibility.Visible;
                _hubTitle.Text = hasThumb ? string.Empty : _files?.Summary ?? string.Empty;
                _hubCaption.Text = centre ? (_mode == WheelMode.Drag ? "solte aqui para cancelar" : "solte para cancelar") : string.Empty;
                FadeThumb(hasThumb ? (centre ? 0.3 : 1) : 0);
            }
        }

        private void FadeThumb(double to)
        {
            if (Math.Abs(_thumbnail.Opacity - to) > 0.01)
            {
                _thumbnail.BeginAnimation(OpacityProperty, new DoubleAnimation(to, Theme.Duration("Motion.Normal")));
            }
        }

        private MenuNode CurrentNode =>
            _thirdIndex >= 0 && _thirdIndex < _thirdSlots.Count ? _thirdSlots[_thirdIndex].Node
            : _subIndex >= 0 && _subIndex < _subSlots.Count ? _subSlots[_subIndex].Node
            : _top >= 0 && _top < _slots.Count ? _slots[_top].Node
            : null;

        // ═══ Commit ═══

        private bool TryCommit()
        {
            MenuNode node = CurrentNode;
            if (node == null)
            {
                return false;
            }
            if (node.IsGroup)
            {
                // Chose a group: open it and wait for a click.
                if (_subIndex >= 0)
                {
                    OpenSubGroup(_subIndex);
                }
                else
                {
                    OpenGroup(_top);
                }
                SetMode(WheelMode.Click);
                return true;
            }
            Slot chosen = _thirdIndex >= 0 ? _thirdSlots[_thirdIndex] : _subIndex >= 0 ? _subSlots[_subIndex] : _slots[_top];
            Diagnostics.Log?.Info("Chosen: " + node.Id);
            FileSet files = _files;
            Close(animated: true, chosen);
            Committed?.Invoke(node, files);
            return true;
        }

        /// <summary>Hold mode: the chord was released.</summary>
        public void OnChordReleased()
        {
            if (_mode != WheelMode.Hold)
            {
                return;
            }
            if (!TryCommit())
            {
                Dismiss();
            }
        }

        public void Navigate(NavigationKey key)
        {
            if (!WantsKeyboard || _slots.Count == 0)
            {
                return;
            }
            _armed = true;
            switch (key)
            {
                case NavigationKey.Escape:
                    if (_openSub >= 0 && _thirdIndex >= 0)
                    {
                        Select(_openGroup, _subIndex, -1);
                    }
                    else if (_openGroup >= 0 && _subIndex >= 0)
                    {
                        Select(_openGroup, -1);
                    }
                    else
                    {
                        Dismiss();
                    }
                    break;
                case NavigationKey.Enter:
                    if (_top >= 0 && _items[_top].IsGroup && _subIndex < 0)
                    {
                        OpenGroup(_top);
                        Select(_top, 0);
                        SetMode(WheelMode.Click);
                    }
                    else if (_subIndex >= 0 && _subSlots[_subIndex].Node.IsGroup && _thirdIndex < 0)
                    {
                        OpenSubGroup(_subIndex);
                        Select(_top, _subIndex, 0);
                        SetMode(WheelMode.Click);
                    }
                    else
                    {
                        TryCommit();
                    }
                    break;
                default:
                    int direction = key == NavigationKey.Left || key == NavigationKey.Up ? -1 : 1;
                    if (_openSub >= 0 && _thirdIndex >= 0)
                    {
                        Select(_openGroup, _openSub, (_thirdIndex + direction + _thirdSlots.Count) % _thirdSlots.Count);
                    }
                    else if (_openGroup >= 0 && _subIndex >= 0)
                    {
                        Select(_openGroup, (_subIndex + direction + _subSlots.Count) % _subSlots.Count);
                    }
                    else
                    {
                        int next = _top < 0 ? 0 : (_top + direction + _slots.Count) % _slots.Count;
                        Select(next, -1);
                    }
                    break;
            }
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (_mode != WheelMode.Click && _mode != WheelMode.Hold)
            {
                return;
            }
            _armed = true;
            OnPointer(e.GetPosition(this));
            if (!TryCommit() && (e.GetPosition(this) - _center).Length < _inner)
            {
                Dismiss();
            }
            e.Handled = true;
        }

        private void OnMouseLeave(object sender, MouseEventArgs e)
        {
            // Only a real exit counts: WPF also raises MouseLeave on re-hit-tests when the mouse never came in.
            if (_mode == WheelMode.Click && _hadMouse)
            {
                Dismiss();
            }
        }

        // ═══ Drag & drop ═══

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_mode == WheelMode.Armed)
            {
                _armTimeout.Stop();
                string[] paths = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
                FileSet files = FileSet.FromPaths(paths);
                IReadOnlyList<MenuNode> items = files.Count > 0 ? MenuProvider?.Invoke(files) : null;
                Diagnostics.Log?.Info($"Drag entered the wheel: {files.Count} item(s), {items?.Count ?? 0} slot(s)");
                if (items == null || items.Count == 0)
                {
                    e.Effects = DragDropEffects.None;
                    Dismiss(animated: false);
                    return;
                }
                _mode = WheelMode.Drag;
                Populate(files, items);
                AnimateIn();
            }
            UpdateEffects(e);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_mode == WheelMode.Drag)
            {
                OnPointer(e.GetPosition(this));
            }
            UpdateEffects(e);        }

        private void UpdateEffects(DragEventArgs e)
        {
            // Copy, never Move: the source must never delete what was dragged onto the wheel.
            DragDropEffects wanted = _mode == WheelMode.Drag && CurrentNode != null ? DragDropEffects.Copy : DragDropEffects.None;
            e.Effects = (e.AllowedEffects & wanted) != 0 ? wanted : (wanted != DragDropEffects.None && (e.AllowedEffects & DragDropEffects.Link) != 0 ? DragDropEffects.Link : DragDropEffects.None);
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_mode == WheelMode.Drag || _mode == WheelMode.Armed)
            {
                Dismiss();
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            if (_mode != WheelMode.Drag)
            {
                return;
            }
            Point at = e.GetPosition(this);
            OnPointer(at);
            e.Effects = DragDropEffects.Copy;
            if (!TryCommit())
            {
                Diagnostics.Log?.Info($"Dropped on nothing at {at.X:0},{at.Y:0} (centre {_center.X:0},{_center.Y:0}, armed={_armed})");
                Dismiss();
            }
        }

        private sealed class Slot
        {
            public MenuNode Node;
            public double Angle;
            public Path Background;
            public Canvas Layer;
            public ScaleTransform Scale;
            public SolidColorBrush Content;
            public SolidColorBrush Secondary;
            public bool Active;
            public bool OnPath;
        }
    }
}
