using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Tamarillo.Core;
using Tamarillo.Operations;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Office documents through what the user already has: Microsoft Office (COM automation, invisible,
    /// read-only, never touching documents the user has open) or LibreOffice (headless, isolated profile so
    /// a running LibreOffice is not disturbed). Optional and detected.
    /// </summary>
    internal static class OfficeEngine
    {
        public static bool CanConvert(Capabilities caps, FileKind kind, string targetExt) =>
            (caps.OfficeFor(kind) && OfficeFormat(kind, targetExt) != null) || (caps.LibreOffice != null && LibreFilter(targetExt) != null);

        public static Task ConvertAsync(Capabilities caps, string dataDirectory, FileItem input, string output, string targetExt, CancellationToken token)
        {
            if (caps.OfficeFor(input.Kind) && OfficeFormat(input.Kind, targetExt) != null)
            {
                return StaWorker.Run(() => ConvertWithOffice(input, output, targetExt), "Tamarillo.Office");
            }
            if (caps.LibreOffice != null)
            {
                return ConvertWithLibreOfficeAsync(caps.LibreOffice, dataDirectory, input.Path, output, targetExt, token);
            }
            throw new OperationException("Instale o Microsoft Office ou o LibreOffice para converter este documento.");
        }

        /// <summary>Office SaveAs format code for a target, or null.</summary>
        private static int? OfficeFormat(FileKind kind, string ext)
        {
            switch (kind)
            {
                case FileKind.Document:
                case FileKind.Text:
                case FileKind.Pdf:
                    switch (ext)
                    {
                        case "pdf": return 17;   // wdExportFormatPDF (ExportAsFixedFormat)
                        case "docx": return 16;  // wdFormatXMLDocument
                        case "rtf": return 6;    // wdFormatRTF
                        case "txt": return 2;    // wdFormatText (+ UTF-8 encoding)
                        case "html": return 10;  // wdFormatFilteredHTML
                        case "odt": return 23;   // wdFormatOpenDocumentText
                    }
                    break;
                case FileKind.Spreadsheet:
                case FileKind.Data:
                    switch (ext)
                    {
                        case "pdf": return 0;    // xlTypePDF (ExportAsFixedFormat)
                        case "xlsx": return 51;  // xlOpenXMLWorkbook
                        case "csv": return 62;   // xlCSVUTF8
                        case "ods": return 60;   // xlOpenDocumentSpreadsheet
                    }
                    break;
                case FileKind.Presentation:
                    switch (ext)
                    {
                        case "pdf": return 32;   // ppSaveAsPDF
                        case "pptx": return 24;  // ppSaveAsOpenXMLPresentation
                        case "odp": return 35;   // ppSaveAsOpenDocumentPresentation
                    }
                    break;
            }
            return null;
        }

        private static string LibreFilter(string ext)
        {
            switch (ext)
            {
                case "pdf": return "pdf";
                case "docx": return "docx:\"MS Word 2007 XML\"";
                case "odt": return "odt";
                case "rtf": return "rtf";
                case "txt": return "txt:Text (encoded):UTF8";
                case "html": return "html";
                case "xlsx": return "xlsx:\"Calc MS Excel 2007 XML\"";
                case "ods": return "ods";
                case "csv": return "csv:\"Text - txt - csv (StarCalc)\":44,34,76";
                case "pptx": return "pptx:\"Impress MS PowerPoint 2007 XML\"";
                case "odp": return "odp";
                default: return null;
            }
        }

        private static void ConvertWithOffice(FileItem input, string output, string ext)
        {
            int format = OfficeFormat(input.Kind, ext).Value;
            dynamic app = null;
            dynamic document = null;
            bool quit = true;
            try
            {
                switch (input.Kind)
                {
                    case FileKind.Spreadsheet:
                    case FileKind.Data:
                        app = Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application", throwOnError: true));
                        quit = app.Workbooks.Count == 0;
                        app.DisplayAlerts = false;
                        app.ScreenUpdating = false;
                        document = app.Workbooks.Open(input.Path, ReadOnly: true, AddToMru: false);
                        if (ext == "pdf")
                        {
                            document.ExportAsFixedFormat(0, output);
                        }
                        else
                        {
                            document.SaveAs(output, format);
                        }
                        document.Close(false);
                        break;
                    case FileKind.Presentation:
                        app = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application", throwOnError: true));
                        // PowerPoint is single-instance: only quit it if nothing of the user's is open.
                        quit = app.Presentations.Count == 0;
                        document = app.Presentations.Open(input.Path, -1 /* ReadOnly */, 0 /* Untitled */, 0 /* WithWindow */);
                        document.SaveAs(output, format);
                        document.Close();
                        break;
                    default:
                        app = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", throwOnError: true));
                        quit = app.Documents.Count == 0;
                        app.Visible = false;
                        app.DisplayAlerts = 0;
                        document = app.Documents.Open(input.Path, ConfirmConversions: false, ReadOnly: true, AddToRecentFiles: false, Visible: false);
                        if (ext == "pdf")
                        {
                            document.ExportAsFixedFormat(output, 17);
                        }
                        else if (ext == "txt")
                        {
                            document.SaveAs2(output, format, Encoding: 65001);
                        }
                        else
                        {
                            document.SaveAs2(output, format);
                        }
                        document.Close(0);
                        break;
                }
            }
            catch (COMException ex)
            {
                throw new OperationException("O Office não conseguiu converter este arquivo.", ex);
            }
            finally
            {
                if (document != null)
                {
                    Marshal.FinalReleaseComObject(document);
                }
                if (app != null)
                {
                    try
                    {
                        if (quit)
                        {
                            app.Quit();
                        }
                    }
                    catch
                    {
                    }
                    Marshal.FinalReleaseComObject(app);
                }
            }
        }

        private static async Task ConvertWithLibreOfficeAsync(string soffice, string dataDirectory, string input, string output, string ext, CancellationToken token)
        {
            string outDir = Path.Combine(Path.GetDirectoryName(output), "~tamarillo-lo-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(outDir);
            try
            {
                string profile = new Uri(Path.Combine(dataDirectory, "libreoffice-profile")).AbsoluteUri;
                string args = $"-env:UserInstallation={profile} --headless --norestore --nologo --convert-to {LibreFilter(ext)} --outdir \"{outDir}\" \"{input}\"";
                var info = new ProcessStartInfo(soffice, args) { UseShellExecute = false, CreateNoWindow = true };
                using (Process process = Process.Start(info))
                {
                    try
                    {
                        process.PriorityClass = ProcessPriorityClass.BelowNormal;
                    }
                    catch
                    {
                    }
                    while (!process.WaitForExit(100))
                    {
                        if (token.IsCancellationRequested)
                        {
                            try
                            {
                                process.Kill();
                            }
                            catch
                            {
                            }
                            token.ThrowIfCancellationRequested();
                        }
                        await Task.Delay(150, token).ConfigureAwait(false);
                    }
                }
                string produced = Directory.EnumerateFiles(outDir).FirstOrDefault();
                if (produced == null)
                {
                    throw new OperationException("O LibreOffice não conseguiu converter este arquivo.");
                }
                File.Move(produced, output);
            }
            finally
            {
                OutputPaths.Discard(outDir);
            }
        }
    }
}
