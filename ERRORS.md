# Resolved errors

## 2026-10-19 — CI: `Tests failed.` although every test passed (exit code 0xC000000D)

- Cause: on the GitHub Windows Server runner the test process fails fast with STATUS_INVALID_PARAMETER while tearing down after `Main` returned 0 (WinRT / Media Foundation components used by the tests). `Environment.Exit` hangs there instead. Not reproducible on a desktop machine.
- Solution: `module-release.yml` builds with `-SkipTests` and runs the tests in their own step, judged by the `N passed, 0 failed` line. Locally `scripts/build.ps1` still uses the exit code.

## 2026-10-19 — Accented text turned into `Ã§`, `â•` after an edit

- Cause: Windows PowerShell 5 `Get-Content`/`Set-Content` read UTF-8 without BOM as ANSI and wrote it back.
- Solution: edit with the file tools, or `[IO.File]::ReadAllText/WriteAllText` with `New-Object Text.UTF8Encoding $false`. Scripts with non-ASCII text that Windows PowerShell runs (`pack.ps1`) are saved **with** a BOM.

## 2026-10-19 — Wheel crashed on hover: `InvalidOperationException` in `Animatable.BeginAnimation` (SetSlotState)

- Cause: `IconView` froze its `Pen`; freezing a pen freezes its brush, which was the slot's animated brush.
- Solution: freeze the pen only when its brush is already frozen.

## 2026-10-19 — Sub-ring blinked when it appeared

- Cause: the sub-ring items fade in with a staggered `BeginTime`. Before its `BeginTime` an animation does not apply its `From`, so each item rendered at its base opacity 1 for the delay, then jumped to 0 and faded back in.
- Solution: `HideUntilAnimated` sets the base opacity/scale to the animation's start values before animating (ring and sub-ring). Any staggered entrance needs this.

## 2026-10-19 — `CS0176 ResizeMode.NoResize` / `CS0104 ResizeMode is ambiguous`

- Cause: a module enum named `ResizeMode` shadowed `System.Windows.ResizeMode`.
- Solution: renamed to `ResizeKind`. Don't reuse WPF type names for module types.

## 2026-10-19 — Click-mode wheel closed by itself

- Cause: WPF raises `MouseLeave` on re-hit-tests even when the mouse never entered the window.
- Solution: only dismiss on `MouseLeave` after a real `MouseMove` over the wheel (`_hadMouse`).

## 2026-10-19 — Stale orange wedge in a new wheel

- Cause: highlight opacity animations (HoldEnd) survived between openings.
- Solution: `ResetState` clears both highlight animations and opacities.

## 2026-10-19 — Drag self-test: `DoDragDrop` returned at once / wheel never armed

- Causes: (1) the drag source was the module's own UI thread, whose OLE modal loop starves dispatcher work; (2) `DoDragDrop` ran before the injected button-down was processed; (3) the source window had no content, so `ContentRendered` never fired; (4) the second `Application` in the child process threw.
- Solution: the source is a child process (`--drag-source`) started before any `Application` exists, with content, waiting for `GetAsyncKeyState(VK_LBUTTON)` before `DoDragDrop`. The self-test moves the real cursor: don't run it while someone is using the mouse.

## 2026-10-19 — Snapshot PNGs off-centre

- Cause: `VisualBrush` maps the visual's content bounds, not its layout box.
- Solution: `Viewbox = new Rect(0, 0, width, height)`, `ViewboxUnits = Absolute`.

## 2026-10-19 — Settings ComboBox showed `Tamarillo.Operations.MenuSection` instead of the title

- Cause: the host's ComboBox template draws the selection box from `SelectionBoxItem` with no item template, so `DisplayMemberPath` only reaches the drop-down list.
- Solution: bind plain objects whose `ToString()` returns the display text (`MenuSection.ToString() => Title`), or give the items a `DataTemplate`.
