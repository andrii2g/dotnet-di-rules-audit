#!/usr/bin/env bash
set -euo pipefail

ROOT="${1:-.}"

if [ -f "./src/DotnetDiRulesAudit/DotnetDiRulesAudit.csproj" ]; then
  dotnet run --project "./src/DotnetDiRulesAudit" -- "${ROOT}"
elif [ -f "${ROOT}/src/DotnetDiRulesAudit/DotnetDiRulesAudit.csproj" ]; then
  dotnet run --project "${ROOT}/src/DotnetDiRulesAudit" -- "${ROOT}"
else
  dotnet tool run dotnet-di-rules-audit "${ROOT}"
fi

test -f DI_AUDIT_REPORT.md
