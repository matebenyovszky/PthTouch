import SwiftUI
import AppKit

@main
struct PthTouchSettingsApp: App {
    @StateObject private var model = SettingsModel()
    var body: some Scene {
        Window("PthTouch Settings", id: "settings") {
            SettingsView(model: model).onAppear { model.reload() }
        }
        .windowResizability(.contentSize)
        .commands { CommandGroup(replacing: .newItem) {} }
    }
}

final class SettingsModel: ObservableObject {
    @Published var values = PthPreferences()
    @Published var tablet = ""
    @Published var hasPenScroll = false
    @Published var busy = false
    @Published var loaded = false
    @Published var failed = false
    @Published var status = "Loading settings…"
    private var baseline = PthPreferences()
    private static var settingsURL: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/OpenTabletDriver/settings.json")
    }
    static func live() throws -> [String: Any] {
        guard let root = try DaemonRPC.call("GetSettings") as? [String: Any] else {
            throw SettingsError.message("The driver returned an invalid configuration.")
        }
        return root
    }
    func reload() {
        guard !busy else { return }
        busy = true; failed = false
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            do {
                let root: [String: Any]
                var offline = false
                do { root = try Self.live() }
                catch {
                    root = try JSONSerialization.jsonObject(with: Data(contentsOf: Self.settingsURL)) as? [String: Any] ?? [:]
                    offline = true
                }
                let profile = try ProfileSettings(document: root)
                DispatchQueue.main.async {
                    self?.values = profile.read(); self?.baseline = profile.read(); self?.tablet = profile.name
                    self?.hasPenScroll = profile.hasPenScroll; self?.loaded = true; self?.busy = false
                    self?.status = offline ? "Saved settings loaded. Start OpenTabletDriver before applying." : "Current driver settings loaded."
                }
            } catch {
                DispatchQueue.main.async { self?.busy = false; self?.failed = true; self?.status = error.localizedDescription }
            }
        }
    }
    func apply() {
        guard !busy && loaded else { return }
        let desired = values, tablet = tablet, baseline = baseline
        busy = true; failed = false; status = "Saving and applying…"
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            do {
                let actual = try Self.save(desired, baseline: baseline, tablet: tablet)
                DispatchQueue.main.async {
                    self?.values = actual; self?.baseline = actual; self?.busy = false; self?.status = "Saved and applied."
                }
            } catch {
                DispatchQueue.main.async { self?.busy = false; self?.failed = true; self?.status = error.localizedDescription }
            }
        }
    }
    private static func save(_ edited: PthPreferences, baseline: PthPreferences, tablet: String) throws -> PthPreferences {
        // Re-read live settings at Apply time; change only the selected mode's
        // controls so unrelated edits in the main driver window survive.
        let original = try live()
        let profile = try ProfileSettings(document: original, tablet: tablet)
        let desired = profile.read().applyingChanges(from: baseline, to: edited)
        let merged = try profile.merging(desired)
        let oldData = try JSONSerialization.data(withJSONObject: original, options: [.prettyPrinted, .sortedKeys])
        let backups = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Application Support/PthTouchSettings/Backups", isDirectory: true)
        try FileManager.default.createDirectory(at: backups, withIntermediateDirectories: true)
        try oldData.write(to: backups.appendingPathComponent("settings-\(UUID().uuidString).json"), options: .atomic)
        do {
            _ = try DaemonRPC.call("SetSettings", params: [merged])
            let actual = try live()
            let result = try ProfileSettings(document: actual, tablet: tablet).read()
            guard result.matches(desired) else { throw SettingsError.message("The driver did not accept all settings.") }
            let data = try JSONSerialization.data(withJSONObject: actual, options: [.prettyPrinted, .sortedKeys])
            try data.write(to: settingsURL, options: .atomic)
            _ = try DaemonRPC.call("ForceResynchronize")
            return result
        } catch {
            _ = try? DaemonRPC.call("SetSettings", params: [original])
            try? oldData.write(to: settingsURL, options: .atomic)
            _ = try? DaemonRPC.call("ForceResynchronize")
            throw error
        }
    }
}

struct SettingsView: View {
    @ObservedObject var model: SettingsModel
    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack(spacing: 12) {
                Image(systemName: "hand.draw.fill").font(.system(size: 30)).foregroundColor(.accentColor)
                VStack(alignment: .leading, spacing: 3) {
                    Text("PthTouch").font(.title2.bold())
                    Text(model.tablet.isEmpty ? "Tablet settings" : model.tablet).foregroundColor(.secondary)
                }
                Spacer()
                Button("Open driver") {
                    NSWorkspace.shared.open(URL(fileURLWithPath: "/Applications/OpenTabletDriver.app"))
                }
            }
            TabView {
                Form {
                    Section("Relative movement") {
                        number("X sensitivity", value: $model.values.xSensitivity, unit: "px/mm")
                        number("Y sensitivity", value: $model.values.ySensitivity, unit: "px/mm")
                        number("Reset after a pause", value: $model.values.resetMs, unit: "ms")
                    }
                    Section("Velocity-based sensitivity (optional)") {
                        Toggle("Enable for pen hover", isOn: $model.values.penAcceleration)
                        Toggle("Enable for one-finger touch", isOn: $model.values.touchAcceleration)
                        number("Slow movement multiplier", value: $model.values.slowGain, unit: "×")
                        number("Fast movement multiplier", value: $model.values.fastGain, unit: "×")
                        number("Full acceleration at", value: $model.values.accelerationSpeed, unit: "mm/s")
                        Text("Multiplies the X/Y sensitivity above. Slow motion is precise; fast motion travels farther. Pen contact, buttons, touch dragging and scrolling keep their normal behaviour. Both switches off restore fixed sensitivity.")
                            .font(.caption).foregroundColor(.secondary)
                    }
                    Section("Upper pen button") {
                        Picker("Scrolling mode", selection: $model.values.joystick) {
                            Text("Drag scroll").tag(false)
                            Text("Joystick autoscroll").tag(true)
                        }
                        .pickerStyle(.segmented)
                        Toggle("Short press opens links with a middle click", isOn: $model.values.middleTap)
                            .disabled(!model.hasPenScroll && !model.values.joystick)
                        Text("Release without moving to click. Hold and move the pen to scroll.")
                            .font(.caption).foregroundColor(.secondary)
                        if model.values.joystick {
                            number("Horizontal reach from centre", value: $model.values.joystickWidth, unit: "mm")
                            number("Vertical reach from centre", value: $model.values.joystickHeight, unit: "mm")
                            number("Horizontal scroll speed", value: $model.values.joystickSpeedX, unit: "px/s")
                            number("Vertical scroll speed", value: $model.values.joystickSpeedY, unit: "px/s")
                            number("Centre deadzone", value: $model.values.joystickDeadzone, unit: "mm")
                            number("Response curve (1–12)", value: $model.values.joystickCurve, unit: "")
                            number("Edge speed boost (1–10)", value: $model.values.edgeBoost, unit: "×")
                            Text("Edge boost increases speed smoothly in the outer 35% of the field. Final edge speed = scroll speed × boost. A higher response curve keeps the centre slower; it does not raise the edge speed.")
                                .font(.caption).foregroundColor(.secondary)
                            Toggle("Reverse joystick scrolling", isOn: $model.values.joystickReverse)
                            Text("Where you press becomes the centre. A 20 mm reach each way makes a 40 × 40 mm field. Hold at an edge to keep scrolling; return to centre or release to stop.")
                                .font(.caption).foregroundColor(.secondary)
                        } else {
                            Group {
                                number("Horizontal scroll speed", value: $model.values.penScrollX, unit: "")
                                number("Vertical scroll speed", value: $model.values.penScrollY, unit: "")
                                Toggle("Reverse pen scrolling", isOn: $model.values.penScrollInvert)
                                Toggle("Keep cursor in place while scrolling", isOn: $model.values.penScrollFreeze)
                            }.disabled(!model.hasPenScroll)
                            if !model.hasPenScroll {
                                Text("For drag scrolling, assign Pen Scroll and Scroll Binding Filter in OpenTabletDriver. Joystick autoscroll is available directly.")
                                    .font(.caption).foregroundColor(.secondary)
                            }
                        }
                    }
                }.formStyle(.grouped).tabItem { Label("Pen", systemImage: "pencil.tip") }
                Form {
                    Section("Touch") {
                        Toggle("Tap to click", isOn: $model.values.tap)
                        Toggle("Tap again and hold to drag", isOn: $model.values.drag).disabled(!model.values.tap)
                        Toggle("Natural scrolling", isOn: $model.values.natural)
                        number("Two-finger scroll speed", value: $model.values.touchScroll, unit: "px/mm")
                    }
                    Section("Gestures") {
                        Toggle("Three-finger system swipes", isOn: $model.values.swipes)
                        Text("Left/right: Spaces · Up: Mission Control · Down: app windows")
                            .font(.caption).foregroundColor(.secondary)
                        Toggle("Desktop animation follows fingers (experimental)", isOn: $model.values.interactiveDesktop)
                            .disabled(!model.values.swipes || ProcessInfo.processInfo.operatingSystemVersion.majorVersion >= 27)
                        number("Travel for a full desktop", value: $model.values.desktopTravel, unit: "mm")
                            .disabled(!model.values.swipes || !model.values.interactiveDesktop)
                        Text("Drag horizontally to preview, reverse to return, then lift to settle. Interactive animation supports macOS 13–26.")
                            .font(.caption).foregroundColor(.secondary)
                        Toggle("Pinch to zoom (experimental)", isOn: $model.values.pinch)
                    }
                }.formStyle(.grouped).tabItem { Label("Touch & gestures", systemImage: "hand.point.up.left") }
                Form {
                    Section("Pen stability") {
                        Toggle("Ignore unstable far hover", isOn: $model.values.limitHover)
                        number("Hover deadband", value: $model.values.hoverDeadband, unit: "mm")
                        Toggle("Reject isolated hover spikes", isOn: $model.values.rejectSpikes)
                        Toggle("Filter all pen movement (including dragging)", isOn: $model.values.filterAllPen)
                            .disabled(model.values.hoverDeadband <= 0)
                        Toggle("Apply noise filtering while pen-scrolling", isOn: $model.values.scrollNoise)
                            .disabled(model.values.hoverDeadband <= 0 || model.values.filterAllPen)
                        Text("Suggested starting point: 0.10 mm with spike rejection. Zero disables the deadband and spike filter. General filtering also covers tip contact, held buttons, dragging and pen scrolling, with one shared coordinate filter. Otherwise, filtering applies to hover and optionally pen scrolling.")
                            .font(.caption).foregroundColor(.secondary)
                    }
                }.formStyle(.grouped).tabItem { Label("Stability", systemImage: "waveform.path") }
            }
            .disabled(model.busy || !model.loaded)
            HStack(alignment: .center, spacing: 12) {
                if model.busy { ProgressView().controlSize(.small) }
                Text(model.status).font(.caption).foregroundColor(model.failed ? .red : .secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                Button("Reload") { model.reload() }.disabled(model.busy)
                Button("Save & apply") { model.apply() }
                    .keyboardShortcut(.defaultAction).disabled(model.busy || !model.loaded)
            }
        }
        .padding(20).frame(width: 550, height: 590)
    }
    private func number(_ title: String, value: Binding<Double>, unit: String) -> some View {
        HStack {
            Text(title)
            Spacer()
            TextField(title, value: value, format: .number.precision(.fractionLength(0...3)))
                .labelsHidden().multilineTextAlignment(.trailing).frame(width: 78)
            Text(unit).font(.caption).foregroundColor(.secondary).frame(width: 46, alignment: .leading)
        }
    }
}
