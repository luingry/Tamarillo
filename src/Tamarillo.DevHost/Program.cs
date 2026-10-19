using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using SwissTechKnife.Sdk;

namespace Tamarillo.DevHost
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            int source = Array.IndexOf(args, "--drag-source");
            if (source >= 0 && source + 1 < args.Length)
            {
                return DragSelfTest.Source(args[source + 1]);
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // Exactly what the host does: its theme in the application resources, inherited by module pages.
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/SwissTechKnife.Sdk;component/Theme/Theme.xaml") });

            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tamarillo.DevHost");
            var context = new DevContext(data, Dispatcher.CurrentDispatcher);
            var module = new TamarilloModule();
            module.Initialize(context);
            module.Start();

            if (args.Contains("--selftest-drag"))
            {
                int code = DragSelfTest.Run(data);
                module.Stop();
                module.Dispose();
                return code;
            }

            int snapshot = Array.FindIndex(args, a => a == "--snapshot");
            if (snapshot >= 0)
            {
                string output = snapshot + 1 < args.Length ? args[snapshot + 1] : Path.Combine(Environment.CurrentDirectory, "snapshots");
                int code = Snapshots.Run(module, output);
                module.Stop();
                module.Dispose();
                return code;
            }

            var window = new DevWindow(module, context);
            window.Closed += (s, e) => app.Shutdown();
            window.Show();
            app.Run();
            module.Stop();
            module.Dispose();
            return 0;
        }
    }

    /// <summary>Minimal stand-in for the host's IModuleContext.</summary>
    internal sealed class DevContext : IModuleContext, IModuleLog
    {
        private readonly string _logPath;

        public DevContext(string dataDirectory, Dispatcher dispatcher)
        {
            DataDirectory = dataDirectory;
            Directory.CreateDirectory(dataDirectory);
            Dispatcher = dispatcher;
            _logPath = Path.Combine(dataDirectory, "devhost.log");
        }

        public event Action<string, NotificationKind> Notified;

        public string ModuleId => "tamarillo";

        public Version ModuleVersion => typeof(TamarilloModule).Assembly.GetName().Version;

        public Version HostVersion => new Version(0, 0, 0);

        public string InstallDirectory => AppDomain.CurrentDomain.BaseDirectory;

        public string DataDirectory { get; }

        public Dispatcher Dispatcher { get; }

        public IModuleLog Log => this;

        public void Notify(string message, NotificationKind kind = NotificationKind.Info) => Notified?.Invoke(message, kind);

        public void ShowModulePage()
        {
        }

        public void Info(string message) => Write("INFO", message);

        public void Warn(string message) => Write("WARN", message);

        public void Error(string message, Exception exception = null) => Write("ERROR", message + (exception == null ? string.Empty : Environment.NewLine + exception));

        private void Write(string level, string message)
        {
            string line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}";
            System.Diagnostics.Debug.WriteLine(line);
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }
        }
    }
}
