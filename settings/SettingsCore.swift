import Foundation

struct PthPreferences: Equatable {
    var xSensitivity = 20.0, ySensitivity = 20.0, resetMs = 100.0
    var separateTouch = false
    var touchX = 20.0, touchY = 20.0, touchSlow = 0.9, touchFast = 1.35, touchSpeed = 150.0, hoverLimit = 0.0
    var touchScroll = 12.0, hoverDeadband = 0.0
    var tap = true, drag = true, natural = true, swipes = false, pinch = false
    var limitHover = true, rejectSpikes = false, middleTap = false, interactiveDesktop = false
    var desktopTravel = 60.0
    var joystick = false, joystickReverse = false, scrollNoise = false
    var joystickWidth = 20.0, joystickHeight = 20.0, joystickSpeedX = 600.0, joystickSpeedY = 1200.0
    var joystickDeadzone = 1.0, joystickCurve = 2.0
    var edgeBoost = 1.0, slowGain = 0.9, fastGain = 1.35, accelerationSpeed = 150.0
    var penAcceleration = false, touchAcceleration = false, filterAllPen = false
    var pressureCopy = false, pressurePaste = false
    var pressureRise = 8.0, pressureDrag = 1.5, dictationPressure = 60.0
    var pressureDictation = false, dictationTarget = "MacParakeet"
    var eraserDelete = false, desktopFlick = false, reverseFlick = false
    var flickPressure = 65.0, flickDistance = 6.0, flickSpeed = 100.0, flickTilt = 12.0
    var penScrollX = 0.5, penScrollY = 1.0, penScrollInvert = false, penScrollFreeze = true
    func applyingChanges(from baseline: PthPreferences, to edited: PthPreferences) -> PthPreferences {
        var result = self
        let numbers: [WritableKeyPath<PthPreferences, Double>] = [
            \.xSensitivity, \.ySensitivity, \.resetMs, \.touchScroll, \.hoverDeadband, \.penScrollX, \.penScrollY, \.desktopTravel, \.joystickWidth, \.joystickHeight, \.joystickSpeedX, \.joystickSpeedY, \.joystickDeadzone, \.joystickCurve, \.edgeBoost, \.slowGain, \.fastGain, \.accelerationSpeed, \.pressureRise, \.pressureDrag, \.dictationPressure, \.flickPressure, \.flickDistance, \.flickSpeed, \.flickTilt, \.touchX, \.touchY, \.touchSlow, \.touchFast, \.touchSpeed, \.hoverLimit]
        let toggles: [WritableKeyPath<PthPreferences, Bool>] = [
            \.tap, \.drag, \.natural, \.swipes, \.pinch, \.limitHover, \.rejectSpikes,
            \.middleTap, \.penScrollInvert, \.penScrollFreeze, \.interactiveDesktop, \.joystick, \.joystickReverse, \.scrollNoise, \.penAcceleration, \.touchAcceleration, \.filterAllPen, \.pressureCopy, \.pressurePaste, \.pressureDictation, \.eraserDelete, \.desktopFlick, \.reverseFlick, \.separateTouch]
        for key in numbers where baseline[keyPath: key] != edited[keyPath: key] { result[keyPath: key] = edited[keyPath: key] }
        for key in toggles where baseline[keyPath: key] != edited[keyPath: key] { result[keyPath: key] = edited[keyPath: key] }
        if baseline.dictationTarget != edited.dictationTarget { result.dictationTarget = edited.dictationTarget }
        return result
    }
    func matches(_ other: PthPreferences) -> Bool {
        let numbers = [(xSensitivity, other.xSensitivity), (ySensitivity, other.ySensitivity),
            (resetMs, other.resetMs), (touchScroll, other.touchScroll), (hoverDeadband, other.hoverDeadband),
            (penScrollX, other.penScrollX), (penScrollY, other.penScrollY), (desktopTravel, other.desktopTravel), (joystickWidth, other.joystickWidth), (joystickHeight, other.joystickHeight), (joystickSpeedX, other.joystickSpeedX), (joystickSpeedY, other.joystickSpeedY), (joystickDeadzone, other.joystickDeadzone), (joystickCurve, other.joystickCurve), (edgeBoost, other.edgeBoost), (slowGain, other.slowGain), (fastGain, other.fastGain), (accelerationSpeed, other.accelerationSpeed), (pressureRise, other.pressureRise), (pressureDrag, other.pressureDrag), (dictationPressure, other.dictationPressure), (flickPressure, other.flickPressure), (flickDistance, other.flickDistance), (flickSpeed, other.flickSpeed), (flickTilt, other.flickTilt), (touchX, other.touchX), (touchY, other.touchY), (touchSlow, other.touchSlow), (touchFast, other.touchFast), (touchSpeed, other.touchSpeed), (hoverLimit, other.hoverLimit)]
        return numbers.allSatisfy { abs($0.0 - $0.1) < 0.0001 } &&
            tap == other.tap && drag == other.drag && natural == other.natural && swipes == other.swipes &&
            pinch == other.pinch && limitHover == other.limitHover && rejectSpikes == other.rejectSpikes &&
            separateTouch == other.separateTouch && eraserDelete == other.eraserDelete && desktopFlick == other.desktopFlick && reverseFlick == other.reverseFlick &&
            dictationTarget == other.dictationTarget && pressureDictation == other.pressureDictation && pressureCopy == other.pressureCopy && pressurePaste == other.pressurePaste && filterAllPen == other.filterAllPen && penAcceleration == other.penAcceleration && touchAcceleration == other.touchAcceleration && middleTap == other.middleTap && joystick == other.joystick && joystickReverse == other.joystickReverse && scrollNoise == other.scrollNoise && interactiveDesktop == other.interactiveDesktop && penScrollInvert == other.penScrollInvert && penScrollFreeze == other.penScrollFreeze
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
        p.interactiveDesktop = values["InteractiveDesktopSwipes"] as? Bool ?? p.interactiveDesktop
        p.desktopTravel = Self.number(values["DesktopSwipeDistanceMm"], p.desktopTravel)
        p.joystickWidth = Self.number(values["JoystickHalfWidthMm"], p.joystickWidth)
        p.joystickHeight = Self.number(values["JoystickHalfHeightMm"], p.joystickHeight)
        p.joystickSpeedX = Self.number(values["JoystickMaxSpeedX"], p.joystickSpeedX)
        p.joystickSpeedY = Self.number(values["JoystickMaxSpeedY"], p.joystickSpeedY)
        p.joystickDeadzone = Self.number(values["JoystickDeadzoneMm"], p.joystickDeadzone)
        p.joystickCurve = Self.number(values["JoystickExponent"], p.joystickCurve)
        p.edgeBoost = Self.number(values["JoystickEdgeBoost"], p.edgeBoost)
        p.slowGain = Self.number(values["AccelerationSlowGain"], p.slowGain)
        p.fastGain = Self.number(values["AccelerationFastGain"], p.fastGain)
        p.accelerationSpeed = Self.number(values["AccelerationTransitionSpeed"], p.accelerationSpeed)
        p.eraserDelete = values["EraserDeleteAfterContact"] as? Bool ?? p.eraserDelete
        p.desktopFlick = values["PenFlickDesktopSwitching"] as? Bool ?? p.desktopFlick
        p.reverseFlick = values["ReverseFlickDirections"] as? Bool ?? p.reverseFlick
        p.flickPressure = Self.number(values["FlickPressurePercent"], p.flickPressure)
        p.flickDistance = Self.number(values["FlickDistanceMm"], p.flickDistance)
        p.flickSpeed = Self.number(values["FlickSpeed"], p.flickSpeed)
        p.flickTilt = Self.number(values["FlickTiltPercent"], p.flickTilt)
        p.pressureDictation = values["PressureDictationEnabled"] as? Bool ?? p.pressureDictation
        p.dictationTarget = values["PressureDictationTarget"] as? String ?? p.dictationTarget
        p.dictationPressure = Self.number(values["PressureDictationPercent"], p.dictationPressure)
        p.pressureCopy = values["PressureCopySelection"] as? Bool ?? p.pressureCopy
        p.pressurePaste = values["PressureDoublePaste"] as? Bool ?? p.pressurePaste
        p.pressureRise = Self.number(values["PressureRisePercent"], p.pressureRise)
        p.pressureDrag = Self.number(values["PressureDragDistanceMm"], p.pressureDrag)
        p.filterAllPen = values["FilterAllPenMovement"] as? Bool ?? p.filterAllPen
        p.penAcceleration = values["PenVelocityAcceleration"] as? Bool ?? p.penAcceleration
        p.separateTouch = values["SeparateTouchSettings"] as? Bool ?? p.separateTouch
        p.touchX = Self.number(values["TouchSensitivityX"], p.xSensitivity)
        p.touchY = Self.number(values["TouchSensitivityY"], p.ySensitivity)
        p.touchSlow = Self.number(values["TouchSlowGain"], p.slowGain)
        p.touchFast = Self.number(values["TouchFastGain"], p.fastGain)
        p.touchSpeed = Self.number(values["TouchAccelerationSpeed"], p.accelerationSpeed)
        p.hoverLimit = Self.number(values["HoverDistanceLimit"], p.hoverLimit)
        p.touchAcceleration = values["TouchVelocityAcceleration"] as? Bool ?? p.touchAcceleration
        p.joystick = values["PenJoystickScrolling"] as? Bool ?? p.joystick
        p.joystickReverse = values["JoystickInvertScroll"] as? Bool ?? p.joystickReverse
        p.scrollNoise = values["FilterPenScrollNoise"] as? Bool ?? p.scrollNoise
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
            (p.penScrollX, 0.0, 100.0), (p.penScrollY, 0.0, 100.0), (p.desktopTravel, 20.0, 160.0), (p.joystickWidth, 1.0, 100.0), (p.joystickHeight, 1.0, 100.0), (p.joystickSpeedX, 0.0, 100000.0), (p.joystickSpeedY, 0.0, 100000.0), (p.joystickDeadzone, 0.0, 90.0), (p.joystickCurve, 1.0, 12.0), (p.edgeBoost, 1.0, 10.0), (p.slowGain, 0.2, 2.0), (p.fastGain, 0.2, 5.0), (p.accelerationSpeed, 10.0, 1000.0), (p.pressureRise, 5.0, 50.0), (p.pressureDrag, 0.5, 10.0), (p.dictationPressure, 20.0, 95.0), (p.flickPressure, 30.0, 95.0), (p.flickDistance, 2.0, 30.0), (p.flickSpeed, 30.0, 1000.0), (p.flickTilt, 2.0, 100.0), (p.touchX, 0.1, 200.0), (p.touchY, 0.1, 200.0), (p.touchSlow, 0.2, 2.0), (p.touchFast, 0.2, 5.0), (p.touchSpeed, 10.0, 1000.0), (p.hoverLimit, 0.0, 63.0)] {
            guard value.isFinite && value >= low && value <= high else {
                throw SettingsError.message("A number is outside the supported range. Check the sensitivity and stability fields.")
            }
        }
        guard ["MacParakeet", "macOS"].contains(p.dictationTarget) else {
            throw SettingsError.message("Choose MacParakeet or macOS as the dictation target.")
        }
        guard p.touchFast >= p.touchSlow else {
            throw SettingsError.message("Touch fast sensitivity must be at least its slow value.")
        }
        guard p.fastGain >= p.slowGain else {
            throw SettingsError.message("Fast movement sensitivity must be at least the slow movement sensitivity.")
        }
        guard p.joystickDeadzone < min(p.joystickWidth, p.joystickHeight) else {
            throw SettingsError.message("The joystick centre deadzone must be smaller than its reach.")
        }
        guard !p.middleTap || hasPenScroll || p.joystick else {
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
            "SeparateTouchSettings": p.separateTouch, "TouchSensitivityX": p.touchX, "TouchSensitivityY": p.touchY,
            "TouchSlowGain": p.touchSlow, "TouchFastGain": p.touchFast, "TouchAccelerationSpeed": p.touchSpeed,
            "HoverDistanceLimit": p.hoverLimit,
            "ScrollSensitivity": p.touchScroll, "HoverJitterDeadband": p.hoverDeadband,
            "TapToClick": p.tap, "TapAndDrag": p.drag, "NaturalScrolling": p.natural,
            "ThreeFingerSwipes": p.swipes, "PinchZoom": p.pinch, "LimitUnstableHover": p.limitHover,
            "RejectHoverSpikes": p.rejectSpikes, "UpperButtonTapMiddleClick": p.middleTap,
            "InteractiveDesktopSwipes": p.interactiveDesktop, "DesktopSwipeDistanceMm": p.desktopTravel,
            "JoystickHalfWidthMm": p.joystickWidth, "JoystickHalfHeightMm": p.joystickHeight, "JoystickMaxSpeedX": p.joystickSpeedX, "JoystickMaxSpeedY": p.joystickSpeedY, "JoystickDeadzoneMm": p.joystickDeadzone, "JoystickExponent": p.joystickCurve,
            "EraserDeleteAfterContact": p.eraserDelete, "PenFlickDesktopSwitching": p.desktopFlick, "ReverseFlickDirections": p.reverseFlick,
            "FlickPressurePercent": p.flickPressure,
            "FlickDistanceMm": p.flickDistance, "FlickSpeed": p.flickSpeed, "FlickTiltPercent": p.flickTilt,
            "PressureDictationEnabled": p.pressureDictation, "PressureDictationTarget": p.dictationTarget, "PressureDictationPercent": p.dictationPressure,
            "PressureCopySelection": p.pressureCopy, "PressureDoublePaste": p.pressurePaste,
            "PressureRisePercent": p.pressureRise, "PressureDragDistanceMm": p.pressureDrag,
            "FilterAllPenMovement": p.filterAllPen, "JoystickEdgeBoost": p.edgeBoost, "PenVelocityAcceleration": p.penAcceleration, "TouchVelocityAcceleration": p.touchAcceleration,
            "AccelerationSlowGain": p.slowGain, "AccelerationFastGain": p.fastGain, "AccelerationTransitionSpeed": p.accelerationSpeed,
            "PenJoystickScrolling": p.joystick, "JoystickInvertScroll": p.joystickReverse, "FilterPenScrollNoise": p.scrollNoise]).filter { !["AIPasteSelection", "EraserDeletesSelection", "AITarget", "RightFlickUndo", "LeftFlickAI"].contains($0["Property"] as? String ?? "") }
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
