import Foundation
import Darwin

@main
struct SettingsChecks {
    static var count = 0
    static func check(_ value: Bool, _ message: String) {
        precondition(value, message); count += 1
    }
    static func main() throws {
        let profile: [String: Any] = [
            "Tablet": "Test tablet", "OutputMode": ["Path": ProfileSettings.modePath,
                "Settings": [["Property": "FutureSetting", "Value": "keep"]]],
            "RelativeModeSettings": ["XSensitivity": 20, "YSensitivity": 20,
                "RelativeRotation": 17, "RelativeResetDelay": "00:00:00.1000000"],
            "Bindings": ["TipActivationThreshold": 1, "PenButtons": [NSNull(),
                ["Path": "ScrollBinding.DragScrollBinding", "Enable": true,
                 "Settings": [["Property": "KineticSetting", "Value": "keep"]]]]],
            "AbsoluteModeSettings": ["EnableClipping": true], "CustomProfileField": "keep"]
        let other: [String: Any] = ["Tablet": "Other tablet", "CustomField": "untouched"]
        let original: [String: Any] = ["Revision": "test", "Profiles": [profile, other], "FutureRoot": "keep"]
        let document = try ProfileSettings(document: original)
        check(document.hasPenScroll, "An unassigned lower button must not hide an assigned upper scroll button")
        var preferences = document.read()
        check(preferences.xSensitivity == 20 && preferences.resetMs == 100, "Load numeric values and reset time")
        preferences.xSensitivity = 22; preferences.swipes = true; preferences.middleTap = true
        preferences.resetMs = 250; preferences.penScrollY = 1.25
        preferences.interactiveDesktop = true; preferences.desktopTravel = 80
        preferences.joystick = true; preferences.joystickWidth = 25; preferences.joystickHeight = 30
        preferences.edgeBoost = 4; preferences.joystickCurve = 9
        preferences.filterAllPen = true; preferences.penAcceleration = true; preferences.touchAcceleration = true
        preferences.slowGain = 0.8; preferences.fastGain = 1.6; preferences.accelerationSpeed = 200
        preferences.joystickSpeedY = 50000; preferences.joystickReverse = true; preferences.scrollNoise = true
        let merged = try document.merging(preferences)
        let roundTrip = try ProfileSettings(document: merged).read()
        check(roundTrip.matches(preferences), "Save/reload must preserve every exposed control")
        check(merged["FutureRoot"] as? String == "keep", "Preserve unrelated root fields")
        let profiles = merged["Profiles"] as! [[String: Any]], selected = profiles[0]
        check(profiles[1]["CustomField"] as? String == "untouched", "Preserve other tablet profiles")
        check(selected["CustomProfileField"] as? String == "keep", "Preserve unknown profile fields")
        check((selected["RelativeModeSettings"] as! [String: Any])["RelativeRotation"] as? Int == 17, "Preserve relative rotation")
        let mode = selected["OutputMode"] as! [String: Any]
        check((mode["Settings"] as! [[String: Any]]).contains { $0["Property"] as? String == "FutureSetting" && $0["Value"] as? String == "keep" }, "Preserve unknown plugin options")
        let buttons = (selected["Bindings"] as! [String: Any])["PenButtons"] as! [Any]
        check(buttons[0] is NSNull, "Preserve unassigned lower pen button")
        let upper = buttons[1] as! [String: Any]
        check((upper["Settings"] as! [[String: Any]]).contains { $0["Property"] as? String == "KineticSetting" }, "Preserve unrelated upper-button options")
        var invalid = preferences; invalid.hoverDeadband = .nan
        do { _ = try document.merging(invalid); check(false, "Reject non-finite input") } catch { check(true, "Rejected non-finite input") }
        var invertedGain = preferences; invertedGain.slowGain = 1.8; invertedGain.fastGain = 1.2
        do { _ = try document.merging(invertedGain); check(false, "Reject inverted acceleration range") } catch { check(true, "Rejected inverted acceleration range") }
        let baseline = PthPreferences(); var external = baseline, edited = baseline
        external.xSensitivity = 35; external.desktopTravel = 90; edited.pinch = true
        let combined = external.applyingChanges(from: baseline, to: edited)
        check(combined.xSensitivity == 35 && combined.desktopTravel == 90 && combined.pinch, "Merge edits with concurrent changes from the driver window")
        do { _ = try ProfileSettings(document: ["Profiles": [other]]); check(false, "Reject incompatible mode") } catch { check(true, "Rejected incompatible mode") }
        let response = try mockRPC(error: false)
        check((response as? [String: Any])?["answer"] as? Int == 42, "Ignore notifications/unrelated responses before the matching RPC result")
        do { _ = try mockRPC(error: true); check(false, "RPC errors must propagate") } catch { check(error.localizedDescription == "test rejection", "Preserve the RPC error message") }
        print("Passed \(count) settings preservation, concurrent-edit and local RPC checks; no driver connection or input events used.")
    }

    static func mockRPC(error: Bool) throws -> Any {
        let path = "/private/tmp/pth-rpc-\(UUID().uuidString.prefix(16)).sock"
        let server = socket(AF_UNIX, SOCK_STREAM, 0)
        guard server >= 0 else { throw SettingsError.message("mock socket") }
        defer { close(server); unlink(path) }
        var address = sockaddr_un(); address.sun_family = sa_family_t(AF_UNIX)
        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        let bytes = Array(path.utf8) + [UInt8(0)]
        withUnsafeMutablePointer(to: &address.sun_path) { pointer in
            bytes.withUnsafeBytes { _ = memcpy(UnsafeMutableRawPointer(pointer), $0.baseAddress!, bytes.count) }
        }
        let bound = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(server, $0, socklen_t(MemoryLayout<sockaddr_un>.size)) }
        }
        guard bound == 0, listen(server, 1) == 0 else { throw SettingsError.message("mock bind") }
        let done = DispatchSemaphore(value: 0)
        Thread.detachNewThread {
            let client = accept(server, nil, nil)
            guard client >= 0 else { done.signal(); return }
            defer { close(client); done.signal() }
            var request = [UInt8](repeating: 0, count: 8192)
            _ = read(client, &request, request.count)
            let messages: [[String: Any]] = [
                ["jsonrpc": "2.0", "method": "Message", "params": ["connection notice"]],
                ["jsonrpc": "2.0", "id": 99, "result": NSNull()],
                error ? ["jsonrpc": "2.0", "id": 1, "error": ["code": -1, "message": "test rejection"]]
                      : ["jsonrpc": "2.0", "id": 1, "result": ["answer": 42]]]
            for message in messages {
                let body = try! JSONSerialization.data(withJSONObject: message)
                var frame = Data("Content-Length: \(body.count)\r\n\r\n".utf8); frame.append(body)
                frame.withUnsafeBytes { buffer in
                    var offset = 0
                    while offset < buffer.count {
                        let n = write(client, buffer.baseAddress!.advanced(by: offset), buffer.count - offset)
                        if n <= 0 { break }; offset += n
                    }
                }
            }
        }
        defer { _ = done.wait(timeout: .now() + 5) }
        return try DaemonRPC.call("TestOnly", pipePath: path)
    }
}
