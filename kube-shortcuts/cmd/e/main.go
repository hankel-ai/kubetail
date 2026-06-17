package main

import (
	"fmt"
	"os"
	"time"

	"kube-shortcuts/internal/kubeutil"
)

// bashFailFast bounds how quickly a failed `bash` attempt must return for us to
// treat it as "bash isn't in this image" and fall back to `sh`. A longer-lived
// session that exits non-zero (you ran `exit 1`, hit Ctrl+C, or the last command
// failed) is a real session, not a missing shell — don't drop into sh after it.
const bashFailFast = 5 * time.Second

func execInto(target ...string) int {
	bash := append([]string{"exec", "-it"}, target...)
	bash = append(bash, "--", "bash")

	start := time.Now()
	rc := kubeutil.RunKubectl(bash...)
	if rc == 0 {
		return 0
	}
	if time.Since(start) >= bashFailFast {
		// bash ran for a while before exiting non-zero: a real session, not a
		// missing shell. Propagate its exit code instead of falling back.
		return rc
	}

	sh := append([]string{"exec", "-it"}, target...)
	sh = append(sh, "--", "sh")
	return kubeutil.RunKubectl(sh...)
}

func main() {
	if len(os.Args) > 1 {
		os.Exit(execInto(os.Args[1:]...))
	}

	pods, err := kubeutil.Pods()
	if err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
	switch len(pods) {
	case 0:
		fmt.Fprintln(os.Stderr, "No pods found in current namespace.")
		os.Exit(1)
	case 1:
		os.Exit(execInto(pods[0]))
	default:
		pod, ok := kubeutil.PickPod(pods)
		if !ok {
			fmt.Fprintln(os.Stderr, "Cancelled.")
			os.Exit(1)
		}
		os.Exit(execInto(pod))
	}
}
