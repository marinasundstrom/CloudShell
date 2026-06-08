#!/usr/bin/env sh
set -eu

docker run -d \
  --name sample-registry \
  -p 5023:5000 \
  --restart unless-stopped \
  registry:2