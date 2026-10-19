using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SwissTechKnife.Sdk;
using Tamarillo.Core;
using Tamarillo.Engines;
using Tamarillo.Input;
using Tamarillo.Jobs;
using Tamarillo.Native;
using Tamarillo.Operations;
using Tamarillo.Ui;

namespace Tamarillo
{
    /// <summary>
    /// Wires the pieces: keyboard hook → wheel → job runner → overlay. Owns the module's windows.
    /// UI-thread only, except the hook callbacks, which only marshal.
    /// </summary>
    /// <remarks>
    /// Idle footprint: one sleeping hook thread and two hidden windows (created at idle priority after start so
    /// the first wheel opens instantly). No timers, no polling, no file watchers.
    /// </remarks>
    public sealed class TamarilloController : IWheelHost, IDisposable
    {
        private const int HoldDelayMs = 140;

        private readonly IModuleContext _context;
        private readonly SettingsStore _settings;
        private readonly Dispatcher _dispatcher;
        private readonly KeyboardHook _hook = new KeyboardHook();
        private readonly JobRunner _jobs;
        private RadialWindow _wheel;
        private OverlayWindow _overlay;
        private volatile Capabilities _capabilities = Capabilities.Baseline;
        private bool _started;
        private bool _chordHeld;
        private int _chordGeneration;

        public TamarilloController(IModuleContext context, SettingsStore settings)
        {
            _context = context;
            _settings = settings;
            _dispatcher = context.Dispatcher;
            _jobs = new JobRunner(_dispatcher, () => _settings.Current, () => _capabilities);
            _jobs.JobStarted += job => Overlay.Add(job, _settings.Current.AutoDismiss ? TimeSpan.FromSeconds(6) : (TimeSpan?)null);
            _jobs.InstantFinished += job => Overlay.Add(job, TimeSpan.FromSeconds(job.State == JobState.Failed ? 5 : 1.8));
            _hook.ChordDown += () => _dispatcher.BeginInvoke(new Action(OnChordDown), DispatcherPriority.Input);
            _hook.ChordUp += () => _dispatcher.BeginInvoke(new Action(OnChordUp), DispatcherPriority.Input);
            _hook.Navigation += key => _dispatcher.BeginInvoke(new Action(() => _wheel?.Navigate(key)), DispatcherPriority.Input);
            _settings.Changed += OnSettingsChanged;
        }

        /// <summary>Hook state or capabilities changed (pages refresh their status).</summary>
        public event Action StatusChanged;

        public Capabilities Capabilities => _capabilities;

        public bool IsDetecting { get; private set; }

        public bool HookActive => _hook.IsRunning;

        public SettingsStore Settings => _settings;

        public string DataDirectory => _context.DataDirectory;

        public IntPtr ShareOwner => Overlay.IsVisible ? new System.Windows.Interop.WindowInteropHelper(Overlay).Handle : Wheel.Handle;

        internal RadialWindow WheelWindow => Wheel;

        internal OverlayWindow OverlayWindow => Overlay;

        private RadialWindow Wheel
        {
            get
            {
                if (_wheel == null)
                {
                    _wheel = new RadialWindow
                    {
                        MenuProvider = files => MenuBuilder.Build(files, _capabilities, this),
                        ThumbnailProvider = path => ShellThumbnail.GetAsync(path, 192),
                    };
                    _wheel.Committed += (node, files) => _jobs.Run(node, files);
                    _wheel.ModeChanged += () => _hook.CaptureNavigation = _wheel.WantsKeyboard;
                }
                return _wheel;
            }
        }

        private OverlayWindow Overlay
        {
            get
            {
                if (_overlay == null)
                {
                    _overlay = new OverlayWindow();
                    TamarilloSettings s = _settings.Current;
                    if (s.OverlayPlaced)
                    {
                        _overlay.SetAnchor((int)s.OverlayLeft, (int)s.OverlayTop);
                    }
                    _overlay.AnchorMoved += (right, bottom) => _settings.Update(x =>
                    {
                        x.OverlayLeft = right;
                        x.OverlayTop = bottom;
                        x.OverlayPlaced = true;
                    });
                }
                return _overlay;
            }
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }
            _started = true;
            _hook.Shortcut = _settings.Current.Shortcut;
            if (!_hook.Start())
            {
                _context.Log.Error("Keyboard hook could not be installed");
                _context.Notify("O Tamarillo não conseguiu registrar o atalho de teclado.", NotificationKind.Error);
            }
            // Warm the windows when the app is idle, so the first wheel is instant.
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_started)
                {
                    _ = Wheel;
                    _ = Overlay;
                }
            }), DispatcherPriority.ApplicationIdle);
            _ = DetectAsync(delay: TimeSpan.FromSeconds(2));
            StatusChanged?.Invoke();
        }

        public void Stop()
        {
            if (!_started)
            {
                return;
            }
            _started = false;
            _hook.Stop();
            _wheel?.Dismiss(animated: false);
            StatusChanged?.Invoke();
        }

        public void Dispose()
        {
            Stop();
            _settings.Changed -= OnSettingsChanged;
            _hook.Dispose();
            _wheel?.Close();
            _overlay?.Close();
            _wheel = null;
            _overlay = null;
        }

        /// <summary>Re-runs engine detection (settings page button, ffmpeg path change).</summary>
        public Task RedetectAsync() => DetectAsync(TimeSpan.Zero);

        private async Task DetectAsync(TimeSpan delay)
        {
            IsDetecting = true;
            StatusChanged?.Invoke();
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay).ConfigureAwait(true);
                }
                TamarilloSettings settings = _settings.Current;
                Capabilities detected = await Task.Run(() =>
                {
                    System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
                    try
                    {
                        return Capabilities.Detect(settings);
                    }
                    finally
                    {
                        System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Normal;
                    }
                }).ConfigureAwait(true);
                _capabilities = detected;
                _context.Log.Info("Capabilities: " + detected);
            }
            catch (Exception ex)
            {
                _context.Log.Error("Capability detection failed", ex);
            }
            finally
            {
                IsDetecting = false;
                StatusChanged?.Invoke();
            }
        }

        private void OnSettingsChanged(TamarilloSettings settings)
        {
            if (!_hook.Shortcut.SameAs(settings.Shortcut))
            {
                _hook.Shortcut = settings.Shortcut;
            }
            if (!string.Equals(settings.FfmpegPath ?? string.Empty, _lastFfmpeg ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            {
                _lastFfmpeg = settings.FfmpegPath;
                _ = RedetectAsync();
            }
        }

        private string _lastFfmpeg;

        // ═══ Chord ═══

        private void OnChordDown()
        {
            _chordHeld = true;
            int generation = ++_chordGeneration;
            if (!_started || !_settings.Current.Enabled)
            {
                return;
            }
            if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            {
                return;
            }
            bool dragging = NativeMethods.IsMouseButtonDown();
            _context.Log.Info(dragging ? "Chord during a drag: arming the wheel" : "Chord without a drag");
            if (dragging)
            {
                Wheel.Arm(cursor);
                return;
            }
            if (!_settings.Current.SelectionTrigger || Wheel.Mode != WheelMode.Hidden)
            {
                return;
            }
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (!ExplorerSelection.IsExplorer(foreground))
            {
                return;
            }
            _ = OpenForSelectionAsync(foreground, cursor, generation);
        }

        private async Task OpenForSelectionAsync(IntPtr foreground, NativeMethods.POINT cursor, int generation)
        {
            Task<IReadOnlyList<string>> selection = ExplorerSelection.GetAsync(foreground);
            // A short hold filters out chords used for something else.
            await Task.Delay(HoldDelayMs).ConfigureAwait(true);
            IReadOnlyList<string> paths = await selection.ConfigureAwait(true);
            if (!_chordHeld || generation != _chordGeneration || paths.Count == 0 || Wheel.Mode != WheelMode.Hidden)
            {
                return;
            }
            FileSet files = FileSet.FromPaths(paths);
            IReadOnlyList<MenuNode> items = MenuBuilder.Build(files, _capabilities, this);
            if (items.Count > 0)
            {
                NativeMethods.GetCursorPos(out cursor);
                Wheel.ShowHold(files, items, cursor);
            }
        }

        private void OnChordUp()
        {
            _chordHeld = false;
            _wheel?.OnChordReleased();
        }

        /// <summary>Opens the wheel for files dropped on the module page's try-it area (click to choose).</summary>
        public void ShowForFiles(IEnumerable<string> paths)
        {
            FileSet files = FileSet.FromPaths(paths);
            IReadOnlyList<MenuNode> items = MenuBuilder.Build(files, _capabilities, this);
            if (items.Count > 0 && NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
            {
                Wheel.ShowClick(files, items, cursor);
            }
        }

        // ═══ IWheelHost ═══

        public void FloatImage(string path)
        {
            StaWorker.Run(() => ImageEngine.Load(path, maxEdge: 2560).Bitmap).ContinueWith(task =>
            {
                _dispatcher.BeginInvoke(new Action(() =>
                {
                    if (task.Status == TaskStatus.RanToCompletion)
                    {
                        new FloatWindow(task.Result, System.IO.Path.GetFileName(path)).Show();
                    }
                    else
                    {
                        _context.Notify("Não foi possível abrir esta imagem.", NotificationKind.Error);
                    }
                }));
            }, TaskScheduler.Default);
        }
    }
}
