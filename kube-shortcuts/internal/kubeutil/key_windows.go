//go:build windows

package kubeutil

import (
	"os"
	"syscall"
	"unsafe"
)

var (
	kernel32              = syscall.NewLazyDLL("kernel32.dll")
	procGetConsoleMode    = kernel32.NewProc("GetConsoleMode")
	procSetConsoleMode    = kernel32.NewProc("SetConsoleMode")
	procReadConsoleInputW = kernel32.NewProc("ReadConsoleInputW")
)

const (
	enableLineInput      = 0x0002
	enableEchoInput      = 0x0004
	enableProcessedInput = 0x0001

	keyEvent = 0x0001
	vkEscape = 0x1B
	vkReturn = 0x0D
)

// inputRecord mirrors INPUT_RECORD with its KEY_EVENT_RECORD union member.
type inputRecord struct {
	eventType       uint16
	_               uint16
	keyDown         int32
	repeatCount     uint16
	virtualKeyCode  uint16
	virtualScanCode uint16
	unicodeChar     uint16
	controlKeyState uint32
}

// ReadSingleKey reads one keystroke from the console without requiring Enter.
// It reads raw key events with ReadConsoleInputW rather than ReadConsole:
// ReadConsole never hands Esc to the caller, even with line input off.
// Processed input is disabled so Ctrl+C arrives as byte 3 instead of
// terminating; Esc returns 27 and Enter returns '\r'.
func ReadSingleKey() (byte, error) {
	h := syscall.Handle(os.Stdin.Fd())

	var oldMode uint32
	r1, _, err := procGetConsoleMode.Call(uintptr(h), uintptr(unsafe.Pointer(&oldMode)))
	if r1 == 0 {
		return 0, err
	}
	newMode := oldMode &^ (enableLineInput | enableEchoInput | enableProcessedInput)
	procSetConsoleMode.Call(uintptr(h), uintptr(newMode))
	defer procSetConsoleMode.Call(uintptr(h), uintptr(oldMode))

	for {
		var rec inputRecord
		var n uint32
		r1, _, err := procReadConsoleInputW.Call(uintptr(h),
			uintptr(unsafe.Pointer(&rec)), 1, uintptr(unsafe.Pointer(&n)))
		if r1 == 0 {
			return 0, err
		}
		// Skip mouse/focus/resize events and key releases (e.g. the Enter
		// release left over from launching the command).
		if n == 0 || rec.eventType != keyEvent || rec.keyDown == 0 {
			continue
		}
		switch rec.virtualKeyCode {
		case vkEscape:
			return 27, nil
		case vkReturn:
			return '\r', nil
		}
		// Bare modifier presses (Shift, Ctrl, Alt) carry no character.
		if rec.unicodeChar == 0 || rec.unicodeChar > 0x7F {
			continue
		}
		return byte(rec.unicodeChar), nil
	}
}
