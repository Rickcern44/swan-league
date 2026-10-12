using NetArchTest.Rules;
using Swan.Core;
using Swan.Data;
using Xunit;

namespace Swan.Architecture.Tests;

public sealed class LayeringTests
{
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

    // To demonstrate enforcement, temporarily reference Swan.Data from an API endpoint;
    // ApiDoesNotDependOnData fails, then remove that temporary reference before committing.
}
