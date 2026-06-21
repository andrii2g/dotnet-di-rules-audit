using DotnetDiRulesAudit.Model;
using DotnetDiRulesAudit.Rules;
using FluentAssertions;

namespace DotnetDiRulesAudit.Tests;

public sealed class RuleTests
{
    [Fact]
    public void Type_identity_uses_metadata_and_assembly_not_short_name()
    {
        var left = new TypeIdentity("A.Service", "A.Service", "AssemblyA", null);
        var right = new TypeIdentity("B.Service", "B.Service", "AssemblyB", null);

        left.SameType(right).Should().BeFalse();
    }

    [Fact]
    public void Singleton_to_scoped_path_is_blocking()
    {
        var singleton = new TypeIdentity("Worker", "Worker", "Tests", null);
        var scoped = new TypeIdentity("Db", "Db", "Tests", null);
        var graph = new DependencyGraph(
            [new DependencyNode(singleton, ServiceLifetimeKind.Singleton, null), new DependencyNode(scoped, ServiceLifetimeKind.Scoped, null)],
            [new DependencyEdge(singleton, scoped, ServiceLifetimeKind.Singleton, ServiceLifetimeKind.Scoped, null, true, false)],
            []);

        var findings = new SingletonCapturesScopedRule().Analyze(new RuleContext([], [], [], graph, []));

        findings.Should().ContainSingle(f => f.RuleId == "DI007" && f.Severity == DiFindingSeverity.Error);
    }

    [Fact]
    public void Circular_dependency_is_reported_once_for_same_cycle()
    {
        var a = new TypeIdentity("A", "A", "Tests", null);
        var b = new TypeIdentity("B", "B", "Tests", null);
        var graph = new DependencyGraph(
            [new DependencyNode(a, ServiceLifetimeKind.Scoped, null), new DependencyNode(b, ServiceLifetimeKind.Scoped, null)],
            [
                new DependencyEdge(a, b, ServiceLifetimeKind.Scoped, ServiceLifetimeKind.Scoped, null, true, false),
                new DependencyEdge(b, a, ServiceLifetimeKind.Scoped, ServiceLifetimeKind.Scoped, null, true, false)
            ],
            []);

        var findings = new CircularDependencyRule().Analyze(new RuleContext([], [], [], graph, []));

        findings.Should().ContainSingle(f => f.RuleId == "DI011");
    }
}
