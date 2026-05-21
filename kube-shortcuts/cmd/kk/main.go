package main

import (
	"os"

	"kube-shortcuts/internal/kubeutil"
)

func main() {
	args := append([]string{"get", "pod", "-w"}, os.Args[1:]...)
	os.Exit(kubeutil.RunKubectl(args...))
}
