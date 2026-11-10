# PTH Touch Relative Mode

A macOS output-mode plugin for OpenTabletDriver that combines relative pen input with trackpad-style Wacom touch input.

- One finger moves the cursor.
- Exactly two fingers scroll vertically and horizontally without moving the cursor.
- One-finger tap sends a left click; two quick taps produce a native macOS double click.
- Two-finger tap sends a right click.
- Tap, then touch again and move or hold the second contact to drag. Lift the finger to release; this supports text selection and moving windows without a title-bar double click.
- Moving, scrolling, long holds and palm contacts do not turn into taps.
- After scrolling, all fingers must lift before one-finger cursor movement resumes.
- Touch is suppressed near the pen, including accidental contacts that began while the pen was present.
- Optional near-proximity gating suppresses unreliable far-hover coordinates while preserving pen/button releases. Re-entry establishes a fresh relative origin.

## Requirements and tested hardware

- macOS, Apple silicon or Intel.
- OpenTabletDriver **0.6.8.1 or newer** and its normal Accessibility/Input Monitoring permissions.
- Tested hardware: **Wacom Intuos Pro Large PTH-851**, macOS 26.5.2 on Apple silicon. Other touch tablets have not been hardware-tested.

The plugin uses OpenTabletDriver's existing device parser and relative pen pipeline. It does not install a kernel extension or require additional macOS permissions beyond the host's permissions.

## Install

1. Download `PthTouch.zip` from [Releases](https://github.com/matebenyovszky/PthTouch/releases).
2. Open OpenTabletDriver's Plugin Manager and install the ZIP file.
3. Restart both the GUI and the daemon if the mode does not appear.
4. Select **PTH Touch Relative Mode** in the Output tab, then Save/Apply.

Disable other touch/gesture filters and output modes for this tablet to avoid duplicate handling. Pen sensitivity is still configured with the normal X/Y Sensitivity controls. Keep OpenTabletDriver running.

Default options: tap-to-click and tap-and-drag enabled, natural scrolling enabled, touch scrolling 12 px/mm, unstable-hover limiting enabled. Taps must finish within 300 ms and move no more than 1 mm. Two-finger scrolling begins after 1 mm of movement. Multi-click timing follows the macOS system setting.

## Build and checks

Requires the .NET 10 SDK:

```sh
dotnet build plugin/PthTouch.csproj -c Release
dotnet run --project checks/Checks.csproj -c Release
```

The plugin DLL is `plugin/bin/Release/net10.0/PthTouch.dll`. Place only that DLL at the top level of the distribution ZIP. Do not bundle OpenTabletDriver assemblies.

Checks exercise touch transitions, stationary contacts, scroll/tap separation, left/right tap dispatch, native double-click sequencing, tap-and-drag and release/cancellation, pen proximity suppression, far-hover release preservation and re-entry without a cursor jump. They use fake pointers/click sinks and do not generate native UI events, so they also run on Linux CI.

## Limitations

No pinch, rotate, three-finger gestures, momentum scrolling or drag lock. Near-proximity limiting only acts on devices that report the Wacom proximity flag; it is not a general interference filter. The pointer pauses in the far, unreliable hover range by design.

## License

MIT. OpenTabletDriver is a separate project with its own license. This plugin is not affiliated with Wacom.
