using System.IO;
using Tamarillo.Operations;

namespace Tamarillo.Engines
{
    /// <summary>Lossless metadata removal by copying only the image-bearing segments/chunks.</summary>
    internal static class MetadataStripper
    {
        /// <summary>JPEG: drops APP1 (EXIF/XMP), APP12–APP15, APP13 (IPTC) and COM; keeps JFIF, ICC (APP2) and Adobe (APP14).</summary>
        public static void StripJpeg(string input, string output)
        {
            byte[] data = File.ReadAllBytes(input);
            if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            {
                throw new OperationException("Este arquivo JPEG está corrompido.");
            }
            using (var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                target.WriteByte(0xFF);
                target.WriteByte(0xD8);
                int i = 2;
                while (i + 4 <= data.Length)
                {
                    if (data[i] != 0xFF)
                    {
                        throw new OperationException("Este arquivo JPEG está corrompido.");
                    }
                    byte marker = data[i + 1];
                    if (marker == 0xDA)
                    {
                        // Start of scan: the rest is image data up to EOI, copied verbatim.
                        target.Write(data, i, data.Length - i);
                        return;
                    }
                    int length = (data[i + 2] << 8) | data[i + 3];
                    if (length < 2 || i + 2 + length > data.Length)
                    {
                        throw new OperationException("Este arquivo JPEG está corrompido.");
                    }
                    bool drop = marker == 0xE1 || marker == 0xED || marker == 0xFE || (marker >= 0xEC && marker <= 0xEF && marker != 0xEE);
                    if (!drop)
                    {
                        target.Write(data, i, 2 + length);
                    }
                    i += 2 + length;
                }
            }
        }

        /// <summary>PNG: drops tEXt, zTXt, iTXt, eXIf and tIME chunks.</summary>
        public static void StripPng(string input, string output)
        {
            byte[] data = File.ReadAllBytes(input);
            if (data.Length < 8 || data[0] != 0x89 || data[1] != 0x50)
            {
                throw new OperationException("Este arquivo PNG está corrompido.");
            }
            using (var target = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                target.Write(data, 0, 8);
                int i = 8;
                while (i + 12 <= data.Length)
                {
                    int length = (data[i] << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3];
                    if (length < 0 || i + 12 + length > data.Length)
                    {
                        throw new OperationException("Este arquivo PNG está corrompido.");
                    }
                    string type = System.Text.Encoding.ASCII.GetString(data, i + 4, 4);
                    if (type != "tEXt" && type != "zTXt" && type != "iTXt" && type != "eXIf" && type != "tIME")
                    {
                        target.Write(data, i, 12 + length);
                    }
                    i += 12 + length;
                    if (type == "IEND")
                    {
                        break;
                    }
                }
            }
        }
    }
}
