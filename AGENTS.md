## Conventions

- Code, comments, docs and commit messages in English; UI text in Brazilian Portuguese.
- This is a SwissTechKnife module: follow the host's rules (`../SwissTechKnife/AGENTS.md`, `docs/MODULES.md`, `docs/DESIGN_SYSTEM.md`). Pages and the settings tab use host styles only; the floating layer uses the Tamarillo tokens in `src/Tamarillo/Theme` (see `docs/DESIGN_SYSTEM.md`). No hard-coded colors, sizes or durations.
- Lightness is a feature: nothing idle may poll, tick or watch; the hook callback stays trivial; heavy work runs off the UI thread at below-normal priority.
- Outputs are written to a temp path and committed atomically; never overwrite a source; drops answer Copy only.
- Offer an action only when it works for every item on this machine (`Capabilities`).
- Edit UTF-8 files with the file tools, or with `[IO.File]` and an explicit UTF-8 encoding — never Windows PowerShell `Get-Content`/`Set-Content` (see ERRORS.md).
- Run `powershell -File scripts/build.ps1` before handing off; record non-trivial fixes in `ERRORS.md`.
- Visual changes: check them with `Tamarillo.DevHost.exe --snapshot <dir>`.
