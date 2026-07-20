#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
echo "build-libraw-apple.sh is a compatibility entry; running build-native-apple.sh."
exec "$ROOT/native/scripts/build-native-apple.sh" "$@"
