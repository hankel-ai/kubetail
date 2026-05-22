//go:build windows

package kubeutil

import (
	"os"
	"syscall"
	"unsafe"
)

var (
	kernel32           = syscall.NewLazyDLL("kernel32.dll")
	procGetConsoleMode = kernel32.NewProc("GetConsoleMode")
	procSetConsoleMode = kernel32.NewProc("SetConsoleMode")
)

const (
	enableLineInput      = 0x0002
	enableEchoInput      = 0x0004
	enableProcessedInput = 0x0001
)

// ReadSingleKey reads one keystroke from stdin without requiring Enter.
// On Windows it temporarily flips the console out of line/echo/processed mode
// so Esc and Ctrl+C come through as bytes (27 and 3) rather than terminating.
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

	buf := make([]byte, 1)
	n, err := os.Stdin.Read(buf)
	if err != nil {
		return 0, err
	}
	if n == 0 {
		return 0, nil
	}
	return buf[0], nil
}
