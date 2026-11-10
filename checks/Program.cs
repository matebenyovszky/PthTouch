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

Console.WriteLine($"Passed {checks} touch, tap, drag, pen-scroll, hover and pointer-output checks.");

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
    public void Flush() { }
    public void Reset() { }
}

sealed class RecordingClicker : ITouchClicker
{
    public List<TapButton> Buttons = [];
    public int DragStarts, DragMoves, DragEnds;
    public void Click(TapButton button, double now) => Buttons.Add(button);
    public void BeginDrag(double now) => DragStarts++;
    public void MoveDrag(Vector2 delta) => DragMoves++;
    public void EndDrag() => DragEnds++;
}

sealed class PenReport(Vector2 position, bool near) : ITabletReport, IProximityReport
{
    public byte[] Raw { get; set; } = [];
    public Vector2 Position { get; set; } = position;
    public uint Pressure { get; set; }
    public bool[] PenButtons { get; set; } = [false, false];
    public bool NearProximity { get; set; } = near;
    public uint HoverDistance { get; set; }
}
