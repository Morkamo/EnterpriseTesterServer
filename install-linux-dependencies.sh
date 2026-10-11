#!/usr/bin/env bash
set -euo pipefail
# Installs only system libraries needed by the bundled .NET host. Never installs/initializes a database.
if [[ "${EUID}" -ne 0 ]]; then echo "Для установки системных библиотек запустите: sudo bash $0" >&2; exit 1; fi
if command -v apt-get >/dev/null 2>&1; then
  apt-get update
  apt-get install -y ca-certificates zlib1g libstdc++6 openssl
elif command -v dnf >/dev/null 2>&1; then
  dnf install -y ca-certificates zlib libstdc++ openssl-libs
else
  echo "Установите ca-certificates, zlib, libstdc++ и OpenSSL через менеджер пакетов вашей системы." >&2
  exit 1
fi
