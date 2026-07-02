#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
classes_dir="$script_dir/target/test-classes"

rm -rf "$classes_dir"
mkdir -p "$classes_dir"

javac -d "$classes_dir" \
  $(find "$script_dir/src/main/java" -name '*.java' | sort) \
  $(find "$script_dir/src/test/java" -name '*.java' | sort)

java -cp "$classes_dir" com.cloudshell.launcher.CloudShellAppTest
