package main

import (
	"fmt"
	"os"

	"kube-shortcuts/internal/kubeutil"
)

func execInto(target ...string) int {
	bash := append([]string{"exec", "-it"}, target...)
	bash = append(bash, "--", "bash")
	if rc := kubeutil.RunKubectl(bash...); rc == 0 {
		return 0
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
	if len(pods) == 1 {
		os.Exit(execInto(pods[0]))
	}
	fmt.Fprintln(os.Stderr, "Usage: e <pod> [args]  (or run in a namespace with exactly one pod)")
	os.Exit(1)
}
