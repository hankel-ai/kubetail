package main

import (
	"fmt"
	"os"
	"strings"

	"kube-shortcuts/internal/kubeutil"
)

func logsArgs(maxLogRequests int, extra ...string) []string {
	a := []string{
		"logs",
		fmt.Sprintf("--max-log-requests=%d", maxLogRequests),
		"--timestamps",
		"--ignore-errors",
		"--all-containers",
		"--prefix",
		"--pod-running-timeout=60s",
		"-f",
	}
	return append(a, extra...)
}

func main() {
	if len(os.Args) > 1 {
		os.Exit(kubeutil.RunKubectl(logsArgs(50, os.Args[1:]...)...))
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
		os.Exit(kubeutil.RunKubectl(logsArgs(50, pods[0])...))
	default:
		fmt.Fprintln(os.Stderr, "Multiple pods in current namespace; specify one:")
		fmt.Fprintln(os.Stderr, "  "+strings.Join(pods, "\n  "))
		os.Exit(1)
	}
}
