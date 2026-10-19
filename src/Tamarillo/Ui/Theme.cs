using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Tamarillo.Ui
{
    /// <summary>
    /// The Tamarillo resource layer (Theme/Tamarillo.xaml), parsed once and shared by every window the module
    /// owns. Code reads tokens through here so no value is ever hard-coded in a view.
    /// </summary>
    public static class Theme
    {
        private static ResourceDictionary _resources;

        public static ResourceDictionary Resources =>
            _resources ?? (_resources = new ResourceDictionary { Source = new Uri("/Tamarillo.Module;component/Theme/Tamarillo.xaml", UriKind.Relative) });

        public static T Get<T>(string key) => (T)Resources[key];

        public static double Number(string key) => Get<double>(key);

        public static Brush Brush(string key) => Get<Brush>(key);

        public static Color Color(string key) => Get<Color>(key);

        /// <summary>Token duration, or zero when Windows animations are turned off (accessibility).</summary>
        public static Duration Duration(string key) =>
            SystemParameters.ClientAreaAnimation ? Get<Duration>(key) : new Duration(TimeSpan.Zero);

        public static TimeSpan Milliseconds(string key) =>
            SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(Number(key)) : TimeSpan.Zero;

        public static IEasingFunction Ease(string key) => Get<IEasingFunction>(key);

        public static bool Animate => SystemParameters.ClientAreaAnimation;
    }
}
