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
- An optional short stationary press/release of the upper pen-scroll button sends a middle click, for example to open links in a new tab. Scrolling, pressure, other-button use, long holds and proximity loss cancel it.
- Optional three-finger swipes switch Spaces/full-screen apps horizontally, open Mission Control upward, and show the current application's windows downward. These are discrete shortcut-triggered actions, not interactive trackpad animations.
- Optional experimental two-finger pinch sends native magnification events to compatible apps, separate from two-finger scrolling.
- Optional near-proximity gating suppresses unreliable far-hover coordinates while preserving pen/button releases. Re-entry establishes a fresh relative origin.
- Optional three-report median spike rejection removes isolated hover outliers before the deadband. It delays small continuous hover movement by one report (about 5 ms at 200 Hz); movements larger than four times the deadband bypass it immediately. Contact and side buttons bypass both filters. The option defaults to off.
- An opt-in spatial hover deadband suppresses tiny stationary jitter without temporal averaging. Pen pressure and side-button use bypass it; the setting defaults to 0 mm (off). For stationary PTH-851 noise, a 0.10 mm deadband with spike rejection worked well in a 30-second replay at 20 px/mm; results depend on hardware and environment. Without spike rejection, a 0.04 mm threshold corresponds to 0.8 px at 20 px/mm.

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

## Settings window

Download **PthTouchSettings-macOS.zip** from the same release, unzip it, and move
**PthTouch Settings.app** to Applications. The companion app supports Apple
silicon and Intel on macOS 13 or newer. OpenTabletDriver must already be running
with this output mode selected.

The Pen, Touch & gestures, and Stability tabs expose sensitivity, pen-scroll
speed/direction, short-press middle clicking, tap/drag, scrolling, swipes, pinch,
and hover noise filtering. **Save & apply** reads the current driver settings,
merges only your edited controls, backs up the previous configuration, applies
and verifies the result, saves it, then refreshes the main driver window.
Changes made meanwhile in the main window are preserved when that control was
not edited here. Reload discards pending edits and reads the driver again.

Backups are stored in `~/Library/Application Support/PthTouchSettings/Backups`.
The app communicates only with OpenTabletDriver's existing local same-user
settings pipe; it does not read keyboard input or request input-monitoring
permissions. It was tested with OpenTabletDriver 0.6.8.1 on macOS 26.5.2.

Build the universal companion app with `python3 settings/build.py` (requires
Xcode tools). The result is `dist/PthTouch Settings.app`; the build uses ad-hoc
code signing. The plugin ZIP continues to contain only the plugin DLL.

## Optional system swipes and pinch zoom

Enable **Three-finger system swipes** and/or **Pinch zoom (experimental)** in the
output-mode options. Both are off by default. Three fingers must travel together
at least 6 mm in a clear direction; one gesture triggers one action, and all
fingers must lift before a new gesture. Horizontal finger motion follows the
trackpad content convention: fingers left reveal the Space on the right.
The actions use Control+Arrow shortcuts, which must be enabled in macOS.
They do not provide an interactive, finger-following Mission Control animation.

A two-finger contact locks to scroll or pinch based on centroid travel versus
finger separation. A pinch sends began/changed/ended/cancelled magnify events;
partial lifts, contact-ID changes, palm contacts, pen takeover and unloading
cancel safely. Existing two-finger taps still right-click. Pinch works in apps
that accept native magnification events; app support varies. It uses undocumented
CoreGraphics gesture fields and may need changes after macOS updates. No private
framework is loaded, and no additional permission is requested.

## Optional pen-button scrolling

The separate [Scroll Bindings](https://github.com/Mrcubix/Scroll-Bindings) plugin can bind an upper pen button to **Pen Scroll**. Enable its **Scroll Binding Filter**, choose a zero pressure threshold for hover scrolling, and enable Freeze Cursor. This output mode reanchors after the upper button changes state so relative movement resumes without a cursor jump. Kinetic scrolling can be disabled for an immediate stop on release. Enable **Upper pen button tap = middle click** in the companion app for a stationary hover press/release within 500 ms to open links as a middle click. More than 0.3 mm of travel cancels the click, including a scroll that returns to its starting point. The option defaults to off and requires the upper button to remain assigned to Pen Scroll with Scroll Binding Filter enabled.

## Build and checks

Requires the .NET 10 SDK:

```sh
dotnet build plugin/PthTouch.csproj -c Release
dotnet run --project checks/Checks.csproj -c Release
```

The plugin DLL is `plugin/bin/Release/net10.0/PthTouch.dll`. Place only that DLL at the top level of the distribution ZIP. Do not bundle OpenTabletDriver assemblies.

Checks also cover deferred upper-button middle taps and cancellation, settings merging/preservation with concurrent edits, local RPC notification handling, all four system swipe directions, asynchronous finger touchdown, pinch/scroll locking, magnification lifecycle/cancellation and correct Control+Function+Arrow event flags. The macOS-only check constructs magnify envelopes and verifies their NSEvent type/phase/value without posting UI events.

Checks exercise touch transitions, stationary contacts, scroll/tap separation, left/right tap dispatch, native double-click sequencing, tap-and-drag and release/cancellation, pen proximity suppression, frozen pen-scroll transitions, spatial hover hysteresis, far-hover release preservation and re-entry without a cursor jump. They use fake pointers/click sinks and do not generate native UI events, so they also run on Linux CI.

## Limitations

No rotation, momentum scrolling or drag lock. Three-finger actions use discrete keyboard shortcuts; native pinch injection is experimental. Near-proximity limiting only acts on devices that report the Wacom proximity flag; it is not a general interference filter. The pointer pauses in the far, unreliable hover range by design.

## License

MIT. OpenTabletDriver is a separate project with its own license. This plugin is not affiliated with Wacom.
