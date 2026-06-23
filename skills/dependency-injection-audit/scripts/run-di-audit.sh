#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-.}"

if [ -f "./src/DIRulesAudit/DIRulesAudit.csproj" ]; then
  dotnet run --project "./src/DIRulesAudit" -- "${ROOT}"
elif [ -f "${ROOT}/src/DIRulesAudit/DIRulesAudit.csproj" ]; then
  dotnet run --project "${ROOT}/src/DIRulesAudit" -- "${ROOT}"
else
  dotnet tool run dotnet-di-rules-audit "${ROOT}"
fi

test -f DI_AUDIT_REPORT.md
