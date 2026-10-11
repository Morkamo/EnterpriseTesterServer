#!/usr/bin/env bash
set -euo pipefail
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
if [[ -f "$ROOT/app/EnterpriseTesterServer" ]]; then chmod u+x "$ROOT/app/EnterpriseTesterServer";exec "$ROOT/app/EnterpriseTesterServer" --root "$ROOT" "$@"; fi
if [[ -f "$ROOT/artifacts/publish/linux-x64/app/EnterpriseTesterServer" ]]; then chmod u+x "$ROOT/artifacts/publish/linux-x64/app/EnterpriseTesterServer";exec "$ROOT/artifacts/publish/linux-x64/app/EnterpriseTesterServer" --root "$ROOT" "$@"; fi
exec dotnet run --project "$ROOT/EnterpriseTester.ServerHost/EnterpriseTester.ServerHost.csproj" -c Release -- --root "$ROOT" "$@"
