//go:build !windows

package kubeutil

import (
	"bufio"
	"os"
	"strings"
)

// ReadSingleKey fallback for non-Windows builds: read a line and return the
// first byte. Keeps the package buildable off Windows for local dev/testing.
func ReadSingleKey() (byte, error) {
	r := bufio.NewReader(os.Stdin)
	line, err := r.ReadString('\n')
	if err != nil {
		return 0, err
	}
	line = strings.TrimRight(line, "\r\n")
	if line == "" {
		return 0, nil
	}
	return line[0], nil
}
