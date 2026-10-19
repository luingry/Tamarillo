using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;
using Tamarillo.Operations;

namespace Tamarillo.Engines
{
    /// <summary>
    /// PDF pages and text recognition with Windows' own components: Windows.Data.Pdf renders pages (the same
    /// renderer Edge uses), Windows.Media.Ocr reads text in the user's languages. Both run fully offline.
    /// </summary>
    internal static class WindowsPdfEngine
    {
        public static async Task<PdfDocument> OpenAsync(string path, CancellationToken token)
        {
            try
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(path).AsTask(token).ConfigureAwait(false);
                return await PdfDocument.LoadFromFileAsync(file).AsTask(token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new OperationException(ex.HResult == unchecked((int)0x8007052B)
                    ? "Este PDF é protegido por senha."
                    : "Não foi possível abrir este PDF.", ex);
            }
        }

        /// <summary>Renders one page to a PNG or JPEG file at <paramref name="dpi"/>, on white.</summary>
        public static async Task RenderPageAsync(PdfDocument document, uint index, string output, string ext, double dpi, CancellationToken token)
        {
            using (PdfPage page = document.GetPage(index))
            {
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Max(1, Math.Round(page.Size.Width * dpi / 96.0)),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                    BitmapEncoderId = ext == "jpg" ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId,
                };
                using (var file = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                using (IRandomAccessStream stream = file.AsRandomAccessStream())
                {
                    await page.RenderToStreamAsync(stream, options).AsTask(token).ConfigureAwait(false);
                    await stream.FlushAsync().AsTask(token).ConfigureAwait(false);
                }
            }
        }

        public static OcrEngine CreateOcr()
        {
            OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages()
                               ?? OcrEngine.AvailableRecognizerLanguages.Select(OcrEngine.TryCreateFromLanguage).FirstOrDefault(e => e != null);
            return engine ?? throw new OperationException("Instale um idioma com reconhecimento de texto nas Configurações do Windows.");
        }

        /// <summary>Text of an image file, one line per recognized line.</summary>
        public static async Task<string> RecognizeImageAsync(OcrEngine engine, string path, CancellationToken token)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (IRandomAccessStream stream = file.AsRandomAccessStream())
            {
                return await RecognizeStreamAsync(engine, stream, token).ConfigureAwait(false);
            }
        }

        /// <summary>Text of a PDF page rendered at 200 DPI.</summary>
        public static async Task<string> RecognizePageAsync(OcrEngine engine, PdfDocument document, uint index, CancellationToken token)
        {
            using (PdfPage page = document.GetPage(index))
            using (var stream = new InMemoryRandomAccessStream())
            {
                var options = new PdfPageRenderOptions
                {
                    DestinationWidth = (uint)Math.Round(page.Size.Width * 200 / 96.0),
                    BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255),
                };
                await page.RenderToStreamAsync(stream, options).AsTask(token).ConfigureAwait(false);
                stream.Seek(0);
                return await RecognizeStreamAsync(engine, stream, token).ConfigureAwait(false);
            }
        }

        private static async Task<string> RecognizeStreamAsync(OcrEngine engine, IRandomAccessStream stream, CancellationToken token)
        {
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token).ConfigureAwait(false);
            uint max = OcrEngine.MaxImageDimension;
            var transform = new BitmapTransform();
            uint width = decoder.OrientedPixelWidth, height = decoder.OrientedPixelHeight;
            if (width > max || height > max)
            {
                double scale = (double)max / Math.Max(width, height);
                transform.ScaledWidth = (uint)(decoder.PixelWidth * scale);
                transform.ScaledHeight = (uint)(decoder.PixelHeight * scale);
                transform.InterpolationMode = BitmapInterpolationMode.Fant;
            }
            using (SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                       ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(token).ConfigureAwait(false))
            {
                OcrResult result = await engine.RecognizeAsync(bitmap).AsTask(token).ConfigureAwait(false);
                var text = new StringBuilder();
                foreach (OcrLine line in result.Lines)
                {
                    text.AppendLine(line.Text);
                }
                return text.ToString();
            }
        }

        /// <summary>Page indices as a list, for loops with progress.</summary>
        public static IEnumerable<uint> Pages(PdfDocument document)
        {
            for (uint i = 0; i < document.PageCount; i++)
            {
                yield return i;
            }
        }
    }
}
