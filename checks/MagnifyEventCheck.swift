// Constructs events but NEVER posts them or opens a tablet device.
import AppKit
import CoreGraphics

let cases: [(Int64, NSEvent.Phase)] = [(1, .began), (2, .changed), (4, .ended), (8, .cancelled)]
for (phase, expected) in cases {
    let event = CGEvent(source: nil)!
    event.type = CGEventType(rawValue: 29)!
    event.setIntegerValueField(CGEventField(rawValue: 110)!, value: 8)
    event.setIntegerValueField(CGEventField(rawValue: 132)!, value: phase)
    let amount = phase == 2 ? 0.025 : 0.0
    event.setDoubleValueField(CGEventField(rawValue: 113)!, value: amount)
    guard let native = NSEvent(cgEvent: event) else { fatalError("Cannot decode gesture event") }
    precondition(native.type == .magnify, "Expected native magnification")
    precondition(native.phase == expected, "Gesture phase was not preserved")
    precondition(abs(native.magnification - amount) < 0.000001, "Magnification was not preserved")
}
print("Native magnify envelope: began/changed/ended/cancelled verified without posting events.")
