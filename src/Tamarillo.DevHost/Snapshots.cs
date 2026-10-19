using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tamarillo.Core;
using Tamarillo.Jobs;
using Tamarillo.Native;
using Tamarillo.Operations;
using Tamarillo.Ui;

namespace Tamarillo.DevHost
{
    /// <summary>Renders every visual state to PNG (2x) so the design can be reviewed without clicking around.</summary>
    internal static class Snapshots
    {
        public static int Run(TamarilloModule module, string output)
        {
            Directory.CreateDirectory(output);
            TamarilloController controller = module.Controller;
            Pump(TimeSpan.FromMilliseconds(100));
            var detect = controller.RedetectAsync();
            while (!detect.IsCompleted)
            {
                Pump(TimeSpan.FromMilliseconds(50));
            }

            string samples = Path.Combine(Path.GetTempPath(), "tamarillo-snapshot-samples");
            Directory.CreateDirectory(samples);
            string photo = Path.Combine(samples, "praia.jpg");
            WriteSamplePhoto(photo);

            RadialWindow wheel = controller.WheelWindow;
            NativeMethods.GetCursorPos(out NativeMethods.POINT cursor);
            var screen = new NativeMethods.POINT { X = (int)(SystemParameters.PrimaryScreenWidth / 2), Y = (int)(SystemParameters.PrimaryScreenHeight / 2) };

            Brush desktop = new LinearGradientBrush(Color.FromRgb(0x3A, 0x4A, 0x5E), Color.FromRgb(0x8C, 0x9A, 0xA6), 45);
            desktop.Freeze();

            // Images: idle, hover, sub-ring
            FileSet images = FileSet.FromPaths(new[] { photo });
            wheel.ShowClick(images, MenuBuilder.Build(images, controller.Capabilities, controller), screen);
            Pump(TimeSpan.FromMilliseconds(900));
            Render(wheel.Stage, Path.Combine(output, "wheel-1-open.png"), desktop);

            wheel.SimulatePointer(wheel.Center + Polar(98, -90 + 2 * 45));
            Pump(TimeSpan.FromMilliseconds(500));
            Render(wheel.Stage, Path.Combine(output, "wheel-2-hover.png"), desktop);

            wheel.SimulatePointer(wheel.Center + Polar(98, -90));
            wheel.SimulateOpenGroup(0);
            Pump(TimeSpan.FromMilliseconds(200));
            wheel.SimulatePointer(wheel.Center + Polar(172, -90 + 30));
            Pump(TimeSpan.FromMilliseconds(600));
            Render(wheel.Stage, Path.Combine(output, "wheel-3-subring.png"), desktop);
            wheel.Dismiss(animated: false);

            Snapshot(wheel, controller, FileSet.Virtual(@"C:\v\clip.mp4", @"C:\v\b.mov"), screen, desktop, Path.Combine(output, "wheel-4-video.png"), 1);
            Snapshot(wheel, controller, FileSet.Virtual(@"C:\docs\", @"C:\x\notes.txt"), screen, desktop, Path.Combine(output, "wheel-5-mixed.png"), 3);
            Snapshot(wheel, controller, FileSet.Virtual(@"C:\d\relatorio.pdf"), screen, desktop, Path.Combine(output, "wheel-6-pdf.png"), -1);

            // Overlay: running, done, failed
            OverlayWindow overlay = controller.OverlayWindow;
            MenuNode convert = MenuBuilder.Build(images, controller.Capabilities, controller)[0].Children[1];
            var running = new Job(convert, FileSet.Virtual(@"C:\f\a.heic", @"C:\f\b.heic", @"C:\f\c.heic"));
            running.Start();
            running.Report(0.42, "b.heic");
            var done = new Job(convert, images);
            done.Start();
            done.Complete(new OperationResult(new[] { Path.Combine(samples, "praia.png") }));
            var failed = new Job(convert, images);
            failed.Start();
            failed.Fail("O Windows não consegue abrir esta imagem. Instale a extensão do formato na Microsoft Store.");
            overlay.Add(running, null);
            overlay.Add(done, null);
            overlay.Add(failed, null);
            Pump(TimeSpan.FromMilliseconds(900));
            Render((FrameworkElement)overlay.Content, Path.Combine(output, "overlay.png"), desktop);
            running.Cancel();
            done.Cancel();
            foreach (JobCard card in ((System.Windows.Controls.Panel)overlay.Content).Children.OfType<JobCard>().ToList())
            {
                card.Leave();
            }

            // Pages
            var window = new DevWindow(module, new DevContext(Path.GetTempPath(), Dispatcher.CurrentDispatcher));
            window.Show();
            Pump(TimeSpan.FromMilliseconds(1200));
            Render((FrameworkElement)window.Content, Path.Combine(output, "pages.png"), (Brush)Application.Current.FindResource("Brush.Canvas"));
            window.Close();

            Console.WriteLine("Snapshots written to " + output);
            return 0;
        }

        private static void Snapshot(RadialWindow wheel, TamarilloController controller, FileSet files, NativeMethods.POINT at, Brush background, string path, int hover)
        {
            IReadOnlyList<MenuNode> items = MenuBuilder.Build(files, controller.Capabilities, controller);
            wheel.ShowClick(files, items, at);
            Pump(TimeSpan.FromMilliseconds(700));
            if (hover >= 0 && hover < items.Count)
            {
                wheel.SimulatePointer(wheel.Center + Polar(98, -90 + hover * 360.0 / items.Count));
                Pump(TimeSpan.FromMilliseconds(400));
            }
            Render(wheel.Stage, path, background);
            wheel.Dismiss(animated: false);
        }

        private static Vector Polar(double radius, double degrees)
        {
            double a = degrees * Math.PI / 180;
            return new Vector(radius * Math.Cos(a), radius * Math.Sin(a));
        }

        private static void Render(FrameworkElement element, string path, Brush background)
        {
            const double scale = 2;
            double width = Math.Max(1, element.ActualWidth), height = Math.Max(1, element.ActualHeight);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.Fill, Viewbox = new Rect(0, 0, width, height), ViewboxUnits = BrushMappingMode.Absolute }, null, new Rect(0, 0, width, height));
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }

        internal static void WriteSamplePhoto(string path)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0xF6, 0xB2, 0x6B), Color.FromRgb(0x5B, 0x8F, 0xC9), 90), null, new Rect(0, 0, 640, 480));
                dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x9A)), null, new Point(420, 200), 70, 70);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2E, 0x5E, 0x8C)), null, new Rect(0, 330, 640, 150));
            }
            var bitmap = new RenderTargetBitmap(640, 480, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path))
            {
                encoder.Save(stream);
            }
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
    }
}
