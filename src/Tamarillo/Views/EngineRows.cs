using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Tamarillo.Engines;

namespace Tamarillo.Views
{
    /// <summary>One line of "what this computer can do", shared by the page and the settings tab.</summary>
    public sealed class EngineRow
    {
        public string Icon { get; set; }

        public string Name { get; set; }

        public string Detail { get; set; }

        public string Status { get; set; }

        public Brush ChipBackground { get; set; }

        public Brush ChipForeground { get; set; }
    }

    public static class EngineRows
    {
        private enum Tone
        {
            Ready,
            Missing,
            Warning,
        }

        public static IReadOnlyList<EngineRow> Build(Capabilities caps)
        {
            string images = "JPG, PNG, TIFF, BMP, GIF e ICO" + (caps.CanWebp ? ", WebP" : string.Empty) + (caps.HeifEncoder ? ", HEIC" : string.Empty)
                            + (caps.CanAvif ? ", AVIF" : string.Empty) + ". Redimensionar, girar, comprimir, PDF.";
            string office = caps.Word || caps.Excel || caps.PowerPoint
                ? string.Join(" · ", new[] { caps.Word ? "Word" : null, caps.Excel ? "Excel" : null, caps.PowerPoint ? "PowerPoint" : null }.Exclude())
                : caps.LibreOffice != null ? "LibreOffice" : null;
            return new[]
            {
                Row("Icon.Image", "Imagens", images, "Windows", Tone.Ready),
                Row("Icon.Video", "Áudio e vídeo", "MP4, WMV, MP3, M4A, WAV, WMA e FLAC pelo Media Foundation do Windows.", "Windows", Tone.Ready),
                Row("Icon.Pdf", "PDF", "Páginas em PNG ou JPG a 300 DPI; imagens e textos em PDF.", "Windows", Tone.Ready),
                Row("Icon.Scan", "Texto em imagens (OCR)", caps.Ocr ? "Reconhece texto nos idiomas instalados no Windows." : "Adicione um idioma com reconhecimento de texto nas Configurações do Windows.",
                    caps.Ocr ? "Windows" : "Sem idioma", caps.Ocr ? Tone.Ready : Tone.Warning),
                Row("Icon.Archive", "Arquivos compactados", caps.Tar != null ? "ZIP, TAR.GZ, 7Z e RAR (pelo tar do Windows)." : "ZIP.", "Windows", Tone.Ready),
                Row("Icon.Convert", "FFmpeg (opcional)",
                    caps.HasFfmpeg ? "MKV, WebM, GIF, MOV, OGG, Opus e mais. " + caps.Ffmpeg : "Instale o FFmpeg para MKV, WebM, GIF, MOV, OGG, Opus e AVIF.",
                    caps.HasFfmpeg ? "Encontrado" : "Não encontrado", caps.HasFfmpeg ? Tone.Ready : Tone.Missing),
                Row("Icon.Document", "Office (opcional)",
                    office != null ? "PDF e conversões de documentos, planilhas e apresentações." : "Com o Microsoft Office ou o LibreOffice: documentos, planilhas e apresentações em PDF.",
                    office ?? "Não instalado", office != null ? Tone.Ready : Tone.Missing),
            };
        }

        private static IEnumerable<string> Exclude(this IEnumerable<string> items)
        {
            foreach (string item in items)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }

        private static EngineRow Row(string icon, string name, string detail, string status, Tone tone)
        {
            string strong = tone == Tone.Ready ? "Brush.Success" : tone == Tone.Warning ? "Brush.Warning" : "Brush.Text.Secondary";
            string soft = tone == Tone.Ready ? "Brush.Success.Soft" : tone == Tone.Warning ? "Brush.Warning.Soft" : "Brush.Surface4";
            return new EngineRow
            {
                Icon = icon,
                Name = name,
                Detail = detail,
                Status = status,
                ChipBackground = (Brush)Application.Current.FindResource(soft),
                ChipForeground = (Brush)Application.Current.FindResource(strong),
            };
        }
    }
}
