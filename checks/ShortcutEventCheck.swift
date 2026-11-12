import AppKit

func verify(_ condition: @autoclosure () -> Bool, _ message: String) {
    precondition(condition(), message)
}
for key: CGKeyCode in [6, 8, 9] {
    for key: CGKeyCode in [123, 124] {
    for down in [true, false] {
        let event = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: down)!
        event.flags = [.maskControl, .maskSecondaryFn]
        let native = NSEvent(cgEvent: event)!
        verify(native.keyCode == key && native.modifierFlags.contains(.control) &&
               native.modifierFlags.contains(.function) && !native.modifierFlags.contains(.command),
               "Desktop arrow must match Control+Function classification in both directions")
    }
}
for down in [true, false] {
        let event = CGEvent(keyboardEventSource: nil, virtualKey: key, keyDown: down)!
        event.flags = .maskCommand
        let native = NSEvent(cgEvent: event)!
        verify(native.keyCode == key && native.modifierFlags.contains(.command), "Clipboard/undo shortcut envelope")
    }
}
for down in [true, false] {
    let event = CGEvent(keyboardEventSource: nil, virtualKey: 51, keyDown: down)!
    event.flags = []
    let native = NSEvent(cgEvent: event)!
    verify(native.keyCode == 51 && !native.modifierFlags.contains(.command), "Selected-text delete envelope")
}
for down in [true, false] {
    let event = CGEvent(keyboardEventSource: nil, virtualKey: 63, keyDown: down)!
    event.type = .flagsChanged
    event.flags = down ? .maskSecondaryFn : []
    let native = NSEvent(cgEvent: event)!
    verify(native.type == .flagsChanged && native.keyCode == 63, "Fn-only dictation envelope")
    verify(native.modifierFlags.contains(.function) == down, "Fn must have a matching release")
}
for down in [true, false] {
    let event = CGEvent(keyboardEventSource: nil, virtualKey: 2, keyDown: down)!
    event.flags = .maskSecondaryFn
    let native = NSEvent(cgEvent: event)!
    verify(native.keyCode == 2 && native.modifierFlags.contains(.function), "macOS Fn+D dictation envelope")
}
print("Verified desktop switching, Delete, clipboard and dictation NSEvent envelopes; no input events posted.")
