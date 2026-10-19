using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Tamarillo.Engines
{
    /// <summary>
    /// Still images through WIC (WPF imaging): decode with EXIF orientation applied, resize with the decoder's
    /// high-quality scaler, encode to every container Windows has an encoder for, plus ICO written by hand.
    /// Call from a <see cref="StaWorker"/> thread.
    /// </summary>
    internal static class ImageEngine
    {
        private static readonly Guid JpegContainer = new Guid("19e4a5aa-5662-4fc5-a0c0-1758028e1057");

        public sealed class Decoded
        {
            public BitmapSource Bitmap;
            public int Orientation;
            public bool IsJpeg;
            public double DpiX;
        }

        /// <summary>Formats the wheel offers, by output extension.</summary>
        public static bool IsWicTarget(string ext) =>
            ext == "jpg" || ext == "png" || ext == "bmp" || ext == "tiff" || ext == "gif" || ext == "webp" || ext == "heic" || ext == "ico";

        /// <summary>
        /// Decodes the first frame, upright. <paramref name="maxEdge"/> &gt; 0 decodes straight to that long edge
        /// (never upscaling), which is both the best-quality and the cheapest way to shrink.
        /// </summary>
        public static Decoded Load(string path, int maxEdge = 0, double scale = 0)
        {
            int orientation;
            int width, height;
            bool isJpeg;
            double dpi;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                BitmapDecoder probe = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
                BitmapFrame frame = probe.Frames[0];
                orientation = ReadOrientation(frame);
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                dpi = frame.DpiX;
                isJpeg = probe.CodecInfo?.ContainerFormat == JpegContainer;
            }

            int decodeWidth = 0, decodeHeight = 0;
            int longEdge = Math.Max(width, height);
            int targetLong = maxEdge > 0 ? Math.Min(maxEdge, longEdge) : scale > 0 && scale < 1 ? (int)Math.Round(longEdge * scale) : 0;
            if (targetLong > 0 && targetLong < longEdge)
            {
                double factor = (double)targetLong / longEdge;
                decodeWidth = Math.Max(1, (int)Math.Round(width * factor));
                decodeHeight = Math.Max(1, (int)Math.Round(height * factor));
            }

            BitmapSource bitmap;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = stream;
                // Orientation 1..8 is applied below; tell the decoder the pre-rotation size.
                if (decodeWidth > 0)
                {
                    image.DecodePixelWidth = decodeWidth;
                    image.DecodePixelHeight = decodeHeight;
                }
                image.EndInit();
                bitmap = image;
            }
            bitmap = ApplyOrientation(bitmap, orientation);
            bitmap.Freeze();
            return new Decoded { Bitmap = bitmap, Orientation = orientation, IsJpeg = isJpeg, DpiX = dpi };
        }

        public static int ReadOrientation(BitmapFrame frame)
        {
            try
            {
                if (frame.Metadata is BitmapMetadata metadata && metadata.GetQuery("System.Photo.Orientation") is ushort value && value >= 1 && value <= 8)
                {
                    return value;
                }
            }
            catch
            {
                // Formats without the policy (BMP, GIF) or broken EXIF: treat as upright.
            }
            return 1;
        }

        public static BitmapSource ApplyOrientation(BitmapSource bitmap, int orientation)
        {
            switch (orientation)
            {
                case 2: return Transform(bitmap, new ScaleTransform(-1, 1));
                case 3: return Transform(bitmap, new RotateTransform(180));
                case 4: return Transform(bitmap, new ScaleTransform(1, -1));
                case 5: return Transform(Transform(bitmap, new RotateTransform(90)), new ScaleTransform(-1, 1));
                case 6: return Transform(bitmap, new RotateTransform(90));
                case 7: return Transform(Transform(bitmap, new RotateTransform(270)), new ScaleTransform(-1, 1));
                case 8: return Transform(bitmap, new RotateTransform(270));
                default: return bitmap;
            }
        }

        public static BitmapSource Transform(BitmapSource bitmap, Transform transform)
        {
            var result = new TransformedBitmap(bitmap, transform);
            result.Freeze();
            return result;
        }

        public static bool HasAlpha(BitmapSource bitmap)
        {
            PixelFormat format = bitmap.Format;
            if (format != PixelFormats.Bgra32 && format != PixelFormats.Pbgra32 && format != PixelFormats.Prgba64 && format != PixelFormats.Rgba64
                && format != PixelFormats.Rgba128Float && format != PixelFormats.Prgba128Float && !(format == PixelFormats.Indexed8 || format == PixelFormats.Indexed4 || format == PixelFormats.Indexed2 || format == PixelFormats.Indexed1))
            {
                return false;
            }
            // Many PNGs carry an alpha channel that is fully opaque: check the pixels.
            var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int stride = bgra.PixelWidth * 4;
            var row = new byte[stride];
            for (int y = 0; y < bgra.PixelHeight; y++)
            {
                bgra.CopyPixels(new Int32Rect(0, y, bgra.PixelWidth, 1), row, stride, 0);
                for (int x = 3; x < stride; x += 4)
                {
                    if (row[x] != 255)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Composites over white (JPEG and BMP have no alpha; black would be the WIC default).</summary>
        public static BitmapSource FlattenOnWhite(BitmapSource bitmap)
        {
            if (!HasAlpha(bitmap))
            {
                return ToBgr(bitmap);
            }
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var rect = new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight);
                dc.DrawRectangle(Brushes.White, null, rect);
                dc.DrawImage(bitmap, rect);
            }
            var target = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            return ToBgr(target);
        }

        private static BitmapSource ToBgr(BitmapSource bitmap)
        {
            if (bitmap.Format == PixelFormats.Bgr24 || bitmap.Format == PixelFormats.Gray8)
            {
                return bitmap;
            }
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
            converted.Freeze();
            return converted;
        }

        public static void Save(BitmapSource bitmap, string path, string ext, int jpegQuality = 90)
        {
            if (ext == "ico")
            {
                SaveIco(bitmap, path);
                return;
            }
            BitmapEncoder encoder;
            BitmapSource frame = bitmap;
            switch (ext)
            {
                case "jpg":
                case "jpeg":
                    encoder = new JpegBitmapEncoder { QualityLevel = jpegQuality };
                    frame = FlattenOnWhite(bitmap);
                    break;
                case "png":
                    encoder = new PngBitmapEncoder();
                    break;
                case "bmp":
                    encoder = new BmpBitmapEncoder();
                    frame = FlattenOnWhite(bitmap);
                    break;
                case "tif":
                case "tiff":
                    encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw };
                    break;
                case "gif":
                    encoder = new GifBitmapEncoder();
                    break;
                case "webp":
                    encoder = BitmapEncoder.Create(Capabilities.WebpContainer);
                    break;
                case "heic":
                    encoder = BitmapEncoder.Create(Capabilities.HeifContainer);
                    break;
                default:
                    throw new NotSupportedException(ext);
            }
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }
        }

        public static byte[] EncodeJpeg(BitmapSource bitmap, int quality)
        {
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(FlattenOnWhite(bitmap)));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        /// <summary>Square, centred icon with PNG frames from 16 to 256 px (never larger than the source).</summary>
        private static void SaveIco(BitmapSource bitmap, string path)
        {
            int source = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
            var frames = new List<KeyValuePair<int, byte[]>>();
            foreach (int size in new[] { 16, 24, 32, 48, 64, 128, 256 })
            {
                if (size > source && frames.Count > 0)
                {
                    break;
                }
                frames.Add(new KeyValuePair<int, byte[]>(size, RenderSquarePng(bitmap, size)));
            }
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)frames.Count);
                int offset = 6 + 16 * frames.Count;
                foreach (KeyValuePair<int, byte[]> frame in frames)
                {
                    byte dim = frame.Key >= 256 ? (byte)0 : (byte)frame.Key;
                    writer.Write(dim);
                    writer.Write(dim);
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write(frame.Value.Length);
                    writer.Write(offset);
                    offset += frame.Value.Length;
                }
                foreach (KeyValuePair<int, byte[]> frame in frames)
                {
                    writer.Write(frame.Value);
                }
            }
        }

        private static byte[] RenderSquarePng(BitmapSource bitmap, int size)
        {
            double scale = (double)size / Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
            double w = bitmap.PixelWidth * scale, h = bitmap.PixelHeight * scale;
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(bitmap, new Rect((size - w) / 2, (size - h) / 2, w, h));
            }
            var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }
    }
}
