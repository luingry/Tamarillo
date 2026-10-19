using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Tamarillo.Core;
using Tamarillo.Engines;
using Tamarillo.Native;

namespace Tamarillo.Input
{
    /// <summary>
    /// Reads the selection of the Explorer window (or desktop) in front, through the Shell's own automation
    /// (Shell.Application). Runs on a short-lived STA thread; returns an empty list when the foreground is not
    /// Explorer or nothing is selected.
    /// </summary>
    internal static class ExplorerSelection
    {
        private static readonly Guid SidTopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
        private static readonly Guid IidShellBrowser = new Guid("000214E2-0000-0000-C000-000000000046");

        public static bool IsExplorer(IntPtr foreground)
        {
            string cls = NativeMethods.ClassNameOf(foreground);
            return cls == "CabinetWClass" || cls == "ExploreWClass" || cls == "Progman" || cls == "WorkerW";
        }

        public static Task<IReadOnlyList<string>> GetAsync(IntPtr foreground) =>
            StaWorker.Run<IReadOnlyList<string>>(() =>
            {
                try
                {
                    return Read(foreground);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log?.Warn("Explorer selection: " + ex.Message);
                    return Array.Empty<string>();
                }
            }, "Tamarillo.Selection");

        private static IReadOnlyList<string> Read(IntPtr foreground)
        {
            string cls = NativeMethods.ClassNameOf(foreground);
            Type type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null)
            {
                return Array.Empty<string>();
            }
            dynamic shell = Activator.CreateInstance(type);
            try
            {
                dynamic windows = shell.Windows();
                if (cls == "Progman" || cls == "WorkerW")
                {
                    object location = 0; // CSIDL_DESKTOP
                    object root = null;
                    int hwnd = 0;
                    dynamic desktop = windows.FindWindowSW(ref location, ref root, 8 /* SWC_DESKTOP */, ref hwnd, 1 /* SWFO_NEEDDISPATCH */);
                    return desktop == null ? Array.Empty<string>() : SelectedPaths(desktop.Document);
                }

                // Windows 11 Explorer has tabs that share one top-level window; the active tab's
                // ShellTabWindowClass child is listed first.
                IntPtr activeTab = NativeMethods.FindWindowEx(foreground, IntPtr.Zero, "ShellTabWindowClass", null);
                dynamic match = null;
                int count = windows.Count;
                for (int i = 0; i < count; i++)
                {
                    dynamic window = windows.Item(i);
                    if (window == null)
                    {
                        continue;
                    }
                    long handle;
                    try
                    {
                        handle = (long)window.HWND;
                    }
                    catch
                    {
                        continue;
                    }
                    if (new IntPtr(handle) != foreground)
                    {
                        continue;
                    }
                    if (activeTab == IntPtr.Zero || TabWindow(window) == activeTab)
                    {
                        match = window;
                        break;
                    }
                    match = match ?? window;
                }
                return match == null ? Array.Empty<string>() : SelectedPaths(match.Document);
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        private static IReadOnlyList<string> SelectedPaths(dynamic document)
        {
            var paths = new List<string>();
            if (document == null)
            {
                return paths;
            }
            dynamic items = document.SelectedItems();
            int count = items.Count;
            for (int i = 0; i < count && i < 500; i++)
            {
                string path = items.Item(i).Path;
                if (!string.IsNullOrEmpty(path) && (System.IO.File.Exists(path) || System.IO.Directory.Exists(path)))
                {
                    paths.Add(path);
                }
            }
            return paths;
        }

        private static IntPtr TabWindow(object window)
        {
            try
            {
                if (window is IServiceProvider provider)
                {
                    Guid sid = SidTopLevelBrowser, iid = IidShellBrowser;
                    provider.QueryService(ref sid, ref iid, out object browserObject);
                    if (browserObject is IShellBrowser browser)
                    {
                        browser.GetWindow(out IntPtr hwnd);
                        return hwnd;
                    }
                }
            }
            catch
            {
            }
            return IntPtr.Zero;
        }

        [ComImport]
        [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            void QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
        }

        /// <summary>Only the first method (IOleWindow::GetWindow) is ever called.</summary>
        [ComImport]
        [Guid("000214E2-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow(out IntPtr phwnd);
        }
    }

    /// <summary>Shell thumbnails (the same Explorer shows), for the wheel's hub.</summary>
    internal static class ShellThumbnail
    {
        public static Task<System.Windows.Media.Imaging.BitmapSource> GetAsync(string path, int size) =>
            StaWorker.Run(() =>
            {
                IntPtr bitmap = IntPtr.Zero;
                try
                {
                    Guid iid = typeof(IShellItemImageFactory).GUID;
                    if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItemImageFactory factory) != 0 || factory == null)
                    {
                        return null;
                    }
                    try
                    {
                        // BIGGERSIZEOK | THUMBNAILONLY: real previews only; plain file icons are not worth showing.
                        if (factory.GetImage(new SIZE { cx = size, cy = size }, 0x1 | 0x8, out bitmap) != 0 || bitmap == IntPtr.Zero)
                        {
                            return null;
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(factory);
                    }
                    var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    return source;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    if (bitmap != IntPtr.Zero)
                    {
                        DeleteObject(bitmap);
                    }
                }
            }, "Tamarillo.Thumbnail");

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItemImageFactory factory);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);
    }
}
