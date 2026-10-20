# Tamarillo Design System

Tamarillo is a layer **on top of** the SwissTechKnife design system, never beside it. Every host token (surfaces, text, accent, status, type ramp, spacing, radii, `Motion.*`, `Ease.*`) is used as is; Tamarillo only adds what the host has no concept of: a radial menu, a floating progress layer, an icon set for file actions and a brand.

Sources: `src/Tamarillo/Theme/` — `Tokens.xaml` (merges the host tokens), `Icons.xaml`, `Controls.xaml`, `Brand.xaml`, entry point `Tamarillo.xaml`. Code reads tokens through `Ui/Theme.cs`; no value is hard-coded in a view.

## Principles

1. **Host first.** Module pages and the settings tab use only host styles (`Card`, `Switch`, `Segmented.*`, `Chip`, `Button.*`, `Text.*`). The floating layer reuses host colors, type and motion.
2. **The selection is orange.** In the wheel, the slot that will run on drop is `Brush.Accent` with `OnAccent` content. Nothing else is orange. A group whose sub-ring is open keeps a 35% accent "path" tint.
3. **Tone, not strokes.** Segments are opaque `Tama.Ring` on any wallpaper; separation is a constant 4 px gap. Elevation of floating surfaces is a soft radial contact shadow (wheel) or `Elevation.Popup` (cards).
4. **Status colors are status.** Green appears only as the "done" disc (which is also the dismiss button), red only for failures.
5. **Motion explains.** The wheel pops from the cursor, segments arrive in a stagger, the highlight *glides* between slots instead of jumping, sub-rings unfold from their group outward. Opacity and transforms only. When Windows animations are off, every duration is zero.

## Tokens added (`Tokens.xaml`)

| Group | Keys |
|---|---|
| Materials | `Tama.Ring` (95% Surface2-ish segment fill) · `Tama.Hub` · `Tama.Rim` · `Tama.Brand #E65A70` (brand only: catalog tile, never state) |
| Radial geometry | `Radial.Hub 50` · `Radial.Inner 58` · `Radial.Outer 138` · `Radial.SubInner 146` · `Radial.SubOuter 198` · `Radial.Gap 4` · `Radial.Corner 9` · `Radial.SubItemAngle 30` (max; long lists tighten to fit 210°) · `Radial.Margin 28` · `Radial.IconSize 22` · `Radial.SubIconSize 18` |
| Motion | `Motion.Wheel.In 260ms` (with `Ease.Pop`) · `Motion.Wheel.Stagger 16ms` · `Motion.Wheel.Out 140ms` (`Ease.In`) · `Motion.Highlight 160ms` · `Motion.SubRing.In 220ms` · `Motion.Overlay.In 320ms` · `Motion.Overlay.Out 180ms` · `Motion.Check 420ms` |
| Easing | `Ease.Pop` (BackEase out, amplitude 0.32: one small overshoot) · `Ease.In` |
| Overlay | `Overlay.Width 360` · `Overlay.Ring 36` · `Overlay.RingStroke 3` |
| Icons | `Icon.Stroke 1.8` |

## Components (`Controls.xaml` + `Ui/`)

| Component | Use |
|---|---|
| **Wheel** (`RadialWindow`) | Hub (thumbnail or kind icon + summary; while pointing, the full action sentence) · ring of ≤ 8 slots (icon + label, or a typographic badge for formats) · groups marked by a dot on the outer edge · sub-ring of ≤ 8 badges/icons |
| **Job card** (`JobCard`, `Tama.Card`) | Status disc (progress ring around the action icon → green ✓ / red alert, display only) · title (action → output name, opens it) · detail (current file "2 de 5" → output folder, reveals it) · corner × (cancels while running, closes when finished) · finished cards leave after `Overlay.AutoDismiss` (10 s), paused while hovered |
| `Tama.RoundButton` | 36 px round button with animated hover/press tints |
| `Tama.Link` | Text that acts (opacity dip on hover, no underline) |
| `Tama.Text.HubTitle` / `HubCaption` / `Label` / `Badge` | Wheel typography: 12 semibold centered, 11 tertiary, 11 semibold, 13 bold display |
| `IconView` | Draws one icon of the set at any size |
| `Keycaps` | A shortcut as keycaps: `[Ctrl] + [Alt] + [Shift]` |

## Iconography (`Icons.xaml`)

24 × 24 grid, 2 px safe margin, **1.8 stroke** (scales with size), round caps and joins, no fills, one idea per icon. Same language as the host's catalog icons. Formats are never drawn as icons: they are typographic badges (`PNG`, `MP4`), which read faster and never lie about the format.

Kinds: `Image Video Audio Document Pdf Table Slides Archive Folder Subtitles Code Files`. Actions: `Convert Resize Compress Rotate(Left|Right|180) Flip Merge Erase Text Scan Extract Zip Waveform Format Minify Gif`. Shell: `More Copy Link Name Terminal Reveal Pin Share Hash Clipboard`. States: `Check Close Alert Cancel`.

## Interaction

- **Drag mode:** chord + mouse button down → an invisible drop target appears under the cursor; on DragEnter with files the wheel animates in. Drop on a slot = run; centre = cancel (cursor shows "no"); dragging out of the wheel dismisses it. Drop on a group opens its sub-ring and waits for a click.
- **Hold mode:** chord without a drag, Explorer in front, files selected → after 140 ms the wheel opens; point and **release the chord** to run. Arrows/Tab move, Enter runs/opens, Esc goes back/closes (the wheel owns these keys only while open).
- **Marking-menu geometry:** anywhere outward selects by angle (huge targets); hovering a group opens its sub-ring at once (no dwell: a delay reads as lag). Selection arms only after 6 px of movement so a wheel clamped to a screen edge never fires what happens to be under the cursor.
- **Copy, never move:** the wheel always answers the drag source with Copy, so a source can never delete what was dropped.

## Copy (pt-BR)

Verbs for actions ("Converter", "Comprimir"), the hub states the full outcome ("Converter para PNG", "Juntar 3 imagens em um PDF"), errors say what happened and what to do ("Instale um idioma com reconhecimento de texto nas Configurações do Windows."). No jargon in the UI: "Metadados", not "EXIF strip".

## Brand (`Brand.xaml`)

A tamarillo cut across: crimson skin, apricot flesh, wine seeds and hub, one wedge pulled out. The cross-section *is* the product — hub (the files), ring (the options), the pulled wedge (the option you drop on); six seeds point at the hub like the wheel's directions. The leaf and stem only appear from 40 px; 16–32 px use `Brand.LogoSmallDrawing`. App icon sits on the family's dark squircle badge, like SwissTechKnife's. Edit the XAML, then `scripts/render-brand.ps1`; never edit rasters.
