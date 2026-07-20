#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
echo "build-libraw-android.sh is a compatibility entry; running build-native-android.sh."
exec "$ROOT/native/scripts/build-native-android.sh" "$@"
