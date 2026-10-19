using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tamarillo.DevHost
{
    /// <summary>
    /// End-to-end check of the real input path, as with Explorer: another process starts a genuine OLE file
    /// drag, the chord goes through SendInput (so through the low-level hook), the cursor flicks to the
    /// "Comprimir" slot and drops. Passes when the compressed copy appears. Moves the real cursor for ~2 s.
    /// </summary>
    internal static class DragSelfTest
    {
        public static int Run(string dataDirectory)
        {
            string folder = Path.Combine(Path.GetTempPath(), "tamarillo-selftest");
            Directory.CreateDirectory(folder);
            foreach (string old in Directory.GetFiles(folder))
            {
                File.Delete(old);
            }
            string photo = Path.Combine(folder, "foto.jpg");
            Snapshots.WriteSamplePhoto(photo);
            string expected = Path.Combine(folder, "foto (comprimida).jpg");

            // Let the module warm its windows first, like a host that has been running for a while.
            Pump(TimeSpan.FromMilliseconds(1500));
            Process child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, $"--drag-source \"{photo}\"") { UseShellExecute = false });
            DateTime deadline = DateTime.Now.AddSeconds(20);
            while (!File.Exists(expected) && DateTime.Now < deadline)
            {
                Pump(TimeSpan.FromMilliseconds(100));
            }
            Pump(TimeSpan.FromMilliseconds(300));
            bool pass = File.Exists(expected) && new FileInfo(expected).Length > 0;
            try
            {
                if (!child.HasExited)
                {
                    child.Kill();
                }
            }
            catch
            {
            }
            ReleaseAll();
            File.AppendAllText(Path.Combine(dataDirectory, "devhost.log"), $"{DateTime.Now:HH:mm:ss.fff} SELFTEST {(pass ? "PASS" : "FAIL")} drag → wheel → Comprimir{Environment.NewLine}");
            return pass ? 0 : 2;
        }

        /// <summary>Child process: the drag source.</summary>
        public static int Source(string file)
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var window = new Window
            {
                Width = 160,
                Height = 120,
                WindowStyle = WindowStyle.ToolWindow,
                Topmost = true,
                Title = "drag source",
                Background = Brushes.DimGray,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Content = new System.Windows.Controls.TextBlock { Text = "foto.jpg", Foreground = Brushes.White, Margin = new Thickness(12) },
            };
            window.ContentRendered += (s, e) =>
            {
                Point center = window.PointToScreen(new Point(window.ActualWidth / 2, window.ActualHeight / 2));
                int x = (int)center.X, y = (int)center.Y;
                double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
                SetCursorPos(x, y);
                Mouse(0x0002); // left down
                for (int i = 0; i < 50 && (GetAsyncKeyState(0x01) & 0x8000) == 0; i++)
                {
                    Thread.Sleep(10);
                }
                window.Dispatcher.Invoke(DispatcherPriority.Input, new Action(() => { }));
                Trace("source: button down at " + x + "," + y);

                var driver = new Thread(() =>
                {
                    try
                    {
                        Thread.Sleep(300);
                        Key(0x11, false);
                        Key(0x12, false);
                        Key(0x10, false);
                        Thread.Sleep(400);
                        // Flick right: slot 2 of 8 is "Comprimir" for images.
                        for (int i = 1; i <= 10; i++)
                        {
                            SetCursorPos(x + (int)(i * 10 * scale), y);
                            Thread.Sleep(25);
                        }
                        Thread.Sleep(300);
                        Mouse(0x0004); // left up → drop
                        Trace("source: dropped");
                    }
                    finally
                    {
                        ReleaseAll();
                    }
                })
                { IsBackground = true };
                driver.Start();

                DragDropEffects effect = DragDrop.DoDragDrop(window, new DataObject(DataFormats.FileDrop, new[] { file }), DragDropEffects.Copy | DragDropEffects.Move);
                Trace("source: DoDragDrop returned " + effect);
                window.Close();
                app.Shutdown();
            };
            window.Show();
            app.Run();
            return 0;
        }

        private static void Trace(string message) =>
            File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tamarillo.DevHost", "devhost.log"), DateTime.Now.ToString("HH:mm:ss.fff ") + message + Environment.NewLine);

        private static void ReleaseAll()
        {
            Key(0x10, true);
            Key(0x12, true);
            Key(0x11, true);
        }

        private static void Pump(TimeSpan duration)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = duration };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        private static void Mouse(uint flags) => mouse_event(flags, 0, 0, 0, UIntPtr.Zero);

        private static void Key(byte vk, bool up) => keybd_event(vk, 0, up ? 0x0002u : 0u, UIntPtr.Zero);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vk);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);
    }
}
