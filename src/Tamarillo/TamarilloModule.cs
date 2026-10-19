using System.Windows;
using SwissTechKnife.Sdk;
using Tamarillo.Core;
using Tamarillo.Views;

namespace Tamarillo
{
    /// <summary>
    /// SwissTechKnife entry point. Starts with the host (<c>"activation": "startup"</c>) because the wheel must
    /// work with the host window closed; the page and the settings tab are created only when opened.
    /// </summary>
    public sealed class TamarilloModule : IBackgroundModule, IModuleSettings
    {
        private IModuleContext _context;
        private SettingsStore _settings;
        private TamarilloController _controller;

        public void Initialize(IModuleContext context)
        {
            _context = context;
            Diagnostics.Log = context.Log;
            _settings = new SettingsStore(context.DataDirectory);
            _settings.Load(); // < 1 KB; everything heavier is deferred to Start's idle/background work
            _controller = new TamarilloController(context, _settings);
        }

        internal TamarilloController Controller => _controller;

        public void Start() => _controller.Start();

        public void Stop() => _controller.Stop();

        public FrameworkElement CreateView() => new HomeView(_controller);

        public FrameworkElement CreateSettingsView() => new SettingsView(_controller);

        public void Dispose()
        {
            _controller?.Dispose();
            _controller = null;
            _settings?.Flush();
        }
    }
}
