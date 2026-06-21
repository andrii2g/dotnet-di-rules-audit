# dotnet-di-rules-audit

`DI Audit` is a small .NET CLI that statically audits Dependency Injection usage in .NET and ASP.NET Core solutions and writes a Markdown report.

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
