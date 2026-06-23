---
name: dependency-injection-audit
description: Audit ASP.NET Core and .NET projects for Dependency Injection lifetime issues, circular dependencies, service locator usage, constructor bloat, concrete injection, missing DI validation, and configuration/options-pattern problems.
---

# Dependency Injection Audit

Use this skill when the user asks to review, validate, fix, or improve Dependency Injection in an ASP.NET Core or .NET repository.

## Goal

Run the repository's DI audit tool and use the generated Markdown report as the source of truth for Dependency Injection findings.

## Command

```bash
dotnet run --project src/DIRulesAudit -- .
```

Expected output:

```text
DI_AUDIT_REPORT.md
```

## Workflow

1. Run the analyzer.
2. Open `DI_AUDIT_REPORT.md`.
3. Summarize blocking issues first.
4. Fix in this order: `DI007`, `DI011`, `DI010`, `DI012`, then design warnings.
5. Prefer minimal patches.
6. Rerun the analyzer after fixes.

## Severity handling

Treat `DI007` and `DI011` as blocking. Treat other findings as warnings or informational guidance unless the user asks for stricter handling.
