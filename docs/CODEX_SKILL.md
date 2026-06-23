# Codex Dependency Injection Audit Skill

The skill wraps the analyzer workflow:

1. Find the .NET solution, project, or repository folder.
2. Run `dotnet run --project src/DIRulesAudit -- <path>`.
3. Read `DI_AUDIT_REPORT.md`.
4. Treat the report as the source of truth.
5. Fix in this order: `DI007`, `DI011`, `DI010`, `DI012`, then design warnings.

The skill should avoid speculative architecture rewrites and prefer small patches.
