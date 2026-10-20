# Architecture

```
keyboard ─► KeyboardHook (own thread, WH_KEYBOARD_LL) ─► TamarilloController (UI thread)
                                                            │  chord + mouse down → RadialWindow.Arm (invisible drop target)
                                                            │  chord, no drag, Explorer → ExplorerSelection → RadialWindow.ShowHold
OLE drag ─► RadialWindow (DragEnter: files → MenuBuilder) ──┘
                 │ Committed(MenuNode, FileSet)
                 ▼
             JobRunner ─► Operation.RunAsync (thread pool / STA workers, below-normal priority, ≤ 2 at once)
                 │            └─ Engines: WIC · Media Foundation · Windows.Data.Pdf · OCR · tar.exe · ffmpeg* · Office/LibreOffice*
                 ▼
             OverlayWindow ─► JobCard (progress → ✓ / error)
```

## Lightness budget

| Idle | Cost |
|---|---|
| Threads | 1 sleeping in `GetMessage` (the hook). No timers, no polling, no watchers. |
| Hook callback | Table update + compare, then `CallNextHookEx`. Never touches the UI thread; a busy host can never lag typing. |
| Windows | Wheel and overlay created once at `ApplicationIdle` after start, hidden until used. |
| Detection | Optional engines probed once, 2 s after start, below-normal priority (spawns `ffmpeg -encoders` once). |

During work: heavy operations run off the UI thread at below-normal priority (external processes too); progress reaches the UI at most once per render; two jobs run at once, the rest queue.

## Reliability

- **Atomic outputs:** each result is written to a hidden `~tamarillo-*` temp next to its destination and renamed into place only when complete; failures and cancellations delete it. Names that exist get " (2)", sources are never overwritten.
- **Copy-only drops** (see design system), zip-slip protection, extraction without double nesting.
- **Fallbacks:** WIC decode → ffmpeg; Media Foundation → ffmpeg; Office → LibreOffice; text formats need nothing.
- **Isolation:** Office only quits if it opened no documents of the user's; LibreOffice runs with its own profile so a running instance is untouched.
- **Settings:** atomic write + `.bak`, unknown/missing fields fall back to defaults, saved off the UI thread.
- **Errors:** every failure becomes a sentence for people (`JobRunner.UserMessage`); details go to the host log.
- **Hook state:** key-ups lost to the secure desktop are re-synced from `GetAsyncKeyState`.

## Host contract

`TamarilloModule` implements `IBackgroundModule` (`activation: startup`: the wheel works with the host window closed) and `IModuleSettings` (`settings: true`: the "Configurações" tab). It loads in `memory` mode; nothing needs `Assembly.Location`. WinRT APIs come from `Microsoft.Windows.SDK.Contracts` as reference-only metadata: nothing extra ships.

## Adding an action

1. Write an `Operation` (usually a `PerFileOperation`: implement `TargetExtension` and `TransformAsync`, write only to the temp path you are given).
2. Offer it in `MenuBuilder` only when it will work for every item on this PC (check `Capabilities`). Its id is part of a settings key (`image/convert/to-gif`, `files/zip` for the shared "Mais" actions) that users hide entries by: never rename an id. `MenuBuilder.Catalog` lists it in the settings on its own, as long as one of its sample sets reaches it.
3. Pick or draw an icon in `Icons.xaml` (24 grid, 1.8 stroke), write the label (≤ 12 chars) and the hub sentence.
4. Add a real-file test in `tests/Tamarillo.Tests`.
