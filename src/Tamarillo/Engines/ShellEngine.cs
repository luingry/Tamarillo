using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Tamarillo.Core;
using Tamarillo.Operations;
using Clipboard = System.Windows.Clipboard;

namespace Tamarillo.Engines
{
    /// <summary>File actions that work for anything, folders included: clipboard, Explorer, terminal, share.</summary>
    internal static class ShellEngine
    {
        public static string CopyPaths(FileSet files)
        {
            SetClipboardText(string.Join(Environment.NewLine, files.Items.Select(i => i.Path)));
            return files.Count == 1 ? "Caminho copiado" : files.Count + " caminhos copiados";
        }

        public static string CopyNames(FileSet files)
        {
            SetClipboardText(string.Join(Environment.NewLine, files.Items.Select(i => i.Name)));
            return files.Count == 1 ? "Nome copiado" : files.Count + " nomes copiados";
        }

        public static string CopyFiles(FileSet files)
        {
            var list = new StringCollection();
            list.AddRange(files.Items.Select(i => i.Path).ToArray());
            Retry(() => Clipboard.SetFileDropList(list));
            return files.Count == 1 ? "Arquivo pronto para colar" : files.Count + " itens prontos para colar";
        }

        public static void SetClipboardText(string text) => Retry(() => Clipboard.SetText(text ?? string.Empty));

        /// <summary>The clipboard is a shared resource another app may hold for a moment: retry briefly.</summary>
        public static void Retry(Action action)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    action();
                    return;
                }
                catch (COMException) when (attempt < 5)
                {
                    System.Threading.Thread.Sleep(40);
                }
                catch (COMException ex)
                {
                    throw new OperationException("A área de transferência está ocupada por outro aplicativo.", ex);
                }
            }
        }

        /// <summary>Opens Explorer with the items selected (grouped per folder).</summary>
        public static string Reveal(IEnumerable<string> paths)
        {
            foreach (IGrouping<string, string> group in paths.GroupBy(p => Path.GetDirectoryName(p.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase))
            {
                IntPtr folder = ILCreateFromPath(group.Key);
                var items = group.Select(ILCreateFromPath).Where(p => p != IntPtr.Zero).ToArray();
                try
                {
                    if (folder == IntPtr.Zero || SHOpenFolderAndSelectItems(folder, (uint)items.Length, items, 0) != 0)
                    {
                        Process.Start("explorer.exe", "/select,\"" + group.First() + "\"");
                    }
                }
                finally
                {
                    foreach (IntPtr item in items)
                    {
                        ILFree(item);
                    }
                    if (folder != IntPtr.Zero)
                    {
                        ILFree(folder);
                    }
                }
            }
            return null;
        }

        public static void Open(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Diagnostics.Log?.Warn("Open failed: " + ex.Message);
            }
        }

        /// <summary>Windows Terminal in the item's folder, falling back to PowerShell.</summary>
        public static string OpenTerminal(FileSet files)
        {
            FileItem first = files.Items[0];
            string folder = first.IsDirectory ? first.Path : first.Folder;
            try
            {
                Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folder.TrimEnd('\\')}\\.\"") { UseShellExecute = true });
            }
            catch (Exception)
            {
                Process.Start(new ProcessStartInfo("powershell.exe") { UseShellExecute = true, WorkingDirectory = folder });
            }
            return null;
        }

        /// <summary>The Windows share sheet for the files, anchored to <paramref name="owner"/>.</summary>
        public static string Share(FileSet files, IntPtr owner)
        {
            var interop = (IDataTransferManagerInterop)System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeMarshal.GetActivationFactory(typeof(DataTransferManager));
            Guid iid = new Guid("a5caee9b-8708-49d1-8d36-67d25a8da00c");
            var manager = (DataTransferManager)Marshal.GetObjectForIUnknown(interop.GetForWindow(owner, ref iid));
            string[] paths = files.Items.Select(i => i.Path).ToArray();
            void OnRequested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                sender.DataRequested -= OnRequested;
                DataRequest request = args.Request;
                request.Data.Properties.Title = files.Count == 1 ? files.Items[0].Name : files.Summary;
                DataRequestDeferral deferral = request.GetDeferral();
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var items = new List<IStorageItem>();
                        foreach (string path in paths)
                        {
                            items.Add(Directory.Exists(path)
                                ? (IStorageItem)await StorageFolder.GetFolderFromPathAsync(path)
                                : await StorageFile.GetFileFromPathAsync(path));
                        }
                        request.Data.SetStorageItems(items);
                    }
                    catch (Exception ex)
                    {
                        request.FailWithDisplayText("Não foi possível compartilhar estes arquivos.");
                        Diagnostics.Log?.Warn("Share failed: " + ex.Message);
                    }
                    finally
                    {
                        deferral.Complete();
                    }
                });
            }
            manager.DataRequested += OnRequested;
            interop.ShowShareUIForWindow(owner);
            return null;
        }

        [ComImport]
        [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDataTransferManagerInterop
        {
            IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);

            void ShowShareUIForWindow(IntPtr appWindow);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ILCreateFromPath(string path);

        [DllImport("shell32.dll")]
        private static extern void ILFree(IntPtr pidl);

        [DllImport("shell32.dll")]
        private static extern int SHOpenFolderAndSelectItems(IntPtr folder, uint count, IntPtr[] items, uint flags);
    }
}
