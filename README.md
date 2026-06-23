# dotnet-di-rules-audit

`DI Audit` is a small .NET CLI that statically audits Dependency Injection usage in .NET and ASP.NET Core solutions and writes a Markdown report.

## Quickstart

From the repository root:

```bash
export DOTNET_CLI_HOME="$PWD/.dotnet-home"
export NUGET_PACKAGES="$PWD/.nuget-packages"
dotnet restore DotnetDiRulesAudit.slnx --configfile NuGet.Config
dotnet test DotnetDiRulesAudit.slnx --no-restore
```

Run the clean sample:

```bash
dotnet run --no-restore --project src/DotnetDiRulesAudit -- samples/GoodDiSample
```

Expected result:

```text
Errors: 0
Result: PASSED
```

Run the intentionally bad sample:

```bash
dotnet run --no-restore --project src/DotnetDiRulesAudit -- samples/BadDiSample
```

Expected result:

```text
Errors: 2
Result: FAILED
```

Open the generated report:

```bash
cat DI_AUDIT_REPORT.md
```

Use the tool on your own solution, project, or repository folder:

```bash
dotnet run --project src/DotnetDiRulesAudit -- /path/to/YourSolution.sln
dotnet run --project src/DotnetDiRulesAudit -- /path/to/YourProject.csproj
dotnet run --project src/DotnetDiRulesAudit -- /path/to/repo
```

## Usage

```bash
dotnet run --project src/DotnetDiRulesAudit -- ./samples/BadDiSample
```

The command writes `DI_AUDIT_REPORT.md` in the current directory.

Exit codes:

- `0`: analysis completed with no blocking findings
- `1`: analysis completed with blocking findings
- `2`: invalid or ambiguous input path
- `3`: solution/project could not be loaded
- `4`: unexpected internal error

## Rules

Blocking V1 rules:

- `DI007` singleton captures scoped dependency
- `DI011` circular dependency detected

Warning and info rules cover constructor size, concrete injection, singleton-to-transient captures, service locator usage, options-pattern guidance, startup validation, and registration organization.

## Codex Skill

The `skills/dependency-injection-audit` skill runs this analyzer, reads `DI_AUDIT_REPORT.md`, and uses the report as the source of truth for DI fixes.
