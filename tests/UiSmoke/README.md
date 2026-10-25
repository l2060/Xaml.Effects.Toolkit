# WPF UI smoke checks

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project tests/UiSmoke -c Release
```

The executable renders all editor dialogs in both themes and the main window at its minimum size, using offscreen windows. It includes both Wil/Wzl tool modes and checks the file/directory/Wil-Wzl import mode transitions. It fails on WPF binding errors, clipped dialog controls, incorrect disabled import options, missing window borders, invalid canvas sizes, incorrect checkbox hover/indeterminate colors, or a clipped main toolbar. PNGs are written to `%TEMP%/xaml-ui-smoke` (an optional first argument changes the output folder).

Operation checks generate small WIL/WIX and WZL/WZX fixtures in a fresh subdirectory of the output folder. They test extraction to PNG/schema.json, conversion to asset, passwords, both asset compression modes, append/overwrite/replace, empty slots, valid 1x1 frames, offsets, RGBA orientation and alpha, cancellation, corrupt input, missing companion files, batch boundaries, and the asynchronous import command. Coverage includes 8/16/32-bit WIL, 8/16/24-bit raw WZL, 32-bit zlib WZL, alpha4, custom palettes, black-background alpha, ordinary PNG import, error recovery and deferred close during cancellation. Existing user resources are never used or modified. These checks do not replace testing real game archives or multiple physical DPI configurations.

The light-theme state sheet covers normal, hover, pressed, selected, disabled, and keyboard-focus appearances. It also checks disabled text colors and a minimum 4.5:1 contrast ratio for normal, primary, hover, pressed, and selected text. The main window is rendered again after a live switch to the light palette.
