package kubeutil

import (
	"bufio"
	"errors"
	"fmt"
	"os"
	"os/exec"
	"os/signal"
	"sort"
	"strings"
	"time"
)

func kubectlCmd(args ...string) *exec.Cmd {
	cmd := exec.Command("kubectl", args...)
	if errors.Is(cmd.Err, exec.ErrDot) {
		cmd.Err = nil
	}
	return cmd
}

func Pods() ([]string, error) {
	out, err := kubectlCmd("get", "pods", "-o", "jsonpath={.items[*].metadata.name}").Output()
	if err != nil {
		return nil, err
	}
	return strings.Fields(strings.TrimSpace(string(out))), nil
}

const pickKeys = "1234567890abcdefghijklmnopqrstuvwxyz"

// PickPod prints a single-key menu of pods and reads one keystroke.
// Returns the chosen pod and true, or "" and false if cancelled (Esc/Ctrl+C/unknown key).
func PickPod(pods []string) (string, bool) {
	max := len(pods)
	if max > len(pickKeys) {
		max = len(pickKeys)
	}

	fmt.Fprintln(os.Stderr, "Multiple pods in current namespace; pick one:")
	for i := 0; i < max; i++ {
		fmt.Fprintf(os.Stderr, "  [%c] %s\n", pickKeys[i], pods[i])
	}
	if len(pods) > max {
		fmt.Fprintf(os.Stderr, "  (... %d more — specify by name)\n", len(pods)-max)
	}
	fmt.Fprint(os.Stderr, "Choice (Esc to cancel): ")

	b, err := ReadSingleKey()
	fmt.Fprintln(os.Stderr)
	if err != nil {
		return "", false
	}
	if b == 27 || b == 3 {
		return "", false
	}
	if b >= 'A' && b <= 'Z' {
		b += 32
	}
	idx := strings.IndexByte(pickKeys[:max], b)
	if idx < 0 {
		return "", false
	}
	return pods[idx], true
}

func extractTimestamp(line string) string {
	idx := strings.Index(line, "] ")
	if idx >= 0 {
		rest := line[idx+2:]
		if sp := strings.IndexByte(rest, ' '); sp > 0 {
			return rest[:sp]
		}
		return rest
	}
	if sp := strings.IndexByte(line, ' '); sp > 0 {
		return line[:sp]
	}
	return ""
}

func RunKubectlSorted(args ...string) int {
	fmt.Fprintln(os.Stderr, "+ kubectl "+strings.Join(args, " "))

	cmd := kubectlCmd(args...)
	cmd.Stdin = os.Stdin
	cmd.Stderr = os.Stderr

	stdout, err := cmd.StdoutPipe()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, os.Interrupt)
	defer signal.Stop(sigCh)
	go func() {
		for range sigCh {
		}
	}()

	if err := cmd.Start(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}

	lineCh := make(chan string)
	go func() {
		scanner := bufio.NewScanner(stdout)
		scanner.Buffer(make([]byte, 0, 64*1024), 1024*1024)
		for scanner.Scan() {
			lineCh <- scanner.Text()
		}
		close(lineCh)
	}()

	var buf []string
	w := bufio.NewWriter(os.Stdout)
	flush := func() {
		if len(buf) == 0 {
			return
		}
		sort.SliceStable(buf, func(i, j int) bool {
			return extractTimestamp(buf[i]) < extractTimestamp(buf[j])
		})
		for _, line := range buf {
			w.WriteString(line)
			w.WriteByte('\n')
		}
		w.Flush()
		buf = buf[:0]
	}

	const flushDelay = 250 * time.Millisecond
	timer := time.NewTimer(flushDelay)
	timer.Stop()

	for {
		select {
		case line, ok := <-lineCh:
			if !ok {
				flush()
				goto done
			}
			buf = append(buf, line)
			if !timer.Stop() {
				select {
				case <-timer.C:
				default:
				}
			}
			timer.Reset(flushDelay)
		case <-timer.C:
			flush()
		}
	}
done:
	if err := cmd.Wait(); err != nil {
		if ee, ok := err.(*exec.ExitError); ok {
			return ee.ExitCode()
		}
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	return 0
}

func RunKubectl(args ...string) int {
	fmt.Fprintln(os.Stderr, "+ kubectl "+strings.Join(args, " "))

	cmd := kubectlCmd(args...)
	cmd.Stdin = os.Stdin
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, os.Interrupt)
	defer signal.Stop(sigCh)
	go func() {
		for range sigCh {
		}
	}()

	if err := cmd.Start(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	if err := cmd.Wait(); err != nil {
		if ee, ok := err.(*exec.ExitError); ok {
			return ee.ExitCode()
		}
		fmt.Fprintln(os.Stderr, err)
		return 1
	}
	return 0
}
