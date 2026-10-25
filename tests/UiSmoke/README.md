# WPF UI smoke checks

Run on Windows with the .NET 10 SDK:

```powershell
dotnet run --project tests/UiSmoke -c Release
```

The executable renders all 12 editor dialogs in both themes and the main window at its minimum size, using offscreen windows. It fails on WPF binding errors, clipped dialog controls, missing window borders, invalid canvas sizes, incorrect checkbox hover/indeterminate colors, or a clipped main toolbar. PNGs are written to `%TEMP%/xaml-ui-smoke` (an optional first argument changes the output folder).

These checks exercise layout and rendering without importing, converting, or exporting user files. They do not replace interactive testing of those operations or testing on multiple physical DPI configurations.

The light-theme state sheet covers normal, hover, pressed, selected, disabled, and keyboard-focus appearances. It also checks disabled text colors and a minimum 4.5:1 contrast ratio for normal, primary, hover, pressed, and selected text. The main window is rendered again after a live switch to the light palette.
