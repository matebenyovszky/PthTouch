import Foundation
import Darwin

enum SettingsError: LocalizedError {
    case message(String)
    var errorDescription: String? { if case .message(let text) = self { return text }; return nil }
}

// OpenTabletDriver's existing local, same-user JSON-RPC pipe. No input events
// are read or posted, and no network listener or extra permission is needed.
enum DaemonRPC {
    static func call(_ method: String, params: [Any] = [], pipePath: String? = nil) throws -> Any {
        let fd = socket(AF_UNIX, SOCK_STREAM, 0)
        guard fd >= 0 else { throw SettingsError.message("Could not open the driver connection.") }
        defer { close(fd) }
        var timeout = timeval(tv_sec: 5, tv_usec: 0)
        var noSignal: Int32 = 1
        setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
        setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &timeout, socklen_t(MemoryLayout<timeval>.size))
        setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &noSignal, socklen_t(MemoryLayout<Int32>.size))
        var address = sockaddr_un()
        address.sun_family = sa_family_t(AF_UNIX)
        address.sun_len = UInt8(MemoryLayout<sockaddr_un>.size)
        let path = pipePath ?? (NSTemporaryDirectory() as NSString).appendingPathComponent("CoreFxPipe_OpenTabletDriver.Daemon")
        let bytes = Array(path.utf8) + [UInt8(0)]
        guard bytes.count <= MemoryLayout.size(ofValue: address.sun_path) else {
            throw SettingsError.message("The driver's local pipe path is too long.")
        }
        withUnsafeMutablePointer(to: &address.sun_path) { pointer in
            bytes.withUnsafeBytes { source in
                _ = memcpy(UnsafeMutableRawPointer(pointer), source.baseAddress!, bytes.count)
            }
        }
        let connected = withUnsafePointer(to: &address) { pointer in
            pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                connect(fd, $0, socklen_t(MemoryLayout<sockaddr_un>.size))
            }
        }
        guard connected == 0 else { throw SettingsError.message("OpenTabletDriver is not running. Open it, then reload settings.") }
        let body = try JSONSerialization.data(withJSONObject: ["jsonrpc": "2.0", "id": 1, "method": method, "params": params])
        var request = Data("Content-Length: \(body.count)\r\n\r\n".utf8)
        request.append(body)
        try request.withUnsafeBytes { buffer in
            var offset = 0
            while offset < buffer.count {
                let sent = write(fd, buffer.baseAddress!.advanced(by: offset), buffer.count - offset)
                if sent < 0 && errno == EINTR { continue }
                guard sent > 0 else { throw SettingsError.message("Could not send settings to OpenTabletDriver.") }
                offset += sent
            }
        }
        let deadline = Date().addingTimeInterval(8)
        for _ in 0..<4096 {
            guard Date() < deadline else { break }
            var header = Data()
            while !header.suffix(4).elementsEqual([13, 10, 13, 10]) {
                var byte: UInt8 = 0
                let count = read(fd, &byte, 1)
                if count < 0 && errno == EINTR { continue }
                guard count == 1, header.count < 8192 else { throw SettingsError.message("The driver did not respond in time.") }
                header.append(byte)
            }
            let headerText = String(decoding: header, as: UTF8.self)
            let length = headerText.components(separatedBy: "\r\n").compactMap { line -> Int? in
                let parts = line.split(separator: ":", maxSplits: 1)
                return parts.count == 2 && parts[0].lowercased() == "content-length"
                    ? Int(parts[1].trimmingCharacters(in: .whitespaces)) : nil
            }.first
            guard let length = length, length > 0, length <= 8_000_000 else {
                throw SettingsError.message("The driver sent an invalid settings response.")
            }
            var response = Data(count: length)
            try response.withUnsafeMutableBytes { buffer in
                var offset = 0
                while offset < length {
                    let received = read(fd, buffer.baseAddress!.advanced(by: offset), length - offset)
                    if received < 0 && errno == EINTR { continue }
                    guard received > 0 else { throw SettingsError.message("The driver response was incomplete.") }
                    offset += received
                }
            }
            guard let message = try JSONSerialization.jsonObject(with: response) as? [String: Any] else {
                throw SettingsError.message("Could not read the driver's response.")
            }
            // The daemon broadcasts log notifications on new connections.
            // Only the response carrying our request ID completes this call.
            guard (message["id"] as? NSNumber)?.intValue == 1 else { continue }
            if let error = message["error"] as? [String: Any] {
                throw SettingsError.message(error["message"] as? String ?? "The driver rejected these settings.")
            }
            return message["result"] ?? NSNull()
        }
        throw SettingsError.message("The driver did not finish the settings request in time.")
    }
}
