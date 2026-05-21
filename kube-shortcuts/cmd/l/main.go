package main

import (
	"fmt"
	"os"

	"kube-shortcuts/internal/kubeutil"
)

func logsArgs(maxLogRequests int, allPods bool, extra ...string) []string {
	a := []string{
		"logs",
		fmt.Sprintf("--max-log-requests=%d", maxLogRequests),
		"--timestamps",
		"--ignore-errors",
		"--all-containers",
	}
	if allPods {
		a = append(a, "--all-pods")
	}
	a = append(a, "--prefix", "--pod-running-timeout=60s", "-f")
	return append(a, extra...)
}

func main() {
	if len(os.Args) > 1 {
		os.Exit(kubeutil.RunKubectl(logsArgs(50, true, os.Args[1:]...)...))
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
		os.Exit(kubeutil.RunKubectl(logsArgs(50, false, pods[0])...))
	default:
		os.Exit(kubeutil.RunKubectl(logsArgs(len(pods)+5, true)...))
	}
}
