<p align="center"><img src="assets/brand/png/logo-128.png" width="96" alt="Tamarillo" /></p>

# Tamarillo

A [SwissTechKnife](../SwissTechKnife) module for Windows, in the spirit of [Tangerine for Mac](https://tangerineformac.com/): **hold a shortcut while dragging files and a radial menu opens under the cursor** with what can be done with them. Drop on an action; the result lands next to the original, with progress in a small draggable overlay that ends with the output name, its folder and a green ✓ that dismisses it.

- **Trigger:** `Ctrl + Alt + Shift` (editable) during any file drag — Explorer, desktop, any app that drags files. Without a drag, the same chord opens the wheel for the files selected in Explorer: point and release the keys.
- **Context-aware:** the wheel only shows what works for *every* dragged item on *this* PC (images, video, audio, PDF, documents, spreadsheets, slides, subtitles, CSV/JSON, archives, folders).
- **Local and light:** Windows' own engines (WIC, Media Foundation, Windows.Data.Pdf, Windows OCR, tar.exe); FFmpeg, Microsoft Office and LibreOffice are detected and used when present, never bundled. Package ≈ 160 KB.

## What it does

| Files | Wheel |
|---|---|
| Images | Converter (JPG, PNG, WebP*, HEIC*, AVIF*, TIFF, BMP, GIF, ICO, PDF) · Tamanho (50%, 25%, 4K, 1920, 1280, 640) · Comprimir · Girar/espelhar · PDF (merge) · Texto (OCR) · Metadados (lossless) |
| Video | Converter (MP4, WMV; MOV, MKV, WebM, AVI, GIF*) · Áudio (MP3, M4A, WAV, FLAC*, Opus*) · Comprimir (1080p/720p/480p) |
| Audio | Converter (MP3, M4A, WAV, FLAC, WMA; OGG, Opus, AIFF*) · Comprimir |
| PDF | Imagens (PNG/JPG, 300 DPI) · Word* · Texto (OCR) |
| Documents, sheets, slides | PDF* · Converter (DOCX, ODT, RTF, TXT, HTML, XLSX, ODS, CSV, PPTX, ODP)*; TXT/MD → DOCX/RTF/PDF and DOCX/RTF → TXT built in |
| Subtitles, data | SRT ⇄ VTT ⇄ TXT · CSV ⇄ TSV ⇄ JSON · JSON format/minify |
| Archives | Extrair (ZIP, TAR, GZ, 7Z, RAR — no double nesting) |
| Anything (in **Mais**, or the whole wheel for folders/mixed) | ZIP · TAR.GZ · Copiar caminho/nome · Mostrar no Explorador · Terminal aqui · Compartilhar · SHA-256 · Fixar imagem na tela · Copiar imagem |

\* when the optional engine or Windows codec is installed.

## Layout

| Path | What |
|---|---|
| `src/Tamarillo` | The module (`Tamarillo.Module.dll`): hook, wheel, operations, engines, views |
| `src/Tamarillo.DevHost` | Runs the module without the host; `--snapshot <dir>` renders every visual state to PNG; `--selftest-drag` drives a real OLE drag end to end (moves the cursor ~2 s) |
| `tests/Tamarillo.Tests` | Console test runner with real conversions |
| `lib/SwissTechKnife.Sdk.dll` | Compile-only host contract (`scripts/update-sdk.ps1`) |
| `assets/brand` | Logo rasters generated from `src/Tamarillo/Theme/Brand.xaml` |
| `docs/` | [Design system](docs/DESIGN_SYSTEM.md) · [Architecture](docs/ARCHITECTURE.md) |

## Build

```powershell
powershell -File scripts/build.ps1         # build + tests + artifacts/tamarillo-<ver>.zip + artifacts/catalog-entry.json
powershell -File scripts/render-brand.ps1  # after editing the logo
powershell -File scripts/update-sdk.ps1    # after the host's SDK changes
```

Install in the host: publish the zip (release asset) and add `artifacts/catalog-entry.json` to the host's `catalog/catalog.json`.

Data: `%LOCALAPPDATA%\SwissTechKnife\data\tamarillo\settings.json` (DevHost: `%LOCALAPPDATA%\Tamarillo.DevHost`).
