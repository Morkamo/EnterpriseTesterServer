#!/usr/bin/env bash
set -euo pipefail
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
if [[ -f "$ROOT/app/EnterpriseTesterServer" ]]; then
    chmod u+x "$ROOT/app/EnterpriseTesterServer"
    exec "$ROOT/app/EnterpriseTesterServer" start --root "$ROOT" "$@"
fi
if [[ -f "$ROOT/artifacts/publish/linux-x64/app/EnterpriseTesterServer" ]]; then
    chmod u+x "$ROOT/artifacts/publish/linux-x64/app/EnterpriseTesterServer"
    exec "$ROOT/artifacts/publish/linux-x64/app/EnterpriseTesterServer" start --root "$ROOT" "$@"
fi
if command -v dotnet >/dev/null 2>&1; then
    exec dotnet run --project "$ROOT/EnterpriseTester.ServerHost/EnterpriseTester.ServerHost.csproj" -c Release -- start --root "$ROOT" "$@"
fi
echo "Нет готового Linux-пакета и .NET SDK. Распакуйте архив для нужной архитектуры." >&2
exit 1
