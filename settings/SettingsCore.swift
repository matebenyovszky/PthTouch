import Foundation

struct PthPreferences: Equatable {
    var xSensitivity = 20.0, ySensitivity = 20.0, resetMs = 100.0
    var touchScroll = 12.0, hoverDeadband = 0.0
    var tap = true, drag = true, natural = true, swipes = false, pinch = false
    var limitHover = true, rejectSpikes = false, middleTap = false
    var penScrollX = 0.5, penScrollY = 1.0, penScrollInvert = false, penScrollFreeze = true
    func applyingChanges(from baseline: PthPreferences, to edited: PthPreferences) -> PthPreferences {
        var result = self
        let numbers: [WritableKeyPath<PthPreferences, Double>] = [
            \.xSensitivity, \.ySensitivity, \.resetMs, \.touchScroll, \.hoverDeadband, \.penScrollX, \.penScrollY]
        let toggles: [WritableKeyPath<PthPreferences, Bool>] = [
            \.tap, \.drag, \.natural, \.swipes, \.pinch, \.limitHover, \.rejectSpikes,
            \.middleTap, \.penScrollInvert, \.penScrollFreeze]
        for key in numbers where baseline[keyPath: key] != edited[keyPath: key] { result[keyPath: key] = edited[keyPath: key] }
        for key in toggles where baseline[keyPath: key] != edited[keyPath: key] { result[keyPath: key] = edited[keyPath: key] }
        return result
    }
    func matches(_ other: PthPreferences) -> Bool {
        let numbers = [(xSensitivity, other.xSensitivity), (ySensitivity, other.ySensitivity),
            (resetMs, other.resetMs), (touchScroll, other.touchScroll), (hoverDeadband, other.hoverDeadband),
            (penScrollX, other.penScrollX), (penScrollY, other.penScrollY)]
        return numbers.allSatisfy { abs($0.0 - $0.1) < 0.0001 } &&
            tap == other.tap && drag == other.drag && natural == other.natural && swipes == other.swipes &&
            pinch == other.pinch && limitHover == other.limitHover && rejectSpikes == other.rejectSpikes &&
            middleTap == other.middleTap && penScrollInvert == other.penScrollInvert && penScrollFreeze == other.penScrollFreeze
    }
}

struct ProfileSettings {
    static let modePath = "PthTouch.TrackpadRelativeMode"
    var document: [String: Any]
    var index: Int
    var name: String { profiles[index]["Tablet"] as? String ?? "Tablet" }
    var profiles: [[String: Any]] { document["Profiles"] as? [[String: Any]] ?? [] }
    var hasPenScroll: Bool {
        let bindings = profiles[index]["Bindings"] as? [String: Any]
        let buttons = bindings?["PenButtons"] as? [Any] ?? []
        return buttons.count > 1 && (buttons[1] as? [String: Any])?["Path"] as? String == "ScrollBinding.DragScrollBinding"
    }
    init(document: [String: Any], tablet: String? = nil) throws {
        self.document = document
        let profiles = document["Profiles"] as? [[String: Any]] ?? []
        guard let index = profiles.firstIndex(where: { profile in
            let mode = profile["OutputMode"] as? [String: Any]
            return mode?["Path"] as? String == Self.modePath &&
                (tablet == nil || profile["Tablet"] as? String == tablet)
        }) else { throw SettingsError.message("Select PTH Touch Relative Mode in OpenTabletDriver first.") }
        self.index = index
    }
    func read() -> PthPreferences {
        let profile = profiles[index]
        let mode = profile["OutputMode"] as? [String: Any] ?? [:]
        let values = Self.values(mode["Settings"])
        let relative = profile["RelativeModeSettings"] as? [String: Any] ?? [:]
        var p = PthPreferences()
        p.xSensitivity = Self.number(relative["XSensitivity"], p.xSensitivity)
        p.ySensitivity = Self.number(relative["YSensitivity"], p.ySensitivity)
        if let duration = relative["RelativeResetDelay"] as? String {
            let parts = duration.split(separator: ":").compactMap { Double($0) }
            if parts.count == 3 { p.resetMs = (parts[0] * 3600 + parts[1] * 60 + parts[2]) * 1000 }
        }
        p.touchScroll = Self.number(values["ScrollSensitivity"], p.touchScroll)
        p.hoverDeadband = Self.number(values["HoverJitterDeadband"], p.hoverDeadband)
        p.tap = values["TapToClick"] as? Bool ?? p.tap
        p.drag = values["TapAndDrag"] as? Bool ?? p.drag
        p.natural = values["NaturalScrolling"] as? Bool ?? p.natural
        p.swipes = values["ThreeFingerSwipes"] as? Bool ?? p.swipes
        p.pinch = values["PinchZoom"] as? Bool ?? p.pinch
        p.limitHover = values["LimitUnstableHover"] as? Bool ?? p.limitHover
        p.rejectSpikes = values["RejectHoverSpikes"] as? Bool ?? p.rejectSpikes
        p.middleTap = values["UpperButtonTapMiddleClick"] as? Bool ?? p.middleTap
        if hasPenScroll, let bindings = profile["Bindings"] as? [String: Any],
           let buttons = bindings["PenButtons"] as? [Any], let upper = buttons[1] as? [String: Any] {
            let pen = Self.values(upper["Settings"])
            p.penScrollX = Self.number(pen["XSensitivity"], p.penScrollX)
            p.penScrollY = Self.number(pen["YSensitivity"], p.penScrollY)
            p.penScrollInvert = pen["InvertScroll"] as? Bool ?? p.penScrollInvert
            p.penScrollFreeze = pen["FrozenCursor"] as? Bool ?? p.penScrollFreeze
        }
        return p
    }
    func merging(_ p: PthPreferences) throws -> [String: Any] {
        for (value, low, high) in [(p.xSensitivity, 0.1, 200.0), (p.ySensitivity, 0.1, 200.0),
            (p.resetMs, 1.0, 5000.0), (p.touchScroll, 0.1, 100.0), (p.hoverDeadband, 0.0, 1.0),
            (p.penScrollX, 0.0, 100.0), (p.penScrollY, 0.0, 100.0)] {
            guard value.isFinite && value >= low && value <= high else {
                throw SettingsError.message("A number is outside the supported range. Check the sensitivity and stability fields.")
            }
        }
        guard !p.middleTap || hasPenScroll else {
            throw SettingsError.message("Bind the upper pen button to Pen Scroll with Scroll Binding Filter in OpenTabletDriver first.")
        }
        var root = document, list = profiles, profile = list[index]
        var relative = profile["RelativeModeSettings"] as? [String: Any] ?? [:]
        relative["XSensitivity"] = p.xSensitivity; relative["YSensitivity"] = p.ySensitivity
        let ticks = Int64((p.resetMs * 10_000).rounded())
        let seconds = ticks / 10_000_000
        let fraction = String(ticks % 10_000_000)
        relative["RelativeResetDelay"] = "00:00:" + (seconds < 10 ? "0" : "") + String(seconds) + "." +
            String(repeating: "0", count: 7 - fraction.count) + fraction
        profile["RelativeModeSettings"] = relative
        var mode = profile["OutputMode"] as? [String: Any] ?? [:]
        mode["Settings"] = Self.updated(mode["Settings"], with: [
            "ScrollSensitivity": p.touchScroll, "HoverJitterDeadband": p.hoverDeadband,
            "TapToClick": p.tap, "TapAndDrag": p.drag, "NaturalScrolling": p.natural,
            "ThreeFingerSwipes": p.swipes, "PinchZoom": p.pinch, "LimitUnstableHover": p.limitHover,
            "RejectHoverSpikes": p.rejectSpikes, "UpperButtonTapMiddleClick": p.middleTap])
        profile["OutputMode"] = mode
        if hasPenScroll, var bindings = profile["Bindings"] as? [String: Any],
           var buttons = bindings["PenButtons"] as? [Any], var upper = buttons[1] as? [String: Any] {
            upper["Settings"] = Self.updated(upper["Settings"], with: [
                "XSensitivity": p.penScrollX, "YSensitivity": p.penScrollY,
                "InvertScroll": p.penScrollInvert, "FrozenCursor": p.penScrollFreeze])
            buttons[1] = upper
            bindings["PenButtons"] = buttons; profile["Bindings"] = bindings
        }
        list[index] = profile; root["Profiles"] = list
        return root
    }
    private static func number(_ value: Any?, _ fallback: Double) -> Double {
        (value as? NSNumber)?.doubleValue ?? fallback
    }
    private static func values(_ raw: Any?) -> [String: Any] {
        var result: [String: Any] = [:]
        for setting in raw as? [[String: Any]] ?? [] {
            if let key = setting["Property"] as? String { result[key] = setting["Value"] }
        }
        return result
    }
    private static func updated(_ raw: Any?, with updates: [String: Any]) -> [[String: Any]] {
        var values = raw as? [[String: Any]] ?? []
        for (key, value) in updates {
            if let i = values.firstIndex(where: { $0["Property"] as? String == key }) { values[i]["Value"] = value }
            else { values.append(["Property": key, "Value": value]) }
        }
        return values
    }
}
