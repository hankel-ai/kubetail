package kubeutil

import (
	"bufio"
	"encoding/json"
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
	out, err := kubectlCmd("get", "pods",
		"--field-selector", "status.phase!=Succeeded,status.phase!=Failed",
		"-o", "jsonpath={.items[*].metadata.name}").Output()
	if err != nil {
		return nil, err
	}
	return strings.Fields(strings.TrimSpace(string(out))), nil
}

const pickKeys = "1234567890abcdefghijklmnopqrstuvwxyz"

// PickPod prints a single-key menu of pods and reads one keystroke.
// Returns the chosen pod and true, or "" and false if cancelled (Esc/Ctrl+C/unknown key).
func PickPod(pods []string) (string, bool) {
	return pick("Multiple pods in current namespace; pick one:", pods, -1)
}

// Containers returns the regular (non-init, non-ephemeral) container names of a
// pod and the index of its default container: the one named by the
// kubectl.kubernetes.io/default-container annotation, else the first.
func Containers(pod string) ([]string, int, error) {
	out, err := kubectlCmd("get", "pod", pod, "-o", "json").Output()
	if err != nil {
		return nil, 0, err
	}
	var p struct {
		Metadata struct {
			Annotations map[string]string `json:"annotations"`
		} `json:"metadata"`
		Spec struct {
			Containers []struct {
				Name string `json:"name"`
			} `json:"containers"`
		} `json:"spec"`
	}
	if err := json.Unmarshal(out, &p); err != nil {
		return nil, 0, err
	}
	names := make([]string, len(p.Spec.Containers))
	def := 0
	want := p.Metadata.Annotations["kubectl.kubernetes.io/default-container"]
	for i, c := range p.Spec.Containers {
		names[i] = c.Name
		if c.Name == want {
			def = i
		}
	}
	return names, def, nil
}

// PickContainer prints a single-key menu of containers with the default marked
// by an asterisk. Enter selects the default.
func PickContainer(pod string, containers []string, def int) (string, bool) {
	return pick(fmt.Sprintf("Multiple containers in %s; pick one:", pod), containers, def)
}

// pick prints a single-key menu and reads one keystroke. def is the index Enter
// selects and is marked with '*'; pass -1 for no default (Enter cancels).
func pick(header string, items []string, def int) (string, bool) {
	max := len(items)
	if max > len(pickKeys) {
		max = len(pickKeys)
	}
	if def >= max {
		def = -1
	}

	fmt.Fprintln(os.Stderr, header)
	for i := 0; i < max; i++ {
		mark := ' '
		if i == def {
			mark = '*'
		}
		fmt.Fprintf(os.Stderr, " %c[%c] %s\n", mark, pickKeys[i], items[i])
	}
	if len(items) > max {
		fmt.Fprintf(os.Stderr, "  (... %d more — specify by name)\n", len(items)-max)
	}
	if def >= 0 {
		fmt.Fprint(os.Stderr, "Choice (Enter for *, Esc to cancel): ")
	} else {
		fmt.Fprint(os.Stderr, "Choice (Esc to cancel): ")
	}

	b, err := ReadSingleKey()
	fmt.Fprintln(os.Stderr)
	if err != nil {
		return "", false
	}
	if b == 27 || b == 3 {
		return "", false
	}
	if (b == '\r' || b == '\n') && def >= 0 {
		return items[def], true
	}
	if b >= 'A' && b <= 'Z' {
		b += 32
	}
	idx := strings.IndexByte(pickKeys[:max], b)
	if idx < 0 {
		return "", false
	}
	return items[idx], true
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
	code, _ := runKubectlSorted(args...)
	return code
}

// runKubectlSorted runs kubectl, sorts buffered output by timestamp, and reports
// both the exit code and whether any stdout line was actually emitted.
func runKubectlSorted(args ...string) (int, bool) {
	fmt.Fprintln(os.Stderr, "+ kubectl "+strings.Join(args, " "))

	produced := false
	cmd := kubectlCmd(args...)
	cmd.Stdin = os.Stdin
	cmd.Stderr = os.Stderr

	stdout, err := cmd.StdoutPipe()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		return 1, produced
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
		return 1, produced
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
			trimmed := strings.TrimSpace(line)
			if trimmed != "" && !strings.HasPrefix(trimmed, "error:") {
				produced = true
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
			return ee.ExitCode(), produced
		}
		fmt.Fprintln(os.Stderr, err)
		return 1, produced
	}
	return 0, produced
}

// RunKubectlLogsWithRetry runs a `kubectl logs` command, retrying while the pod
// is not ready yet. With --ignore-errors kubectl may exit 0 even when containers
// aren't ready, so we retry whenever no log lines were actually streamed.
func RunKubectlLogsWithRetry(args ...string) int {
	const (
		retryInterval = 2 * time.Second
		maxWait       = 5 * time.Minute
	)
	deadline := time.Now().Add(maxWait)

	for {
		code, produced := runKubectlSorted(args...)
		if produced {
			return code
		}
		if time.Now().After(deadline) {
			return code
		}

		fmt.Fprintf(os.Stderr, "Pod not ready yet; retrying in %s... (Ctrl+C to stop)\n", retryInterval)

		sig := make(chan os.Signal, 1)
		signal.Notify(sig, os.Interrupt)
		select {
		case <-time.After(retryInterval):
			signal.Stop(sig)
		case <-sig:
			signal.Stop(sig)
			fmt.Fprintln(os.Stderr, "Cancelled.")
			return code
		}
	}
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
