using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Tamarillo.Jobs;
using Tamarillo.Native;

namespace Tamarillo.Ui
{
    /// <summary>
    /// Floating stack of job cards. Never takes focus, never in Alt+Tab, stays on top, can be dragged by any
    /// card; it remembers where it was dropped. Cards stack upward from a bottom-right anchor, so a new card
    /// never moves the ones the user is looking at.
    /// </summary>
    public sealed class OverlayWindow : Window
    {
        private readonly StackPanel _stack = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        private readonly IntPtr _hwnd;
        private NativeMethods.POINT _anchor; // bottom-right corner, physical pixels
        private bool _hasAnchor;

        public OverlayWindow()
        {
            Resources.MergedDictionaries.Add(Theme.Resources);
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            UseLayoutRounding = true;
            Title = "Tamarillo";
            Content = _stack;
            _hwnd = new WindowInteropHelper(this).EnsureHandle();
            NativeMethods.MakeToolWindow(_hwnd);
            SizeChanged += (s, e) => Reposition();
            PreviewMouseLeftButtonDown += OnPreviewMouseDown;
        }

        /// <summary>The user dragged the overlay somewhere: bottom-right corner in physical pixels.</summary>
        public event Action<int, int> AnchorMoved;

        public bool HasCards => _stack.Children.Count > 0;

        /// <summary>Remembered position from settings (bottom-right, px), or none for the default corner.</summary>
        public void SetAnchor(int right, int bottom)
        {
            _anchor = new NativeMethods.POINT { X = right, Y = bottom };
            _hasAnchor = true;
        }

        public void Add(Job job, TimeSpan? autoDismiss)
        {
            var card = new JobCard(job, autoDismiss);
            card.Left += c =>
            {
                _stack.Children.Remove(c);
                if (_stack.Children.Count == 0)
                {
                    Hide();
                }
            };
            _stack.Children.Add(card);
            if (!IsVisible)
            {
                EnsureAnchorOnScreen();
                Show();
            }
            card.Enter();
        }

        private void EnsureAnchorOnScreen()
        {
            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            NativeMethods.POINT probe = _hasAnchor ? _anchor : cursor;
            NativeMethods.GetMonitorAt(probe, out NativeMethods.RECT work, out double scale);
            int margin = (int)Math.Round(12 * scale);
            if (!_hasAnchor || _anchor.X < work.Left + 80 || _anchor.X > work.Right || _anchor.Y < work.Top + 40 || _anchor.Y > work.Bottom)
            {
                NativeMethods.GetMonitorAt(cursor, out work, out scale);
                _anchor = new NativeMethods.POINT { X = work.Right - margin, Y = work.Bottom - margin };
            }
        }

        private void Reposition()
        {
            if (!IsVisible)
            {
                return;
            }
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix toDevice = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
            int width = (int)Math.Round(ActualWidth * toDevice.M11);
            int height = (int)Math.Round(ActualHeight * toDevice.M22);
            NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST, _anchor.X - width, _anchor.Y - height, 0, 0,
                NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            Reposition();
        }

        private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Drag from anywhere that is not a button.
            for (DependencyObject node = e.OriginalSource as DependencyObject; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is ButtonBase)
                {
                    return;
                }
                if (node == this)
                {
                    break;
                }
            }
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                return;
            }
            if (GetWindowRect(_hwnd, out NativeMethods.RECT rect))
            {
                _anchor = new NativeMethods.POINT { X = rect.Right, Y = rect.Bottom };
                _hasAnchor = true;
                AnchorMoved?.Invoke(rect.Right, rect.Bottom);
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeMethods.RECT rect);
    }
}
