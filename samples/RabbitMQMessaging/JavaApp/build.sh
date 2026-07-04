#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
classes_dir="$script_dir/target/classes"
lib_dir="$script_dir/target/lib"
jar_path="$script_dir/target/cloudshell-rabbitmq-java-sample.jar"

rabbitmq_client_version="${RABBITMQ_JAVA_CLIENT_VERSION:-5.22.0}"
slf4j_version="${SLF4J_VERSION:-2.0.13}"
rabbitmq_client_jar="$lib_dir/amqp-client-$rabbitmq_client_version.jar"
slf4j_api_jar="$lib_dir/slf4j-api-$slf4j_version.jar"
slf4j_simple_jar="$lib_dir/slf4j-simple-$slf4j_version.jar"

download() {
  local url="$1"
  local path="$2"
  if [[ ! -f "$path" ]]; then
    curl -fsSL "$url" -o "$path"
  fi
}

rm -rf "$classes_dir"
mkdir -p "$classes_dir" "$lib_dir" "$(dirname "$jar_path")"

download "https://repo1.maven.org/maven2/com/rabbitmq/amqp-client/$rabbitmq_client_version/amqp-client-$rabbitmq_client_version.jar" "$rabbitmq_client_jar"
download "https://repo1.maven.org/maven2/org/slf4j/slf4j-api/$slf4j_version/slf4j-api-$slf4j_version.jar" "$slf4j_api_jar"
download "https://repo1.maven.org/maven2/org/slf4j/slf4j-simple/$slf4j_version/slf4j-simple-$slf4j_version.jar" "$slf4j_simple_jar"

javac -cp "$rabbitmq_client_jar:$slf4j_api_jar" \
  -d "$classes_dir" \
  "$script_dir/src/main/java/com/example/cloudshell/rabbitmq/RabbitMqSampleServer.java"
jar --create \
  --file "$jar_path" \
  --main-class com.example.cloudshell.rabbitmq.RabbitMqSampleServer \
  -C "$classes_dir" .

echo "$jar_path"
