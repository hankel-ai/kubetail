package kubeutil

import (
	"errors"
	"fmt"
	"os"
	"os/exec"
	"os/signal"
	"strings"
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
