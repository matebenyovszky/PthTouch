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
- Optional three-finger swipes switch Spaces/full-screen apps horizontally, open Mission Control upward, and show the current application's windows downward. By default these use system shortcuts; experimental horizontal transitions can instead follow the fingers.
- Optional experimental two-finger pinch sends native magnification events to compatible apps, separate from two-finger scrolling.
- Optional velocity-based sensitivity for pen hover and one-finger touch, with independent switches and configurable slow/fast multipliers.
- Optional general pen noise filtering covers tip contact, held buttons and dragging as well as hover and pen scrolling.
- Optional near-proximity gating suppresses unreliable far-hover coordinates while preserving pen/button releases. Re-entry establishes a fresh relative origin.
- Optional three-report median spike rejection removes isolated hover outliers before the deadband. It delays small continuous hover movement by one report (about 5 ms at 200 Hz); movements larger than four times the deadband bypass it immediately. Contact and side buttons normally bypass both filters unless general pen filtering is enabled. The option defaults to off; pen scrolling can opt into a separate filter below.
- An opt-in spatial hover deadband suppresses tiny stationary jitter without temporal averaging. Pen pressure and side-button use normally bypass it; the setting defaults to 0 mm (off). For stationary PTH-851 noise, a 0.10 mm deadband with spike rejection worked well in a 30-second replay at 20 px/mm; results depend on hardware and environment. Without spike rejection, a 0.04 mm threshold corresponds to 0.8 px at 20 px/mm.

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
with this output mode selected. The settings window can be resized; growing it
shows more controls, while smaller windows keep the forms scrollable.

The Pen, Touch & gestures, and Stability tabs expose sensitivity, pen-scroll
mode/speed/direction, short-press middle clicking, tap/drag, scrolling, swipes,
pinch, and hover/pen-scroll noise filtering. **Save & apply** reads the current driver settings,
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

## Interactive desktop switching (experimental)

Enable **Interactive desktop swipes** together with three-finger system swipes
to let horizontal desktop transitions follow the three-finger centroid. The
separate **Desktop swipe travel** setting controls how many physical mm map to
a full transition (default 60 mm; supported range 20–160 mm). Reverse toward the
start to roll the transition back, or lift to settle. Each contact session is
limited to one neighbouring desktop. Up/down gestures keep their normal system
shortcuts.

The implementation sends native DockControl progress events on macOS 13–26.
It is off by default and uses undocumented system event fields. On macOS 27+
the mode falls back to discrete shortcuts because that native event transport
changed. Contact loss has a short grace period; invalid IDs, palm contacts,
stale data, pen takeover and unloading cancel an in-flight animation.

## Optional pen-button scrolling

The separate [Scroll Bindings](https://github.com/Mrcubix/Scroll-Bindings) plugin can bind an upper pen button to **Pen Scroll**. Enable its **Scroll Binding Filter**, choose a zero pressure threshold for hover scrolling, and enable Freeze Cursor. This output mode reanchors after the upper button changes state so relative movement resumes without a cursor jump. Kinetic scrolling can be disabled for an immediate stop on release. Enable **Upper pen button tap = middle click** in the companion app for a stationary hover press/release within 500 ms to open links as a middle click. More than 0.3 mm of travel cancels the click, including a scroll that returns to its starting point. The middle-click option defaults to off and requires either this Pen Scroll binding with Scroll Binding Filter or the built-in joystick mode below.

### Joystick autoscroll

Choose **Joystick autoscroll** in the companion app's **Pen → Scrolling mode**.
The upper button's press position becomes the centre of a virtual rectangle.
Moving away selects scrolling speed rather than a finite scroll distance;
holding the pen at an edge keeps scrolling. Returning to the centre stops it,
and releasing the button stops scrolling and reanchors relative pointing.
A short stationary press/release can still send a middle click.

Horizontal and vertical reach are measured **from the centre**. The default
20 mm each way makes a 40 × 40 mm field. A 1 mm centre deadzone and a quadratic
acceleration curve give fine control near the centre; base speeds default
to 600 px/s horizontally and 1200 px/s vertically. Reach, maximum speed,
deadzone, acceleration and direction are adjustable. Outside the field, speed
stays capped. The optional **Edge speed boost** (1–10×, default 1×) smoothly
increases speed over the outer 35% of travel after the centre deadzone; final
edge speed is the configured base speed multiplied by that boost. The base-speed
range is 0–100,000 px/s. **Response curve** accepts 1–12: larger values keep the
inner field slower and do not change the final edge speed. No cursor overlay is drawn.

This mode is built into PthTouch and does not require Scroll Bindings. An
existing upper-button Scroll Bindings assignment is suppressed while joystick
mode is selected and remains available when switching back to **Drag scroll**.
Joystick mode is off by default. Disconnects, release, stale device reports
and unloading stop wheel output; continuous autoscroll requires the tablet to
keep sending reports while stationary, as the tested PTH-851 does.

### Filtering pen-scroll noise

Enable **Apply noise filtering while pen-scrolling** on the **Stability** tab.
This separate filter uses the configured hover deadband and median-spike
rejection for upper-button drag scrolling and joystick scrolling. It filters
coordinates before either scroll handler, while preserving button transitions
and the middle-click recognizer's raw movement test. It is off by default.
The existing 0.10 mm deadband and spike rejection are a useful starting point.
Large movements pass immediately; small continuous motion may wait one report.

### General pen noise filtering

Enable **Filter all pen movement (including dragging)** on the **Stability** tab
to apply the same deadband and optional median-spike filter to tip-down motion,
held normal buttons, scrollbar/window dragging, hover and pen scrolling. This
uses a single coordinate filter; the scroll-only filter is bypassed so two
median windows never stack. It defaults to off to preserve unfiltered drawing
behaviour for existing users. Pressure and button values continue through the
normal pipeline; zero deadband disables coordinate filtering.

## Optional velocity-based pointer sensitivity

The **Pen → Velocity-based sensitivity** section has independent switches for
pen hover and one-finger touch. Both default to off. Slow and fast multipliers
scale the ordinary X/Y sensitivity; defaults are 0.90× and 1.35×, with full gain
at 150 mm/s. Slow multiplier accepts 0.2–2×, fast multiplier up to 5× (at least
the slow value), and the transition speed accepts 10–1000 mm/s.

The curve is continuous and direction-preserving. Only the speed estimate is
smoothed (8 ms time constant); coordinates are emitted immediately, with no
queued motion or drift after stopping. The calculation uses physical mm/s,
independent of pointer sensitivity and report rate. Pen contact, side buttons,
touch dragging and scrolling keep fixed sensitivity. General jitter filtering
is a separate option and can still apply during these actions. Turning both
velocity switches off restores the original fixed-sensitivity behaviour.

## Experimental pen-pressure gestures

The **Pen → Pressure gestures** section has independent switches; all default
to off. The copy, double-pressure paste and dictation features below use ordinary
macOS shortcuts through the host's existing permission, without reading clipboard
contents or the app's actual selection.

- **Copy at selection end:** drag with the tip, squeeze harder at the end, then
  lift. A selection-like drag must travel at least 1.5 mm (adjustable 0.5–10 mm).
  The recognizer learns normal contact pressure and requires a sustained rise.
  Extra pressure defaults to 8 percentage points of the sensor range
  (adjustable 5–50), adapting to remaining sensor headroom; a peak must reach
  at least 25% total pressure. The moving-pressure reference is frozen once the drag stops, so gradual deliberate squeezes remain detectable. Command+C is sent after tip/mouse-up is flushed.
  Continuing to drag more than 0.8 mm after the peak cancels that request.
- **Stationary double-pressure paste:** lightly touch down, squeeze twice,
  partially relaxing between peaks without lifting, then lift. Peaks must fit within the
  system double-click interval and stay within 0.8 mm. A single long hold,
  separate tip taps and dragging do not trigger Paste. Command+V follows
  tip/mouse-up. An existing app selection can be replaced, as with normal Paste;
  the condition is the stationary gesture, not semantic selection detection.
- **Upper button + strong pressure:** hold the middle/scroll pen button and
  press firmly to send one dictation shortcut. Release the button before
  repeating. Threshold defaults to 60% (adjustable 20–95%). A strong-pressure
  gesture pauses scrolling and freezes the cursor; ordinary light-pressure
  scrolling remains available. Choose **MacParakeet (Fn shortcut)** or
  **macOS (Fn+D)**. Configure that shortcut in the selected app beforehand;
  MacParakeet must be running. For MacParakeet, Fn stays held until the pen button is released. Keep the pen within sensing range; proximity loss and unloading also release Fn. macOS uses the Fn+D toggle instead.

Pressure peaks are debounced for 40 ms; clipboard gestures learn a normal
contact baseline for 80 ms. Side buttons, eraser use, proximity loss, stale
input, unloading and disabling cancel pending clipboard gestures. These are
pen gestures, separate from native Apple Force Click. Actual pressure sensitivity
and application shortcut behaviour require a hardware trial.

## Experimental eraser and pressure-flick gestures

The companion app's **Pen → Eraser and flick gestures** section exposes separate
switches, all off by default. This release remains macOS-only; the pure gesture
recognizers are portable C#, but native output, app launching, Accessibility
integration and the SwiftUI companion require a Windows implementation.

- **Delete after eraser contact:** touch or drag with the eraser end, then lift
  to send one ordinary Mac Delete (Backspace, key 51). There is no selection,
  editability or text-field-focus query: selected text is deleted, or the preceding
  character may be deleted when there is no selection. Other apps apply their
  normal Delete behavior. Hover never triggers it. Contact must reach 1% sensor
  pressure and last 30 ms; stale input, side buttons, tool changes and disabling
  cancel it. A timely proximity-loss report can finish a contact without a final
  zero-pressure packet. The new setting defaults off and does not inherit the
  former guarded eraser setting. Eraser contact cannot arm tip clipboard actions.
- **Right flick:** strong pressure held for 40 ms arms the gesture. Holding that
  pressure keeps it ready until the flick begins; after easing pressure the
  response window is 650 ms. Move
  quickly right while changing tilt to send Command+Z. The default requirements
  are 65% pressure, 6 mm horizontal travel, 100 mm/s average motion speed and a
  tilt-vector change of 12% of the raw sensor range. Travel must dominate vertical
  movement and finish within 180 ms of starting fast horizontal motion. Slow
  squeeze drift does not start that timer. Each threshold is adjustable;
  tilt units are sensor fractions, not degrees. Directions refer to the physical
  tablet, with an optional **Reverse flick directions** switch. Devices without
  tilt reports cannot trigger the action.
- **Left flick:** opens the selected desktop app or HTTPS page in the default
  browser. Presets are ChatGPT/Codex (`com.openai.codex`), ChatGPT
  (`com.openai.chat`), Claude, ChatGPT in browser and Claude in browser. The
  corresponding desktop app must already be installed. No app is installed by
  the gesture.

AI opening does not read or transfer selected text, inspect the AI composer,
or change the clipboard. Native actions use NSWorkspace foreground-app identity
and ordinary shortcuts/open targets. There is no Accessibility text inspection,
renderer-tree activation or selection retry path. The companion removes the
obsolete selection-transfer and guarded-eraser settings on save.

Recognized flicks release the pen tip before dispatching the action, suppress
additional tip events until physical release, and cancel competing copy/paste.
Side buttons, tool changes, stale reports and unloading cancel recognition.
Already recognized commands can finish after the pen leaves proximity; unloading
cancels pending native work. The foreground app is checked again before delayed
shortcuts. Debug diagnostics record recognition, skipped-action reasons and
contact pressure/tilt summaries, without selection text or clipboard content.
Hardware behavior and application shortcut handling still need a manual trial.
A local replay of ten recorded deliberate flicks recognized all ten in the actual
filtered C# output mode after fixing premature timers; the recorded light control
movements did not trigger flicks. That is one recording, not a guarantee against
false gestures during other real-world use.

### Touch recovery

The pen and touch use separate USB interfaces. If pen input works but no touch
reports arrive even with the pen far away and the physical touch switch on,
unplugging and reconnecting the tablet can restore the touch interface. This
was observed on the PTH-851; the underlying intermittent USB/firmware cause is
not established. The plugin cannot synthesize missing touch reports, and does
not repeatedly reset USB devices or weaken pen palm rejection to hide the issue.

## Build and checks

Requires the .NET 10 SDK:

```sh
dotnet build plugin/PthTouch.csproj -c Release
dotnet run --project checks/Checks.csproj -c Release
```

The plugin DLL is `plugin/bin/Release/net10.0/PthTouch.dll`. Place only that DLL at the top level of the distribution ZIP. Do not bundle OpenTabletDriver assemblies.

Checks also cover joystick velocity integration and bounds, stationary edge scrolling, release/re-entry, filtered scroll coordinates, interactive desktop progress and contact-dropout recovery, deferred upper-button middle taps and cancellation, settings merging/preservation with concurrent edits, local RPC notification handling, all four system swipe directions, asynchronous finger touchdown, pinch/scroll locking, magnification lifecycle/cancellation and correct Control+Function+Arrow event flags. The macOS-only check constructs magnify envelopes and verifies their NSEvent type/phase/value without posting UI events.

Checks exercise touch transitions, stationary contacts, scroll/tap separation, left/right tap dispatch, native double-click sequencing, tap-and-drag and release/cancellation, pen proximity suppression, frozen pen-scroll transitions, spatial hover hysteresis, far-hover release preservation and re-entry without a cursor jump. They use fake pointers/click sinks and do not generate native UI events, so they also run on Linux CI.

## Limitations

No rotation, momentum scrolling or drag lock. Three-finger actions normally use discrete keyboard shortcuts, with opt-in interactive horizontal transitions on macOS 13–26; native pinch injection is experimental. Near-proximity limiting only acts on devices that report the Wacom proximity flag; it is not a general interference filter. The pointer pauses in the far, unreliable hover range by design.

## License

MIT. OpenTabletDriver is a separate project with its own license. This plugin is not affiliated with Wacom.
