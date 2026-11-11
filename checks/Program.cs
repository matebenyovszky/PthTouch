using System.Numerics;
using PthTouch;
using OpenTabletDriver.Plugin.Tablet.Touch;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++;
}
Motion Update(TouchMotion state, double time, params Contact[] contacts) =>
    state.Update(contacts, time, Vector2.One, new Vector2(15), 12);
Contact Finger(byte id, float x, float y) => new(id, new Vector2(x, y));

var engine = new TouchMotion();
Check(Update(engine, 0, Finger(0, 100, 100)) == default, "Touch-down must not jump");
var one = Update(engine, .01, Finger(0, 101, 102));
Check(one.Cursor == new Vector2(15, 30) && one.Scroll == Vector2.Zero,
    "One finger must only move the cursor");
Check(Update(engine, .02, Finger(0, 101, 102)) == default,
    "A stationary finger must not move or scroll");
Check(Update(engine, .03, Finger(0, 101, 102), Finger(1, 110, 110)) == default,
    "Adding a second finger must not jump or scroll");
var two = Update(engine, .04, Finger(0, 101, 104), Finger(1, 110, 112));
Check(two.Cursor == Vector2.Zero && two.Scroll == new Vector2(0, 24),
    "Two fingers must only scroll by centroid displacement");
Check(Update(engine, .05, Finger(1, 110, 112), Finger(0, 101, 104)) == default,
    "Contact ordering must not produce output");
Check(Update(engine, .06, Finger(0, 101, 104)) == default,
    "Lifting one scrolling finger must not jump the cursor");
Check(Update(engine, .07, Finger(0, 103, 105)) == default,
    "A remaining scrolling finger must wait for a full lift");
Check(Update(engine, .08) == default, "Lifting all fingers must stop output");
Check(Update(engine, .09, Finger(0, 120, 120)) == default,
    "New touch after scrolling must establish a fresh anchor");
Check(Update(engine, .10, Finger(0, 121, 120)).Cursor == new Vector2(15, 0),
    "One-finger movement must resume after full lift");
Check(Update(engine, .11, Finger(0, 200, 200)) == default,
    "Implausible coordinate jumps must be ignored");
Check(Update(engine, .5, Finger(0, 201, 201)) == default,
    "Stale contact positions must not generate motion");
Check(engine.Update([Finger(0, 202, 202)], .51, Vector2.One,
    new Vector2(15), 12, penNearby: true) == default,
    "Touch must be suppressed near the pen");
Check(Update(engine, .52, Finger(0, 203, 203), Finger(1, 210, 210),
    Finger(2, 215, 215)) == default, "Palm/three-finger input must be ignored");

// Exercise the actual output mode against OTD's pointer interfaces as well.
var pointer = new RecordingPointer();
var clicker = new RecordingClicker();
var mode = new TrackpadRelativeMode { Pointer = pointer, Clicker = clicker, Sensitivity = new Vector2(15) };
void Feed(params TouchPoint[] points) => mode.Read(new TouchReport(points));
TouchPoint Point(byte id, float x, float y) => new() { TouchID = id, Position = new(x, y) };
Feed(Point(0, 100, 100));
Feed(Point(0, 110, 100));
Check(pointer.Moves == 1 && pointer.Scrolls == 0,
    "The output mode must send only cursor motion for one finger");
Feed(Point(0, 110, 100), Point(1, 200, 100));
Feed(Point(0, 110, 130), Point(1, 200, 130));
Check(pointer.Moves == 1 && pointer.Scrolls > 0,
    "The output mode must scroll without moving the pointer for two fingers");
int scrolls = pointer.Scrolls;
Feed(Point(0, 110, 130), Point(1, 200, 130));
Check(pointer.Scrolls == scrolls,
    "Stationary two-finger contact must not keep scrolling");
Feed(Point(0, 110, 130));
Feed(Point(0, 120, 130));
Check(pointer.Moves == 1 && pointer.Scrolls == scrolls,
    "A finger remaining after scrolling must produce no output");
Feed();
Feed(Point(0, 300, 300));
Feed(Point(0, 310, 300));
Check(pointer.Moves == 2 && pointer.Scrolls == scrolls,
    "Lifting all fingers must restore normal one-finger pointer motion");

var taps = new TouchMotion();
Update(taps, 1, Finger(0, 100, 100));
Check(Update(taps, 1.08).Tap == TapButton.Left, "A single tap must left-click on release");
Update(taps, 1.15, Finger(0, 100, 100));
Check(Update(taps, 1.23).Tap == TapButton.Left, "A second quick tap must produce a second left click");
Update(taps, 2, Finger(0, 100, 100));
Check(Update(taps, 2.5).Tap == TapButton.None, "A long hold must not click");
Update(taps, 3, Finger(0, 100, 100));
Update(taps, 3.05, Finger(0, 102, 100));
Check(Update(taps, 3.10).Tap == TapButton.None, "Pointer movement must not turn into a tap");
Update(taps, 4, Finger(0, 100, 100));
Check(Update(taps, 4.02, Finger(0, 100, 100), Finger(1, 110, 100)) == default,
    "Adding a second tapping finger must produce no output");
Check(Update(taps, 4.04, Finger(0, 100.1f, 100.1f), Finger(1, 110.1f, 100.1f)) == default,
    "Two-finger tap jitter must not initiate scrolling");
Update(taps, 4.06, Finger(0, 100.1f, 100.1f));
Check(Update(taps, 4.08).Tap == TapButton.Right, "A staggered two-finger tap must right-click once");
Update(taps, 5, Finger(0, 100, 100), Finger(1, 110, 100));
Update(taps, 5.03, Finger(0, 100, 102), Finger(1, 110, 102));
Check(Update(taps, 5.06).Tap == TapButton.None, "A short scroll must never become a right click");
Update(taps, 6, Finger(0, 100, 100), Finger(1, 110, 100), Finger(2, 120, 100));
Check(Update(taps, 6.08).Tap == TapButton.None, "A palm must not create a click");
taps.Update([Finger(0, 100, 100)], 7, Vector2.One, new(15), 12, penNearby: true);
Update(taps, 7.02, Finger(0, 100, 100));
Check(Update(taps, 7.08).Tap == TapButton.None, "Touch that began near the pen must not click on release");

var sequence = new ClickSequence();
Check(sequence.Next(TapButton.Left, 1, Vector2.Zero, .5) == 1, "First native click count must be one");
Check(sequence.Next(TapButton.Left, 1.2, Vector2.Zero, .5) == 2, "Two taps must create native double-click count two");
Check(sequence.Next(TapButton.Left, 2, Vector2.Zero, .5) == 1, "Expired double-click sequence must reset");
Check(sequence.Next(TapButton.Left, 2.1, new(20, 0), .5) == 1, "Moving between taps must reset double-click count");
Check(sequence.Next(TapButton.Right, 2.2, new(20, 0), .5) == 1, "Right click must use count one");

var hoverPointer = new RecordingPointer();
var hoverMode = new TrackpadRelativeMode
{
    Pointer = hoverPointer, Clicker = clicker, Sensitivity = new(20),
    ResetTime = TimeSpan.FromMilliseconds(100)
};
hoverMode.Read(new PenReport(new(1000, 1000), true));
hoverMode.Read(new PenReport(new(1001, 1000), true));
int nearMoves = hoverPointer.Moves;
hoverMode.Read(new PenReport(new(5000, 5000), false));
Check(hoverPointer.LastPosition == Vector2.Zero, "Far-hover noise must not move the cursor");
hoverMode.Read(new PenReport(new(6000, 6000), false));
Check(hoverPointer.LastPosition == Vector2.Zero, "Repeated far-hover noise must remain suppressed");
hoverMode.Read(new PenReport(new(1100, 1100), true));
Check(hoverPointer.LastPosition == Vector2.Zero, "Returning from far hover must re-anchor without a jump");
hoverMode.Read(new PenReport(new(1101, 1100), true));
Check(Vector2.Distance(hoverPointer.LastPosition, new(.2f, 0)) < .001,
    "Normal pen sensitivity must resume after hover re-entry");
int beforeContact = hoverPointer.Moves;
hoverMode.Read(new PenReport(new(1102, 1100), false) { Pressure = 1 });
Check(hoverPointer.Moves == beforeContact + 1, "Pen contact must bypass far-hover suppression");
bool sawRelease = false;
hoverMode.Emit += report =>
{
    if (report is PenReport { NearProximity: false, Pressure: 0 }) sawRelease = true;
};
hoverMode.Read(new PenReport(new(7000, 7000), false));
Check(sawRelease && hoverPointer.LastPosition == Vector2.Zero,
    "Far-hover filtering must preserve pen/button releases without cursor movement");

// Click output tests use a fake sink and never post native UI events.
var tapPointer = new RecordingPointer();
var sink = new RecordingClicker();
var tapMode = new TrackpadRelativeMode { Pointer = tapPointer, Clicker = sink, Sensitivity = new(20) };
tapMode.Read(new TouchReport([Point(0, 100, 100)]));
tapMode.Read(new TouchReport([]));
tapMode.Read(new TouchReport([Point(0, 100, 100)]));
tapMode.Read(new TouchReport([]));
Check(sink.Buttons.SequenceEqual(new[] { TapButton.Left, TapButton.Left }),
    "Two actual output tap cycles must dispatch two left clicks");
tapMode.Read(new TouchReport([Point(0, 100, 100), Point(1, 200, 100)]));
tapMode.Read(new TouchReport([]));
Check(sink.Buttons.Last() == TapButton.Right, "The actual output mode must dispatch a right click for two fingers");
tapMode.TapToClick = false;
int clicksBeforeDisable = sink.Buttons.Count;
tapMode.Read(new TouchReport([Point(0, 100, 100)]));
tapMode.Read(new TouchReport([]));
Check(sink.Buttons.Count == clicksBeforeDisable, "Disabling tap-to-click must suppress click dispatch");

var drag = new TouchMotion();
Update(drag, 10, Finger(0, 100, 100));
Check(Update(drag, 10.05).Tap == TapButton.Left, "Tap-and-drag must begin with an ordinary tap");
Check(Update(drag, 10.08) == default, "Idle packets must not create events or disarm dragging");
Update(drag, 10.10, Finger(0, 100, 100));
Check(Update(drag, 10.11, Finger(0, 100.2f, 100)).Cursor == Vector2.Zero,
    "A possible second tap must not move before becoming a drag");
var startDrag = Update(drag, 10.12, Finger(0, 102, 100));
Check(startDrag.Drag == DragChange.Start && Vector2.Distance(startDrag.Cursor, new(30, 0)) < .001,
    "The second contact must start dragging after movement and preserve pending displacement");
var continueDrag = Update(drag, 10.13, Finger(0, 103, 100));
Check(continueDrag.Cursor == new Vector2(15, 0) && continueDrag.Scroll == Vector2.Zero,
    "A held drag must keep moving without scrolling");
var endDrag = Update(drag, 10.14);
Check(endDrag.Drag == DragChange.End && endDrag.Tap == TapButton.None,
    "Lifting a drag must release without another click");
Update(drag, 11, Finger(0, 100, 100)); Update(drag, 11.05);
Update(drag, 11.10, Finger(0, 100, 100));
Check(Update(drag, 11.27, Finger(0, 100, 100)).Drag == DragChange.Start,
    "Holding the second contact must arm a drag without movement");
Check(Update(drag, 11.28, Finger(0, 100, 100), Finger(1, 110, 100)).Drag == DragChange.End,
    "Adding a finger during dragging must release the drag");
Check(Update(drag, 11.29).Tap == TapButton.None, "Cancelling a drag must not right-click");
Update(drag, 12, Finger(0, 100, 100)); Update(drag, 12.05);
Update(drag, 12.10, Finger(0, 100, 100)); Update(drag, 12.12, Finger(0, 102, 100));
Check(drag.Update([Finger(0, 102, 100)], 12.13, Vector2.One, new(15), 12,
    penNearby: true).Drag == DragChange.End, "Pen takeover must release an active drag");
Update(drag, 13, Finger(0, 100, 100)); Update(drag, 13.05);
Update(drag, 14, Finger(0, 100, 100));
Check(Update(drag, 14.02, Finger(0, 102, 100)).Drag == DragChange.None,
    "A late second contact must remain ordinary pointer movement");

var dragSink = new RecordingClicker();
var dragPointer = new RecordingPointer();
var dragMode = new TrackpadRelativeMode { Pointer = dragPointer, Clicker = dragSink, Sensitivity = new(20) };
dragMode.Read(new TouchReport([Point(0, 100, 100)]));
dragMode.Read(new TouchReport([]));
dragMode.Read(new TouchReport([]));
dragMode.Read(new TouchReport([Point(0, 100, 100)]));
dragMode.Read(new TouchReport([Point(0, 130, 100)]));
Check(dragSink.DragStarts == 1 && dragSink.DragMoves == 1 && dragPointer.Moves == 0,
    "Actual drag output must use native drag events rather than ordinary mouse movement");
dragMode.Read(new TouchReport([]));
Check(dragSink.DragEnds == 1 && dragSink.Buttons.Count == 1,
    "Actual drag release must emit one mouse-up and no extra tap");
dragMode.Read(new TouchReport([Point(0, 100, 100)]));
dragMode.Read(new TouchReport([]));
dragMode.Read(new TouchReport([Point(0, 100, 100)]));
dragMode.Read(new TouchReport([Point(0, 130, 100)]));
int endsBeforeDispose = dragSink.DragEnds;
dragMode.Dispose();
Check(dragSink.DragEnds == endsBeforeDispose + 1,
    "Switching/unloading the output mode must release an active drag");

var scrollPointer = new RecordingPointer();
var scrollMode = new TrackpadRelativeMode
{
    Pointer = scrollPointer, Clicker = sink, Sensitivity = new(20),
    ResetTime = TimeSpan.FromMilliseconds(100),
    Elements = [new ScrollBinding.ScrollBindingFilter()]
};
PenReport ScrollPen(float x, bool pressed) => new(new(x, 1000), true)
    { PenButtons = [false, pressed] };
scrollMode.Read(ScrollPen(1000, false));
scrollMode.Read(ScrollPen(1001, true));
scrollMode.Read(ScrollPen(1400, true));
Check(scrollPointer.LastPosition == Vector2.Zero, "Starting frozen pen scroll must not jump");
scrollMode.Read(ScrollPen(1600, true));
Check(scrollPointer.LastPosition == Vector2.Zero, "Frozen pen scroll must keep the cursor in place");
scrollMode.Read(ScrollPen(1600, false));
Check(scrollPointer.LastPosition == Vector2.Zero, "Releasing pen scroll must not move the cursor");
scrollMode.Read(ScrollPen(1601, false));
Check(scrollPointer.LastPosition == Vector2.Zero, "The first unfrozen report must establish a new origin");
scrollMode.Read(ScrollPen(1602, false));
Check(Vector2.Distance(scrollPointer.LastPosition, new(.2f, 0)) < .001,
    "Ordinary relative motion must resume from the new pen position");

var deadband = new HoverDeadband();
Vector2 rawScale = new(.005f);
Check(deadband.Filter(new(100, 100), rawScale, .04f, true) == new Vector2(100),
    "Hover deadband must anchor the first report");
Check(deadband.Filter(new(104, 98), rawScale, .04f, true) == new Vector2(100),
    "Subpixel hover jitter must stay at the anchor");
Check(deadband.Filter(new(109, 100), rawScale, .04f, true) == new Vector2(109, 100),
    "Intentional motion must pass its full displacement immediately");
Check(deadband.Filter(new(1000, 100), rawScale, .04f, true) == new Vector2(1000, 100),
    "Fast movement must not be averaged or delayed");
Check(deadband.Filter(new(1001, 100), rawScale, .04f, false) == new Vector2(1001, 100),
    "Contact/button use must bypass hover filtering");
Check(deadband.Filter(new(1002, 100), rawScale, 0, true) == new Vector2(1002, 100),
    "Zero deadband must preserve exact input");
deadband.Reset();
Check(deadband.Filter(new(5000, 5000), rawScale, .04f, true) == new Vector2(5000),
    "Re-entry must not reuse an old hover anchor");
var jitterPointer = new RecordingPointer();
var jitterMode = new TrackpadRelativeMode
{
    Pointer = jitterPointer, Clicker = sink, Sensitivity = new(20),
    ResetTime = TimeSpan.FromMilliseconds(100), HoverJitterDeadband = .04f
};
jitterMode.Read(new PenReport(new(1000, 1000), true));
jitterMode.Read(new PenReport(new(1004, 998), true));
Check(jitterPointer.LastPosition == Vector2.Zero, "The actual mode must suppress small hover displacement");
jitterMode.Read(new PenReport(new(1010, 1000), true));
Check(jitterPointer.LastPosition.X > 0, "The actual mode must immediately resume deliberate hover motion");
jitterMode.Read(new PenReport(new(1011, 1000), true) { Pressure = 1 });
Check(jitterPointer.LastPosition.X > 0, "The actual mode must not filter pen contact movement");

var spikes = new HoverDeadband();
Vector2 Spike(float x, bool hovering = true) => spikes.Filter(new(x, 1000), rawScale, .1f, hovering, true);
Check(Spike(1000) == new Vector2(1000), "Spike filtering must anchor the first sample");
Check(Spike(1030) == new Vector2(1000), "An isolated small hover spike must be rejected");
Check(Spike(1000) == new Vector2(1000), "A rejected spike must not rebound on the next report");
Check(Spike(1000) == new Vector2(1000), "A rejected spike must leave no delayed movement");
Check(Spike(1500) == new Vector2(1500, 1000), "Large intentional motion must bypass the window immediately");
Check(Spike(1500) == new Vector2(1500, 1000), "Fast motion must not replay stale history when stopping");
Check(Spike(1501, false) == new Vector2(1501, 1000), "Contact must bypass both filters immediately");
spikes.Reset(); Spike(1000);
Check(Spike(1025) == new Vector2(1000), "Small deliberate motion may wait one report");
Check(Spike(1030) == new Vector2(1025, 1000), "Small deliberate motion must resume on the next report");
spikes.Reset();
Check(Spike(3000) == new Vector2(3000, 1000), "Re-entry must discard median history");

Motion Gesture(TouchMotion state, double time, params Contact[] contacts) =>
    state.Update(contacts, time, Vector2.One, new(20), 12,
        threeFingerSwipes: true, pinchZoom: true);
Contact[] Three(float x, float y) => [Finger(0, x, y), Finger(1, x + 10, y), Finger(2, x + 20, y)];
foreach (var (delta, direction) in new[] {
    (new Vector2(-7, 0), SwipeDirection.Left), (new Vector2(7, 0), SwipeDirection.Right),
    (new Vector2(0, -7), SwipeDirection.Up), (new Vector2(0, 7), SwipeDirection.Down) })
{
    var swipe = new TouchMotion();
    Check(Gesture(swipe, 0, Three(100, 100)) == default, "Three-finger touchdown must not act");
    var action = Gesture(swipe, .01, Three(100 + delta.X, 100 + delta.Y));
    Check(action.Swipe == direction && action.Cursor == Vector2.Zero && action.Scroll == Vector2.Zero,
        "Each three-finger direction must emit its action without cursor movement or scroll");
    Check(Gesture(swipe, .02, Three(100 + delta.X * 2, 100 + delta.Y * 2)) == default,
        "A three-finger session must fire only once");
    Check(Gesture(swipe, .03, Finger(0, 100, 100), Finger(1, 110, 100)) == default,
        "Partially lifting a swipe must not turn it into scrolling");
    Check(Gesture(swipe, .04, Finger(0, 110, 110)) == default,
        "A remaining swipe finger must not move the pointer");
    Check(Gesture(swipe, .05) == default, "Ending a swipe must not click");
}
var coherent = new TouchMotion();
Gesture(coherent, 0, Three(100, 100));
Check(Gesture(coherent, .01, Finger(0, 124, 100), Finger(1, 110, 100), Finger(2, 120, 100)).Swipe == SwipeDirection.None,
    "One moving finger plus two stationary fingers must not trigger a swipe");
var palm = new TouchMotion();
Gesture(palm, 0, Finger(0, 100, 100), Finger(1, 110, 100), Finger(2, 120, 100), Finger(3, 130, 100));
Check(Gesture(palm, .01, Three(110, 100)) == default && Gesture(palm, .02, Three(120, 100)) == default,
    "A palm reduced to three contacts must remain suppressed until a full lift");
var diagonal = new TouchMotion();
Gesture(diagonal, 0, Three(100, 100));
Check(Gesture(diagonal, .01, Three(107, 107)).Swipe == SwipeDirection.None,
    "Ambiguous diagonal swipes must wait for a dominant direction");

var pinch = new TouchMotion();
Gesture(pinch, 0, Finger(0, 100, 100), Finger(1, 120, 100));
var startPinch = Gesture(pinch, .01, Finger(0, 99, 100), Finger(1, 121, 100));
Check(startPinch.Magnify == MagnifyPhase.Began && MathF.Abs(startPinch.Magnification - .1f) < .001 &&
    startPinch.Scroll == Vector2.Zero && startPinch.Cursor == Vector2.Zero,
    "Finger spreading must begin a real magnification without scroll or cursor output");
var zoomOut = Gesture(pinch, .02, Finger(1, 120, 100), Finger(0, 100, 100));
Check(zoomOut.Magnify == MagnifyPhase.Changed && zoomOut.Magnification < 0,
    "Pinching back together must zoom out even if contact order changes");
Check(Gesture(pinch, .03, Finger(0, 100, 103), Finger(1, 120, 103)).Scroll == Vector2.Zero,
    "A classified pinch must never turn into scrolling");
Check(Gesture(pinch, .04).Magnify == MagnifyPhase.Ended,
    "Lifting a pinch must emit exactly one ended phase");
Check(Gesture(pinch, .05) == default, "Idle reports must not repeat the pinch end");
Gesture(pinch, 1, Finger(0, 100, 100), Finger(1, 120, 100));
Gesture(pinch, 1.01, Finger(0, 99, 100), Finger(1, 121, 100));
Check(Gesture(pinch, 1.02, Finger(0, 99, 100)).Magnify == MagnifyPhase.Cancelled,
    "Partial pinch lift must cancel magnification");
Check(Gesture(pinch, 1.03, Finger(0, 110, 110)) == default,
    "A remaining pinch finger must stay suppressed");
Gesture(pinch, 1.04);
Gesture(pinch, 2, Finger(0, 100, 100), Finger(1, 120, 100));
Gesture(pinch, 2.01, Finger(0, 99, 100), Finger(1, 121, 100));
Check(pinch.Update([Finger(0, 99, 100), Finger(1, 121, 100)], 2.02, Vector2.One, new(20), 12,
    penNearby: true, threeFingerSwipes: true, pinchZoom: true).Magnify == MagnifyPhase.Cancelled,
    "Pen takeover must cancel a live magnification");
var scrollLock = new TouchMotion();
Gesture(scrollLock, 0, Finger(0, 100, 100), Finger(1, 120, 100));
Check(Gesture(scrollLock, .01, Finger(0, 100, 102), Finger(1, 120, 102)).Scroll.Y > 0,
    "Two fingers travelling together must preserve ordinary scrolling");
Check(Gesture(scrollLock, .02, Finger(0, 98, 102), Finger(1, 122, 102)).Magnify == MagnifyPhase.None,
    "A classified scroll must never turn into zoom midway through the contact");
var pinchTap = new TouchMotion();
Gesture(pinchTap, 0, Finger(0, 100, 100), Finger(1, 120, 100));
Check(Gesture(pinchTap, .05).Tap == TapButton.Right,
    "Enabling pinch must preserve a stationary two-finger right tap");
var pinchSink = new RecordingClicker();
var pinchPointer = new RecordingPointer();
var pinchMode = new TrackpadRelativeMode { Pointer = pinchPointer, Clicker = pinchSink,
    Sensitivity = new(20), ThreeFingerSwipes = true, PinchZoom = true };
pinchMode.Read(new TouchReport([Point(0, 1000, 1000), Point(1, 2000, 1000)]));
pinchMode.Read(new TouchReport([Point(0, 980, 1000), Point(1, 2020, 1000)]));
Check(pinchSink.Magnifications.Count == 1 && pinchSink.Magnifications[0] == MagnifyPhase.Began &&
    pinchPointer.Moves == 0 && pinchPointer.Scrolls == 0,
    "The actual output mode must dispatch magnify through the native sink only");
pinchMode.Dispose();
Check(pinchSink.Magnifications.Last() == MagnifyPhase.Cancelled,
    "Unloading the output mode must cancel a native magnification");
var swipeSink = new RecordingClicker();
var swipeMode = new TrackpadRelativeMode { Pointer = new RecordingPointer(), Clicker = swipeSink,
    Sensitivity = new(20), ThreeFingerSwipes = true };
swipeMode.Read(new TouchReport([Point(0, 1000, 1000), Point(1, 1200, 1000), Point(2, 1400, 1000)]));
swipeMode.Read(new TouchReport([Point(0, 900, 1000), Point(1, 1100, 1000), Point(2, 1300, 1000)]));
Check(swipeSink.Swipes.SequenceEqual(new[] { SwipeDirection.Left }),
    "The actual output mode must dispatch a three-finger system swipe");

var pausedSwipe = new TouchMotion();
Gesture(pausedSwipe, 0, Three(100, 100));
Check(Gesture(pausedSwipe, .3, Three(100, 100)) == default,
    "A paused stationary swipe must reanchor without output");
Check(Gesture(pausedSwipe, .31, Three(107, 100)).Swipe == SwipeDirection.Right,
    "A swipe must still work after a stationary pause");
var cancelledPinch = new TouchMotion();
Gesture(cancelledPinch, 0, Finger(0, 100, 100), Finger(1, 120, 100));
Gesture(cancelledPinch, .01, Finger(0, 99, 100), Finger(1, 121, 100));
Check(Gesture(cancelledPinch, .02, Finger(0, 99, 100), Finger(2, 121, 100)).Magnify == MagnifyPhase.Cancelled,
    "Changing pinch contact IDs must cancel rather than jump the scale");
var invalidPinch = new TouchMotion();
Gesture(invalidPinch, 0, Finger(0, 100, 100), Finger(1, 120, 100));
Gesture(invalidPinch, .01, Finger(0, 99, 100), Finger(1, 121, 100));
Check(Gesture(invalidPinch, .02, Finger(0, float.NaN, 100), Finger(1, 121, 100)).Magnify == MagnifyPhase.Cancelled,
    "Invalid coordinates must release a live native magnification");
var disabledPinch = new TouchMotion();
Update(disabledPinch, 0, Finger(0, 100, 100), Finger(1, 120, 100));
Check(Update(disabledPinch, .01, Finger(0, 99, 100), Finger(1, 121, 100)).Magnify == MagnifyPhase.None,
    "Pinch and swipes must remain off unless explicitly enabled");

var landing = new TouchMotion();
Gesture(landing, 0, Finger(0, 100, 100), Finger(1, 120, 100));
Check(Gesture(landing, .01, Finger(0, 99, 100), Finger(1, 121, 100)).Magnify == MagnifyPhase.Began,
    "A tentative two-finger contact can initially classify as pinch");
Check(Gesture(landing, .02, Finger(0, 99, 100), Finger(1, 121, 100), Finger(2, 140, 100)).Magnify == MagnifyPhase.Cancelled,
    "A third finger landing after pinch must cancel the zoom");
Check(Gesture(landing, .03, Finger(0, 106, 100), Finger(1, 128, 100), Finger(2, 147, 100)).Swipe == SwipeDirection.Right,
    "Asynchronous three-finger touchdown must still allow a system swipe");

foreach (var (direction, key) in new[] {
    (SwipeDirection.Left, 124), (SwipeDirection.Right, 123),
    (SwipeDirection.Up, 126), (SwipeDirection.Down, 125) })
{
    var shortcut = SwipeShortcut.For(direction);
    Check(shortcut.Key == key && shortcut.Flags == 0x840000,
        "System swipes must match macOS Control+Arrow symbolic shortcuts, including the function-key bit");
}

var penTap = new PenButtonTap();
bool PenTap(float x, bool down, double time, uint pressure = 0, bool other = false, bool enabled = true) =>
    penTap.Update(new(x, 1000), new(.005f), down, other, pressure, time, enabled);
Check(!PenTap(1000, true, 0), "Upper-button down must defer the middle click");
Check(!PenTap(1004, true, .1), "Tiny hover noise while holding the button must not click");
Check(PenTap(1002, false, .2), "A short stationary upper-button release must middle-click");
Check(!PenTap(1002, false, .21), "Idle button-up packets must not repeat a middle click");
PenTap(1000, true, 1); PenTap(1100, true, 1.1);
Check(!PenTap(1000, false, 1.2), "A scroll that returns to its origin must never middle-click");
PenTap(1000, true, 2);
Check(!PenTap(1100, false, 2.1), "Movement on the release packet must cancel a middle click");
PenTap(1000, true, 3);
Check(!PenTap(1000, false, 3.6), "A long stationary hold must not accidentally open a link");
PenTap(1000, true, 4, pressure: 1);
Check(!PenTap(1000, false, 4.1), "Tip pressure must cancel a middle click");
PenTap(1000, true, 5); PenTap(1000, true, 5.01, other: true);
Check(!PenTap(1000, false, 5.1), "Using another pen button must cancel a middle click");
PenTap(1000, true, 6); penTap.Reset();
Check(!PenTap(1000, false, 6.1), "Proximity loss must discard a pending middle click");
PenTap(1000, true, 7);
Check(!PenTap(1000, false, 7.1, enabled: false), "Disabling middle-tap must cancel pending input");
var middleSink = new RecordingClicker();
var middleMode = new TrackpadRelativeMode { Pointer = new RecordingPointer(), Clicker = middleSink,
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100),
    Elements = [new ScrollBinding.ScrollBindingFilter()], UpperButtonTapMiddleClick = true };
middleMode.Read(ScrollPen(1000, false));
middleMode.Read(ScrollPen(1000, true));
middleMode.Read(ScrollPen(1000, false));
Check(middleSink.Buttons.SequenceEqual(new[] { TapButton.Middle }),
    "The actual output mode must emit a middle click only after the upper button is released");
middleMode.Read(ScrollPen(1000, true)); middleMode.Read(ScrollPen(1100, true));
middleMode.Read(ScrollPen(1000, false));
Check(middleSink.Buttons.Count == 1, "Actual pen scrolling must never append a middle click");

Motion Interactive(TouchMotion state, double time, params Contact[] contacts) =>
    state.Update(contacts, time, Vector2.One, new(20), 12,
        threeFingerSwipes: true, pinchZoom: true, interactiveDesktopSwipes: true);
var follow = new TouchMotion();
Check(Interactive(follow, 0, Three(100, 100)) == default, "Interactive touchdown must not animate yet");
var began = Interactive(follow, .01, Three(97, 100));
Check(began.Desktop.Phase == DesktopSwipePhase.Began && began.Desktop.Progress > 0 &&
    began.Swipe == SwipeDirection.None && began.Cursor == Vector2.Zero && began.Scroll == Vector2.Zero,
    "Horizontal movement must begin native progress without a shortcut or cursor motion");
var changed = Interactive(follow, .02, Three(90, 100));
Check(changed.Desktop.Phase == DesktopSwipePhase.Changed && changed.Desktop.Progress > began.Desktop.Progress,
    "Desktop progress must follow further finger movement directly");
var reversed = Interactive(follow, .03, Three(95, 100));
Check(reversed.Desktop.Progress < changed.Desktop.Progress,
    "Reversing the fingers must reverse the animation before release");
Interactive(follow, .04, Three(80, 100));
Check(Interactive(follow, .05).Desktop.Phase == DesktopSwipePhase.Ended,
    "A deliberate desktop swipe must end and settle on lift");
Check(Interactive(follow, .06) == default, "Desktop ending must not repeat on idle packets");
var retreat = new TouchMotion();
Interactive(retreat, 0, Three(100, 100)); Interactive(retreat, .01, Three(95, 100));
Interactive(retreat, .02, Three(100, 100));
Check(Interactive(retreat, .03).Desktop.Phase == DesktopSwipePhase.Cancelled,
    "Returning to the origin must roll the desktop back on lift");
var dropout = new TouchMotion();
Interactive(dropout, 0, Three(100, 100)); Interactive(dropout, .01, Three(95, 100));
Check(Interactive(dropout, .02, Finger(0, 95, 100), Finger(1, 105, 100)).Desktop.Phase == DesktopSwipePhase.None,
    "A brief dropped contact must not prematurely end the animation");
Check(Interactive(dropout, .03, Three(90, 100)).Desktop.Phase == DesktopSwipePhase.Changed,
    "All three original contacts must resume tracking within the dropout grace period");
Check(Interactive(dropout, .04, Finger(0, 90, 100), Finger(1, 100, 100), Finger(2, 110, 100), Finger(3, 120, 100))
    .Desktop.Phase == DesktopSwipePhase.Cancelled, "A palm must cancel a native desktop animation");
var desktopPen = new TouchMotion();
Interactive(desktopPen, 0, Three(100, 100)); Interactive(desktopPen, .01, Three(95, 100));
Check(desktopPen.Update(Three(95, 100), .02, Vector2.One, new(20), 12, penNearby: true,
    threeFingerSwipes: true, interactiveDesktopSwipes: true).Desktop.Phase == DesktopSwipePhase.Cancelled,
    "Pen takeover must cancel the native desktop animation");
var heldDesktop = new TouchMotion();
Interactive(heldDesktop, 0, Three(100, 100));
for (int i = 1; i <= 30; i++) Interactive(heldDesktop, i * .01, Three(100, 100));
Check(Interactive(heldDesktop, .31, Three(95, 100)).Desktop.Phase == DesktopSwipePhase.Began,
    "Resting three fingers before moving must not disarm the gesture");
var interactiveVertical = new TouchMotion();
Interactive(interactiveVertical, 0, Three(100, 100));
Check(Interactive(interactiveVertical, .01, Three(100, 93)).Swipe == SwipeDirection.Up,
    "Interactive horizontal switching must preserve vertical system shortcuts");
var desktopSink = new RecordingClicker();
var desktopMode = new TrackpadRelativeMode { Pointer = new RecordingPointer(), Clicker = desktopSink,
    ThreeFingerSwipes = true, InteractiveDesktopSwipes = true, InteractiveDesktopSupported = true };
desktopMode.Read(new TouchReport([Point(0, 1000, 1000), Point(1, 1200, 1000), Point(2, 1400, 1000)]));
desktopMode.Read(new TouchReport([Point(0, 950, 1000), Point(1, 1150, 1000), Point(2, 1350, 1000)]));
Check(desktopSink.DesktopGestures.Count == 1 && desktopSink.DesktopGestures[0].Phase == DesktopSwipePhase.Began &&
    desktopSink.Swipes.Count == 0, "Actual mode must route desktop progress instead of a keyboard shortcut");
desktopMode.Dispose();
Check(desktopSink.DesktopGestures.Last().Phase == DesktopSwipePhase.Cancelled,
    "Unloading the mode must release a native desktop animation");

var smoothLoss = new TouchMotion();
Interactive(smoothLoss, 0, Three(100, 100));
var fullThree = Interactive(smoothLoss, .01, Three(95, 100));
var onlyTwo = Interactive(smoothLoss, .02, Finger(0, 90, 100), Finger(1, 100, 100));
Check(onlyTwo.Desktop.Phase == DesktopSwipePhase.Changed && onlyTwo.Desktop.Progress > fullThree.Desktop.Progress,
    "A brief missing third finger must keep following the two remaining contacts");
var rejoins = Interactive(smoothLoss, .03, Finger(0, 85, 100), Finger(1, 95, 100), Finger(2, 111, 100));
Check(MathF.Abs(rejoins.Desktop.Progress - .25f) < .0001f,
    "The returning finger's stale position must not jump the reconstructed desktop centroid");
Check(Interactive(smoothLoss, .04, Finger(0, 85, 100), Finger(1, 95, 100), Finger(2, 111, 100)).Desktop.Phase == DesktopSwipePhase.None,
    "Stationary duplicate frames must not flood the native gesture stream");

var scrollNoiseFilter = new ScrollBinding.ScrollBindingFilter();
var scrollNoiseMode = new TrackpadRelativeMode { Pointer = new RecordingPointer(), Clicker = new RecordingClicker(),
    Sensitivity = new(20), Elements = [scrollNoiseFilter], FilterPenScrollNoise = true,
    HoverJitterDeadband = .1f, RejectHoverSpikes = true };
scrollNoiseMode.Read(ScrollPen(1000, false)); scrollNoiseMode.Read(ScrollPen(1000, true));
scrollNoiseMode.Read(ScrollPen(1025, true)); scrollNoiseMode.Read(ScrollPen(1000, true));
Check(scrollNoiseFilter.SeenPositions.TakeLast(3).All(p => p == new Vector2(1000, 1000)),
    "The scroll plugin must receive filtered coordinates even while the upper button is held");
scrollNoiseMode.Read(ScrollPen(1100, true));
Check(scrollNoiseFilter.SeenPositions.Last().X == 1100,
    "Deliberate pen-scroll movement must bypass the small-spike window immediately");
var stick = new PenJoystickScroll();
void Stick(Vector2 position, bool pressed, double now, bool reverse = false) => stick.Update(position, pressed, now,
    new(20, 20), new(600, 1200), 1, 2, reverse);
Stick(new(100), true, 0);
Check(stick.Step(.01) == Vector2.Zero, "Joystick centre must not scroll");
Stick(new(100.2f, 100.3f), true, .02);
Check(stick.Step(.03) == Vector2.Zero, "Joystick deadzone must absorb stationary pen noise");
Stick(new(100, 120), true, .04);
Check(stick.Step(.05).Y < 0, "Joystick edge must scroll in the intended document direction");
Check(stick.Step(.06).Y < 0, "An unmoving pen at the edge must continue scrolling without new coordinates");
float edge = MathF.Abs(stick.Step(.07).Y);
Stick(new(100, 140), true, .08);
Check(MathF.Abs(stick.Step(.09).Y) <= edge * 2 + 1,
    "Beyond the virtual edge must remain bounded at the configured maximum speed");
Stick(new(100), true, .10);
Check(stick.Step(.11) == Vector2.Zero, "Returning to centre must stop continuous scrolling");
Stick(new(100, 120), false, .12);
Check(!stick.Active && stick.Step(.13) == Vector2.Zero, "Releasing the button must immediately cancel all wheel output");
Stick(new(300), true, 1); Stick(new(300, 320), true, 1.01, reverse: true);
Check(stick.Step(1.02).Y > 0, "Joystick reverse option must reverse the wheel direction");
Check(stick.Step(2) == Vector2.Zero, "Lost device reports must not leave autoscroll running indefinitely");
stick.Reset(); Stick(new(500), true, 3);
Check(stick.Step(3.01) == Vector2.Zero, "Every new press must establish its own virtual centre");
var stickPointer = new RecordingPointer();
var stickSink = new RecordingClicker();
var stickMode = new TrackpadRelativeMode { Pointer = stickPointer, Clicker = stickSink,
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100),
    PenJoystickScrolling = true, EnableJoystickTimer = false, UpperButtonTapMiddleClick = true };
stickMode.Read(ScrollPen(1000, false)); stickMode.Read(ScrollPen(1000, true));
stickMode.Read(ScrollPen(5000, true));
Check(stickPointer.LastPosition == Vector2.Zero,
    "Joystick holding must freeze the actual relative pointer while the pen is displaced");
stickMode.Read(ScrollPen(5000, false));
Check(stickPointer.LastPosition == Vector2.Zero && stickSink.Buttons.Count == 0,
    "Joystick release must neither jump the cursor nor emit a middle click after scrolling");
stickMode.Read(ScrollPen(5020, false));
Check(MathF.Abs(stickPointer.LastPosition.X - 4) < .001f,
    "Relative pointing must resume from the post-scroll pen position without discarded movement");
stickMode.Dispose();

// SetSettings publishes a new output mode before configuring its relative
// parameters. Simulate its ResetTime setter landing during the first report.
var configuringFilter = new ScrollBinding.ScrollBindingFilter();
var configuringPointer = new RecordingPointer();
var configuringMode = new TrackpadRelativeMode { Pointer = configuringPointer,
    Clicker = new RecordingClicker(), Sensitivity = new(20), Elements = [configuringFilter] };
configuringFilter.BeforeConsume = () => {
    configuringMode.ResetTime = TimeSpan.FromMilliseconds(100);
    configuringFilter.BeforeConsume = null;
};
configuringMode.Read(ScrollPen(1000, false));
Check(configuringMode.ResetTime == TimeSpan.FromMilliseconds(100),
    "Reanchoring must not overwrite ResetTime configured concurrently by the host");
configuringMode.Read(ScrollPen(1100, false));
Check(configuringPointer.LastPosition.X > 0,
    "Pointing must work after the host configures the mode during its first report");
configuringMode.Dispose();
int movesAfterDispose = configuringPointer.Moves;
configuringMode.Read(ScrollPen(1200, false));
Check(configuringPointer.Moves == movesAfterDispose,
    "A disposed output mode must ignore reports while the host replaces it");

var edgeStick = new PenJoystickScroll();
Vector2 boostedReach = new(20), boostedMax = new(2200);
void EdgeAt(float x, double time, float boost) => edgeStick.Update(new(x, 0), true, time,
    boostedReach, boostedMax, 1, 3, false, boost);
EdgeAt(0, 0, 4); EdgeAt(20, .01, 4);
Check(MathF.Abs(edgeStick.Step(.02).X) >= 175,
    "Edge boost must multiply the actual edge speed, independently of the response exponent");
EdgeAt(100, .03, 4);
Check(MathF.Abs(edgeStick.Step(.04).X) <= 177,
    "Even boosted motion must stay capped outside the joystick field");
edgeStick.Reset(); EdgeAt(0, 1, 1); EdgeAt(10, 1.01, 1);
float unboostedCentre = edgeStick.Step(1.02).X;
edgeStick.Reset(); EdgeAt(0, 2, 10); EdgeAt(10, 2.01, 10);
Check(edgeStick.Step(2.02).X == unboostedCentre,
    "Outer-edge boost must preserve the inner 65 percent of the response curve");
edgeStick.Reset(); EdgeAt(0, 3, 1); EdgeAt(20, 3.01, 1);
Check(MathF.Abs(edgeStick.Step(3.02).X) <= 45,
    "A boost of one must preserve the previous joystick behaviour");
edgeStick.Reset();
edgeStick.Update(Vector2.Zero, true, 4, new(20), new(50000), 1, 9, false, 2);
edgeStick.Update(new(20, 0), true, 4.01, new(20), new(50000), 1, 9, false, 2);
Check(MathF.Abs(edgeStick.Step(4.02).X) >= 999,
    "Higher base speeds and response exponents must be accepted without the old limits");

var acceleration = new PointerAcceleration();
Vector2 Accelerated(Vector2 delta, double time, bool enabled = true) =>
    acceleration.Apply(delta * 22, delta, time, enabled, .9f, 1.35f, 150);
Check(Accelerated(Vector2.One, 0, false) == new Vector2(22),
    "Disabled acceleration must preserve fixed sensitivity exactly");
Accelerated(Vector2.Zero, 1);
float slowMotion = Accelerated(new(.01f, 0), 1.01).X / .22f;
Check(slowMotion >= .9f && slowMotion < .91f,
    "Slow motion must use the configured precision multiplier");
float fastMotion = Accelerated(new(3, 0), 1.02).X / 66;
Check(fastMotion > slowMotion && fastMotion <= 1.35f,
    "Fast motion must immediately travel farther with a bounded smooth gain");
Check(Accelerated(Vector2.Zero, 1.03) == Vector2.Zero,
    "Speed smoothing must never create delayed cursor drift from zero displacement");
Check(Accelerated(new(1, 0), 2).X <= 22,
    "A long pause must reset speed rather than replay a previous fast gain");
acceleration.Reset(); Accelerated(Vector2.Zero, 3);
var diagonalAcceleration = Accelerated(new(1, 2), 3.01);
Check(MathF.Abs(diagonalAcceleration.Y - 2 * diagonalAcceleration.X) < .001f,
    "Acceleration must preserve movement direction");
float SteadyGain(double interval)
{
    var rate = new PointerAcceleration(); rate.Apply(Vector2.Zero, Vector2.Zero, 0, true, .9f, 1.35f, 150);
    Vector2 result = default;
    for (int i = 1; i <= (int)(.2 / interval); i++)
        result = rate.Apply(new(1, 0), new((float)(interval * 75), 0), i * interval, true, .9f, 1.35f, 150);
    return result.X;
}
Check(MathF.Abs(SteadyGain(.005) - SteadyGain(.01)) < .001f,
    "The gain curve must depend on physical speed, not the tablet report rate");
var acceleratedPointer = new RecordingPointer();
var acceleratedMode = new TrackpadRelativeMode { Pointer = acceleratedPointer, Clicker = new RecordingClicker(),
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100), PenVelocityAcceleration = true,
    AccelerationSlowGain = .5f, AccelerationFastGain = .5f };
acceleratedMode.Read(ScrollPen(1000, false)); acceleratedMode.Read(ScrollPen(1000, false));
acceleratedMode.Read(ScrollPen(1100, false));
Check(MathF.Abs(acceleratedPointer.LastPosition.X - 10) < .001f,
    "The actual pen output must apply enabled velocity sensitivity after the relative transform");
acceleratedMode.Read(new PenReport(new(1200, 1000), true) { Pressure = 1 });
Check(MathF.Abs(acceleratedPointer.LastPosition.X - 20) < .001f,
    "Pen contact must preserve unaccelerated drawing movement");
acceleratedMode.Read(ScrollPen(1300, true));
Check(MathF.Abs(acceleratedPointer.LastPosition.X - 20) < .001f,
    "Side-button handling must bypass cursor acceleration");
acceleratedMode.Dispose();
var acceleratedTouchPointer = new RecordingPointer();
var acceleratedTouch = new TrackpadRelativeMode { Pointer = acceleratedTouchPointer, Clicker = new RecordingClicker(),
    Sensitivity = new(20), TouchVelocityAcceleration = true, AccelerationSlowGain = .5f, AccelerationFastGain = .5f };
acceleratedTouch.Read(new TouchReport([Point(0, 100, 100)]));
acceleratedTouch.Read(new TouchReport([Point(0, 120, 100)]));
Check(MathF.Abs(acceleratedTouchPointer.LastPosition.X - 20 * (325.12f / 4095) * 10) < .001f,
    "One-finger pointer output must use its independently enabled acceleration");
acceleratedTouch.Dispose();

var allFilterPointer = new RecordingPointer();
var allFilterMode = new TrackpadRelativeMode { Pointer = allFilterPointer, Clicker = new RecordingClicker(),
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100), HoverJitterDeadband = .1f,
    RejectHoverSpikes = true, FilterAllPenMovement = true };
allFilterMode.Read(ScrollPen(1000, false));
allFilterMode.Read(new PenReport(new(1025, 1000), true) { Pressure = 100 });
Check(allFilterPointer.LastPosition == Vector2.Zero,
    "General pen filtering must reject a small spike while the tip is pressed");
allFilterMode.Read(new PenReport(new(1000, 1000), true) { Pressure = 100, PenButtons = [true, false] });
Check(allFilterPointer.LastPosition == Vector2.Zero,
    "Holding a normal side button must not bypass the general noise filter");
allFilterMode.Read(new PenReport(new(1100, 1000), true) { Pressure = 100 });
Check(allFilterPointer.LastPosition.X > 0,
    "Deliberate dragging must pass immediately through the general pen filter");
allFilterMode.Read(new PenReport(new(1102, 1000), true));
Check(allFilterPointer.LastPosition == Vector2.Zero,
    "Releasing contact must not replay jitter or change the filter anchor");
allFilterMode.Dispose();
var sharedScrollSink = new ScrollBinding.ScrollBindingFilter();
var sharedScrollMode = new TrackpadRelativeMode { Pointer = new RecordingPointer(), Clicker = new RecordingClicker(),
    Sensitivity = new(20), Elements = [sharedScrollSink], HoverJitterDeadband = .1f,
    RejectHoverSpikes = true, FilterAllPenMovement = true, FilterPenScrollNoise = true };
sharedScrollMode.Read(ScrollPen(1000, false)); sharedScrollMode.Read(ScrollPen(1000, true));
sharedScrollMode.Read(ScrollPen(1025, true)); sharedScrollMode.Read(ScrollPen(1030, true));
Check(sharedScrollSink.SeenPositions.Last().X == 1025,
    "General pen filtering and scroll-only filtering must not stack two median windows");
sharedScrollMode.Dispose();

var pressureGesture = new PressureClipboardGesture();
PressureClipboardAction PressureAt(double t, float x, float p, bool cancel = false,
    bool copy = true, bool paste = true) => pressureGesture.Update(new(x, 0), p, cancel, t, copy, paste);
PressureAt(0, 0, .2f); PressureAt(.09, 3, .2f);
Check(PressureAt(.11, 3, .8f) == PressureClipboardAction.None,
    "A pressure peak must not send Copy before the normal drag finishes");
PressureAt(.16, 3, .8f);
Check(PressureAt(.19, 3, 0) == PressureClipboardAction.Copy,
    "A selection-ending rise above normal pressure must copy on tip release");
Check(PressureAt(.20, 3, 0) == PressureClipboardAction.None,
    "One selection must send Copy only once");
PressureAt(1, 0, .2f); PressureAt(1.09, 3, .2f); PressureAt(1.11, 3, .8f);
PressureAt(1.16, 3, .8f); PressureAt(1.18, 5, .8f);
Check(PressureAt(1.21, 5, 0) == PressureClipboardAction.None,
    "Continuing to drag after a pressure peak must cancel that copy request");
PressureAt(2, 0, .2f); PressureAt(2.09, 0, .2f); PressureAt(2.11, 0, .8f);
PressureAt(2.16, 3, .8f);
Check(PressureAt(2.19, 3, 0) == PressureClipboardAction.None,
    "A hard landing before dragging must not act as a selection-ending peak");
PressureAt(3, 0, .2f); PressureAt(3.09, 0, .2f); PressureAt(3.11, 0, .8f);
PressureAt(3.16, 0, .8f); PressureAt(3.19, 0, .2f); PressureAt(3.22, 0, .8f);
Check(PressureAt(3.27, 0, .8f) == PressureClipboardAction.None,
    "Two stationary pressure peaks must not paste while mouse-down is still active");
Check(PressureAt(3.30, 0, 0) == PressureClipboardAction.Paste,
    "Two quick stationary pressure peaks in one contact must paste after mouse-up");
PressureAt(4, 0, .2f); PressureAt(4.09, 0, .2f); PressureAt(4.11, 0, .8f);
PressureAt(4.16, 0, .8f); PressureAt(4.19, 0, .8f); PressureAt(4.22, 0, .8f);
Check(PressureAt(4.25, 0, 0) == PressureClipboardAction.None,
    "A sustained strong hold must not count as two pressure peaks");
PressureAt(5, 0, .2f); PressureAt(5.09, 0, .2f); PressureAt(5.11, 0, .8f);
PressureAt(5.16, 0, .8f); PressureAt(5.19, 0, 0);
PressureAt(5.25, 0, .2f); PressureAt(5.34, 0, .2f); PressureAt(5.36, 0, .8f);
PressureAt(5.41, 0, .8f);
Check(PressureAt(5.44, 0, 0) == PressureClipboardAction.None,
    "Two separate tip taps must not become a pressure-paste gesture");
PressureAt(6, 0, .2f); PressureAt(6.09, 0, .2f); PressureAt(6.11, 0, .8f);
PressureAt(6.16, 0, .8f); PressureAt(6.19, 0, .2f); PressureAt(6.38, 0, .2f);
PressureAt(6.57, 0, .2f); PressureAt(6.71, 0, .8f); PressureAt(6.76, 0, .8f);
Check(PressureAt(6.79, 0, 0) == PressureClipboardAction.None,
    "Pressure peaks outside double-click timing must not paste");
PressureAt(7, 0, .2f); PressureAt(7.09, 3, .2f); PressureAt(7.11, 3, .8f);
PressureAt(7.115, 3, .2f);
Check(PressureAt(7.17, 3, 0) == PressureClipboardAction.None,
    "An isolated pressure spike must not request Copy");
PressureAt(8, 0, .2f); PressureAt(8.09, 3, .2f); PressureAt(8.11, 3, .24f);
PressureAt(8.16, 3, .24f);
Check(PressureAt(8.19, 3, 0) == PressureClipboardAction.None,
    "Small variations around normal selection pressure must not copy");
PressureAt(9, 0, .6f); PressureAt(9.09, 3, .6f); PressureAt(9.11, 3, .9f);
PressureAt(9.16, 3, .9f);
Check(PressureAt(9.19, 3, 0) == PressureClipboardAction.Copy,
    "Peak detection must also adapt to a heavier normal selection pressure");
PressureAt(10, 0, .2f); PressureAt(10.09, 3, .2f); PressureAt(10.11, 3, .8f);
PressureAt(10.16, 3, .8f); PressureAt(10.18, 3, .8f, cancel: true);
Check(PressureAt(10.21, 3, 0) == PressureClipboardAction.None,
    "A side button, eraser or proximity cancellation must discard pending Copy");
PressureAt(11, 0, .2f); PressureAt(11.09, 3, .2f); PressureAt(11.11, 3, .8f);
PressureAt(11.16, 3, .8f);
Check(PressureAt(12, 3, 0) == PressureClipboardAction.None,
    "A lost report stream must not execute a stale copy request");
PressureAt(13, 0, .2f); PressureAt(13.09, 3, .2f); PressureAt(13.11, 3, .8f);
PressureAt(13.16, 3, .8f);
Check(PressureAt(13.19, 3, 0, copy: false, paste: false) == PressureClipboardAction.None,
    "Disabling the feature must cancel an armed pressure command");
var gradualPressure = new PressureClipboardGesture();
for (int i = 0; i <= 60; i++)
    gradualPressure.Update(new(i / 12f, 0), .2f + .2f * i / 60, false, i * .01, true, true);
for (int i = 1; i <= 70; i++)
    gradualPressure.Update(new(5, 0), .4f + .4f * i / 70, false, .6 + i * .01, true, true);
Check(gradualPressure.Update(new(7, 0), 0, false, 1.31, true, true) == PressureClipboardAction.Copy,
    "A gradual deliberate pressure rise at a stopped selection must not be learned away or cancelled by lift coordinates");
var partialRelaxation = new PressureClipboardGesture();
PressureClipboardAction Partial(double t, float p) => partialRelaxation.Update(Vector2.Zero, p, false, t, true, true);
Partial(0, .25f); Partial(.09, .25f); Partial(.11, .5f); Partial(.16, .5f);
Partial(.22, .8f); Partial(.27, .8f); Partial(.30, .65f); Partial(.33, .85f); Partial(.38, .85f);
Check(partialRelaxation.LockPointer, "Recognized stationary squeezing must freeze pointer drift during the held click");
Check(Partial(.41, 0) == PressureClipboardAction.Paste,
    "A partial peak-relative relaxation must rearm the second squeeze without requiring a return to the original baseline");

var dictationPress = new PressureDictationGesture();
Check(!dictationPress.Update(.9f, true, false, true, 0, .6f), "Dictation pressure must survive the short debounce interval");
Check(dictationPress.Captured, "Crossing the strong-pressure threshold must capture scrolling while the peak is verified");
Check(dictationPress.Update(.9f, true, false, true, .05, .6f), "Upper button plus strong pressure must trigger dictation");
Check(!dictationPress.Update(.9f, true, false, true, .10, .6f), "Dictation must not repeat while the same button is held");
dictationPress.Update(0, false, false, true, .12, .6f);
Check(!dictationPress.Captured, "Releasing the upper button must restore ordinary pen scrolling");
Check(!dictationPress.Update(.3f, true, false, true, .14, .6f), "Light pressure with the upper button must keep normal scrolling");
Check(!dictationPress.Captured, "Ordinary light-pressure pen scrolling must remain available before a dictation gesture");
dictationPress.Update(.9f, true, false, true, .16, .6f);
Check(dictationPress.Update(.9f, true, false, true, .21, .6f), "Releasing the button must rearm dictation");
Check(!dictationPress.Update(.9f, true, true, true, .26, .6f), "The other side button must cancel dictation");

var pressureEvents = new List<string>();
var pressurePointer = new RecordingPointer { AfterFlush = () => pressureEvents.Add("flush") };
var pressureSink = new RecordingClicker { ClipboardEvent = action => pressureEvents.Add(action.ToString()) };
double pressureTime = 0;
var pressureMode = new TrackpadRelativeMode { Pointer = pressurePointer, Clicker = pressureSink,
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100),
    PressureCopySelection = true, PressureDoublePaste = true, ClockSeconds = () => pressureTime,
    Elements = [new ReleaseMarker(pressureEvents)] };
void ModePressure(double time, float x, uint value, bool upper = false)
{
    pressureTime = time;
    pressureMode.Read(new PenReport(new(x, 1000), true) { Pressure = value, PenButtons = [false, upper] });
}
ModePressure(0, 1000, 409); ModePressure(.09, 1800, 409); ModePressure(.11, 1800, 1500);
ModePressure(.16, 1800, 1500); ModePressure(.19, 2200, 0);
Check(pressureSink.ClipboardActions.SequenceEqual([PressureClipboardAction.Copy]),
    "The real output mode must dispatch Copy from the normalized pressure gesture");
Check(pressureEvents.TakeLast(3).SequenceEqual(["release", "flush", "Copy"]),
    "Mouse-up handling and pointer flush must precede the Copy shortcut");
ModePressure(1, 1800, 409); ModePressure(1.09, 1800, 409); ModePressure(1.11, 1800, 1500);
ModePressure(1.16, 1800, 1500); ModePressure(1.19, 1800, 409); ModePressure(1.22, 1850, 1500);
ModePressure(1.27, 1900, 1500); ModePressure(1.30, 2200, 0);
Check(pressureSink.ClipboardActions.Last() == PressureClipboardAction.Paste &&
    pressureEvents.TakeLast(3).SequenceEqual(["release", "flush", "Paste"]),
    "The real output mode must flush mouse-up before stationary pressure paste");
Check(pressurePointer.LastPosition == Vector2.Zero, "Stationary pressure paste must not move the cursor on squeeze or release");
pressureMode.Dispose();
var dictationSink = new RecordingClicker();
var dictationPointer = new RecordingPointer();
var dictationMode = new TrackpadRelativeMode { Pointer = dictationPointer, Clicker = dictationSink,
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100),
    PressureCopySelection = true, PressureDoublePaste = true, PressureDictationEnabled = true,
    UpperButtonTapMiddleClick = true, PenJoystickScrolling = true, EnableJoystickTimer = false,
    ClockSeconds = () => pressureTime };
void DictationAt(double time, float x, uint value, bool upper)
{
    pressureTime = time;
    dictationMode.Read(new PenReport(new(x, 1000), true) { Pressure = value, PenButtons = [false, upper] });
}
DictationAt(2, 1000, 0, false); DictationAt(2.01, 1000, 0, true);
DictationAt(2.02, 1000, 1600, true); DictationAt(2.07, 1100, 1600, true);
Check(dictationSink.DictationCalls == 1 && dictationPointer.LastPosition == Vector2.Zero,
    "The dictation chord must trigger once with the cursor frozen");
DictationAt(2.12, 1100, 1600, true);
Check(dictationSink.DictationEnds == 0, "The Fn modifier must stay held while the upper button remains down");
DictationAt(2.14, 1100, 0, false);
Check(dictationSink.DictationCalls == 1 && dictationSink.DictationEnds == 1 && dictationSink.Buttons.Count == 0 && dictationSink.ClipboardActions.Count == 0,
    "Dictation must not leak a middle click or clipboard action");
DictationAt(2.2, 1100, 0, true); DictationAt(2.22, 1100, 1600, true); DictationAt(2.28, 1100, 1600, true);
Check(dictationSink.DictationCalls == 2 && dictationSink.DictationEnds == 1, "A new held dictation chord must stay down until a release condition");
dictationMode.Read(new OutOfRangeReport());
Check(dictationSink.DictationEnds == 2, "Proximity loss must release a held Fn modifier");
DictationAt(3, 1200, 0, false); DictationAt(3.02, 1200, 1600, true); DictationAt(3.08, 1200, 1600, true);
dictationMode.Dispose();
Check(dictationSink.DictationCalls == 3 && dictationSink.DictationEnds == 3,
    "Unloading or replacing a mode must never leave the synthetic Fn modifier held");

// Physical-tool identity is separate from pressure reports on Intuos Pro.
var erase = new EraserSelectionGesture();
bool Erase(double t, float x, float p, bool eraser = true, bool buttons = false) =>
    erase.Update(new(x, 0), p, eraser, buttons, t, true, 1.5f);
Erase(0, 0, .2f); Erase(.03, 3, .2f);
Check(Erase(.05, 100, 0), "Eraser selection must run only on lift, ignoring lift-coordinate drift");
Check(!Erase(.06, 100, 0), "Eraser lift must delete only once");
Erase(1, 0, .2f); Check(!Erase(1.05, 0, 0), "Stationary eraser taps must not delete");
Erase(2, 0, .2f); Erase(2.04, 3, .2f); Erase(2.05, 3, .2f, buttons: true);
Check(!Erase(2.08, 3, 0), "Side buttons must cancel eraser deletion");
Erase(3, 0, .2f); Erase(3.04, 3, .2f);
Check(!Erase(3.4, 3, 0), "Lost reports must not delete a stale selection");
Erase(4, 0, .2f); Erase(4.04, 3, .2f);
Check(!Erase(4.06, 3, 0, eraser: false), "Switching to the tip must cancel eraser deletion");

var flickEngine = new PressureFlickGesture();
FlickUpdate Flick(double t, float x, float tilt = 0, float pressure = .8f, bool cancel = false, float y = 0) =>
    flickEngine.Update(new(x, y), new Vector2(tilt, 0), pressure, cancel, t, true, true);
Check(Flick(0, 0) == default, "Flick arming needs sustained pressure");
Check(Flick(.045, 0).JustArmed, "Sustained pressure must arm the flick");
Check(!Flick(.055, 2, .1f).JustArmed, "Flick motion must not arm the same gesture again");
Check(Flick(.08, 8, .2f).Action == PenGestureAction.Undo, "Fast right movement with tilt change must undo");
Check(Flick(.09, 16, .4f).Action == PenGestureAction.None, "A held flick cannot repeat");
Flick(.1, 16, pressure: 0); Flick(.12, 0); Flick(.17, 0); Flick(.18, -2, .1f);
Check(Flick(.21, -8, .2f).Action == PenGestureAction.OpenAI, "The left flick must choose AI independently");
flickEngine.Reset(); Flick(1, 0); Flick(1.05, 0); Flick(1.06, 2);
Check(Flick(1.09, 9).Action == PenGestureAction.None, "Fast straight movement without tilt must not trigger");
flickEngine.Reset(); Flick(2, 0); Flick(2.05, 0); Flick(2.06, 2, .2f);
Check(Flick(2.18, 7, .2f).Action == PenGestureAction.None, "Slow pen movement must not trigger a flick");
flickEngine.Reset(); Flick(3, 0); Flick(3.05, 0); Flick(3.06, 2, .2f, y: 2);
Check(Flick(3.08, 8, .2f, y: 8).Action == PenGestureAction.None, "Diagonal motion must not trigger a horizontal action");
flickEngine.Reset(); Flick(4, 0); Flick(4.05, 0);
Check(Flick(4.4, 10, .3f).Action == PenGestureAction.None, "A report gap must cancel an armed flick");
flickEngine.Reset(); Flick(5, 0); Flick(5.05, 0); Flick(5.06, 2, .2f, cancel: true);
Check(Flick(5.08, 10, .3f).Action == PenGestureAction.None, "Side buttons and tool changes must cancel flicks");
flickEngine.Reset(); Flick(6, 0); Flick(6.05, 0);
for (int i = 1; i < 8; i++) Flick(6.05 + i * .1, 0);
Check(Flick(6.79, 10, .3f).Action == PenGestureAction.None, "An expired arming window must not execute later");
Check(flickEngine.Update(Vector2.Zero, null, 1, false, 8, true, true) == default,
    "Devices without tilt reports must not guess flick gestures");
Check(MacPenActions.ResolveTarget("ChatGPT in browser").Url == "https://chatgpt.com/" &&
    MacPenActions.ResolveTarget("ChatGPT/Codex").Bundle == "com.openai.codex" &&
    MacPenActions.ResolveTarget("invalid") == default, "AI targets must use a fixed app/HTTPS allowlist");


var extraEvents = new List<string>();
var extraSink = new RecordingClicker { PenEvent = action => extraEvents.Add(action.ToString()) };
var extraPointer = new RecordingPointer { AfterFlush = () => extraEvents.Add("flush") };
double extraTime = 0;
var extraMode = new TrackpadRelativeMode { Pointer = extraPointer, Clicker = extraSink,
    Sensitivity = new(20), ResetTime = TimeSpan.FromMilliseconds(100), ClockSeconds = () => extraTime,
    EraserDeletesSelection = true, PressureCopySelection = true, PressureDoublePaste = true,
    RightFlickUndo = true, LeftFlickAI = true,
    Elements = [new ReleaseMarker(extraEvents)] };
void Extra(double t, float x, uint pressure, float tilt = 0) {
    extraTime = t;
    extraMode.Read(new PenReport(new(x, 1000), true) { Pressure = pressure, Tilt = new(tilt, 0) });
}
extraMode.Read(new ToolReport(true));
Extra(0, 1000, 409); Extra(.05, 1800, 409); Extra(.1, 1800, 1600);
Extra(.15, 1800, 1600); Extra(.18, 1800, 0);
Check(extraSink.PenActions.SequenceEqual([PenGestureAction.DeleteSelection]) && extraSink.ClipboardActions.Count == 0,
    "Separate eraser identity must reach ordinary pen packets and exclude tip clipboard gestures");
Check(extraEvents.TakeLast(3).SequenceEqual(["release", "flush", "DeleteSelection"]),
    "Eraser deletion must follow native mouse-up and flush");
extraMode.Read(new ToolReport(true)); Extra(.3, 1000, 409); Extra(.35, 1800, 409);
extraTime = .38; extraMode.Read(new OutOfRangeReport());
Check(extraSink.PenActions.Count == 2 && extraSink.PenActions.Last() == PenGestureAction.DeleteSelection &&
    extraEvents.TakeLast(2).SequenceEqual(["flush", "DeleteSelection"]),
    "An eraser leaving range without a zero-pressure packet must finalize only after mouse reset/flush");
extraMode.Read(new ToolReport(true)); Extra(.5, 1000, 409); Extra(.55, 1800, 409);
extraTime = .9; extraMode.Read(new OutOfRangeReport());
Check(extraSink.PenActions.Count == 2, "A stale eraser stream must not delete on a late proximity-loss report");
extraSink.PenActions.RemoveAt(1);
extraMode.Read(new ToolReport(false)); Extra(1, 1000, 1600); Extra(1.05, 1000, 1600);
Extra(1.06, 1400, 1600, 8); Extra(1.09, 2600, 1600, 16);
Check(extraSink.PenActions.Last() == PenGestureAction.Undo &&
    extraEvents.TakeLast(3).SequenceEqual(["release", "flush", "Undo"]), "Flick actions must release the tip before shortcut dispatch");
Extra(1.11, 3000, 1600, 25); Extra(1.14, 3000, 0);
Check(extraSink.ClipboardActions.Count == 0 && extraSink.PenActions.Count == 2,
    "A captured flick must not leak later clipboard commands or repeat");
Extra(2, 3000, 1600); Extra(2.05, 3000, 1600); Extra(2.06, 2600, 1600, 8); Extra(2.09, 1400, 1600, 16);
Check(extraSink.PenActions.Last() == PenGestureAction.OpenAI && extraSink.ClipboardActions.Count == 0,
    "The AI flick must open its target without a clipboard action");
extraMode.Dispose();
Check(extraSink.PenCancels > 0, "Unloading must cancel delayed native actions");

Console.WriteLine($"Passed {checks} touch, tap, drag, pen-scroll, hover, system-gesture and pointer-output checks.");

sealed class TouchReport(TouchPoint[] points) : ITouchReport
{
    public byte[] Raw { get; set; } = [];
    public TouchPoint[] Touches { get; } = points;
}

sealed class RecordingPointer : IRelativePointer, IMouseScrollHandler, ISynchronousPointer
{
    public int Moves;
    public int Scrolls;
    public Vector2 LastPosition;
    public void SetPosition(Vector2 delta) { Moves++; LastPosition = delta; }
    public void ScrollVertically(int amount) => Scrolls++;
    public void ScrollHorizontally(int amount) => Scrolls++;
    public Action AfterFlush;
    public void Flush() { AfterFlush?.Invoke(); }
    public void Reset() { }
}

sealed class RecordingClicker : ITouchClicker
{
    public List<PenGestureAction> PenActions = [];
    public Action<PenGestureAction> PenEvent;
    public int PenCancels;
    public bool PenAction(PenGestureAction action, string target) {
        PenActions.Add(action); PenEvent?.Invoke(action); return true;
    }
    public void CancelPenActions() => PenCancels++;
    public List<PressureClipboardAction> ClipboardActions = [];
    public Action<PressureClipboardAction> ClipboardEvent;
    public int DictationCalls, DictationEnds;
    public bool BeginDictation(bool macParakeet) { DictationCalls++; return true; }
    public void EndDictation() { DictationEnds++; }
    public bool ClipboardShortcut(PressureClipboardAction action) { ClipboardActions.Add(action); ClipboardEvent?.Invoke(action); return true; }
    public List<TapButton> Buttons = [];
    public int DragStarts, DragMoves, DragEnds;
    public List<SwipeDirection> Swipes = [];
    public List<MagnifyPhase> Magnifications = [];
    public List<DesktopGesture> DesktopGestures = [];
    public void DesktopSwipe(DesktopGesture gesture) => DesktopGestures.Add(gesture);
    public void Swipe(SwipeDirection direction) => Swipes.Add(direction);
    public void Magnify(float amount, MagnifyPhase phase) => Magnifications.Add(phase);
    public void Click(TapButton button, double now) => Buttons.Add(button);
    public void BeginDrag(double now) => DragStarts++;
    public void MoveDrag(Vector2 delta) => DragMoves++;
    public void EndDrag() => DragEnds++;
}

sealed class PenReport(Vector2 position, bool near) : ITabletReport, IProximityReport, ITiltReport
{
    public byte[] Raw { get; set; } = [];
    public Vector2 Position { get; set; } = position;
    public uint Pressure { get; set; }
    public bool[] PenButtons { get; set; } = [false, false];
    public bool NearProximity { get; set; } = near;
    public Vector2 Tilt { get; set; }
    public uint HoverDistance { get; set; }
}

sealed class ReleaseMarker(List<string> events) : OpenTabletDriver.Plugin.Output.IPositionedPipelineElement<IDeviceReport>
{
    public OpenTabletDriver.Plugin.Output.PipelinePosition Position => OpenTabletDriver.Plugin.Output.PipelinePosition.PostTransform;
    public event Action<IDeviceReport> Emit;
    public void Consume(IDeviceReport report)
    {
        if (report is ITabletReport { Pressure: 0 }) events.Add("release");
        Emit?.Invoke(report);
    }
}

sealed class ToolReport(bool eraser) : IEraserReport {
    public byte[] Raw { get; set; } = [];
    public bool Eraser { get; set; } = eraser;
}
