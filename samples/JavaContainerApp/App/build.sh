#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
classes_dir="$script_dir/target/classes"
jar_path="$script_dir/target/cloudshell-java-container-app-sample.jar"
repo_root="$(cd "$script_dir/../../.." && pwd)"

rm -rf "$classes_dir"
mkdir -p "$classes_dir" "$(dirname "$jar_path")"

javac --release 21 --add-modules jdk.httpserver \
  -d "$classes_dir" \
  "$repo_root"/sdk/java/cloudshell/src/main/java/com/cloudshell/sdk/*.java \
  "$script_dir/src/main/java/com/example/cloudshell/SampleServer.java"
jar --create \
  --file "$jar_path" \
  --main-class com.example.cloudshell.SampleServer \
  -C "$classes_dir" .

echo "$jar_path"
