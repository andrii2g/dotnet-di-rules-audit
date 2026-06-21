param(
    [string]$Root = "."
)

$ErrorActionPreference = "Stop"

if (Test-Path "./src/DotnetDiRulesAudit/DotnetDiRulesAudit.csproj") {
    dotnet run --project "./src/DotnetDiRulesAudit" -- $Root
}
elseif (Test-Path (Join-Path $Root "src/DotnetDiRulesAudit/DotnetDiRulesAudit.csproj")) {
    dotnet run --project (Join-Path $Root "src/DotnetDiRulesAudit") -- $Root
}
else {
    dotnet tool run dotnet-di-rules-audit $Root
}

if (!(Test-Path "DI_AUDIT_REPORT.md")) {
    Write-Error "DI_AUDIT_REPORT.md was not generated"
}
