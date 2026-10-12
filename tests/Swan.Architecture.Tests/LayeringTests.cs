using NetArchTest.Rules;
using Swan.Core;
using Swan.Data;
using Xunit;

namespace Swan.Architecture.Tests;

public sealed class LayeringTests
{
    private static readonly string[] ContextNamespaces =
    [
        "Swan.Core.Identity", "Swan.Core.League", "Swan.Core.Platform", "Swan.Core.Schedule",
        "Swan.Core.Picks", "Swan.Core.Scoring", "Swan.Core.Finance", "Swan.Core.Communication"
    ];

    [Theory]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    public void CoreDoesNotDependOnFrameworks(string forbiddenDependency)
    {
        var result = Types.InAssembly(typeof(IClock).Assembly)
            .ShouldNot()
            .HaveDependencyOn(forbiddenDependency)
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void DataDoesNotDependOnServices()
    {
        var result = Types.InAssembly(typeof(SwanDbContext).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Swan.Services")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Fact]
    public void ApiDoesNotDependOnData()
    {
        var result = Types.InAssembly(typeof(global::Program).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Swan.Data")
            .GetResult();

        Assert.True(result.IsSuccessful);
    }

    [Theory]
    [MemberData(nameof(ContextPairs))]
    public void ContextsOnlyCrossBoundariesThroughCommonContracts(string sourceNamespace, string forbiddenNamespace)
    {
        var result = Types.InAssembly(typeof(IClock).Assembly)
            .That()
            .ResideInNamespace(sourceNamespace)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        Assert.True(result.IsSuccessful, $"{sourceNamespace} must not reference {forbiddenNamespace} directly.");
    }

    public static IEnumerable<object[]> ContextPairs() =>
        ContextNamespaces.SelectMany(source => ContextNamespaces
            .Where(forbidden => forbidden != source)
            .Select(forbidden => new object[] { source, forbidden }));

    // Demonstrated locally: adding a Swan.Core.Schedule.Week field to Swan.Core.Picks.Ballot
    // makes ContextsOnlyCrossBoundariesThroughCommonContracts fail. The field was removed.
}
