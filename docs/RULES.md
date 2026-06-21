# Dependency Injection Rules

## Blocking

- `DI007 Singleton captures scoped dependency`: reports singleton or hosted-service paths that reach scoped services.
- `DI011 Circular dependency detected`: reports resolved dependency cycles.

## Warnings

- `DI004 Prefer interface-based injection`: concrete application services injected into constructors.
- `DI005 Too many constructor dependencies`: constructor has seven or more dependencies.
- `DI006 Singleton captures transient dependency`: singleton path reaches transient service; warning-only in V1.
- `DI009 DI validation is not explicitly enabled`: `ValidateScopes` and `ValidateOnBuild` were not detected.
- `DI010 Service locator usage`: `GetService` or `GetRequiredService` in application code.
- `DI012 Prefer strongly typed options`: broad `IConfiguration` constructor injection.

## Info

- `DI008 Too many direct registrations in Program.cs`: suggests moving large registration blocks into extension methods.
