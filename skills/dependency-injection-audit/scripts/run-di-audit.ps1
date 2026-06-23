param(
    [string]$Root = "."
)

$ErrorActionPreference = "Stop"

if (Test-Path "./src/DIRulesAudit/DIRulesAudit.csproj") {
    dotnet run --project "./src/DIRulesAudit" -- $Root
}
elseif (Test-Path (Join-Path $Root "src/DIRulesAudit/DIRulesAudit.csproj")) {
    dotnet run --project (Join-Path $Root "src/DIRulesAudit") -- $Root
}
else {
    dotnet tool run dotnet-di-rules-audit $Root
}

if (!(Test-Path "DI_AUDIT_REPORT.md")) {
    Write-Error "DI_AUDIT_REPORT.md was not generated"
}
