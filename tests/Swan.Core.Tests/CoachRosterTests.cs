using Swan.Core.Common;
using Swan.Core.League;
using Xunit;

namespace Swan.Core.Tests;

public sealed class CoachRosterTests
{
    private static CoachEntry E(string email, string name) => new(email, name);

    [Fact]
    public void ValidEntriesComeBackWithTrimmedLowercaseEmailsAndTrimmedNames()
    {
        var validated = CoachRoster.Validate([E("  Pat.Smith@Example.COM ", "  Pat Smith "), E("lee@example.com", "Lee")]);

        Assert.True(validated.IsSuccess);
        Assert.Equal([("pat.smith@example.com", "Pat Smith"), ("lee@example.com", "Lee")], validated.Value!.Select(entry => (entry.Email.Value, entry.Name)));
    }

    [Fact]
    public void AnEmptyListIsValidBecauseAZeroCoachSeasonIsAllowedHere()
    {
        Assert.True(CoachRoster.Validate([]).IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("Pat <pat@example.com>")]
    [InlineData("two@example.com, three@example.com")]
    public void AMalformedEmailIsRejectedNamingTheRow(string email)
    {
        var validated = CoachRoster.Validate([E("ok@example.com", "Ok"), E(email, "Pat")]);

        Assert.Equal("season.coaches", validated.Error!.Code);
        Assert.Contains("row 2", validated.Error.Message);
        Assert.Contains("email", validated.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public void ABlankNameIsRejectedNamingTheRow(string name)
    {
        var validated = CoachRoster.Validate([E("ok@example.com", name)]);

        Assert.Equal("season.coaches", validated.Error!.Code);
        Assert.Contains("row 1", validated.Error.Message);
        Assert.Contains("name", validated.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADuplicateEmailIsRejectedEvenWhenOnlyTheCaseOrSpacesDiffer()
    {
        var validated = CoachRoster.Validate([E("pat@example.com", "Pat"), E(" PAT@example.com ", "Patricia")]);

        Assert.Equal("season.coaches", validated.Error!.Code);
        Assert.Contains("pat@example.com", validated.Error.Message);
        Assert.Contains("more than once", validated.Error.Message);
    }

    [Fact]
    public void TooLongAnEmailOrNameIsRejected()
    {
        var longName = new string('n', 201);
        var longEmail = new string('e', 320) + "@example.com";

        Assert.Equal("season.coaches", CoachRoster.Validate([E("ok@example.com", longName)]).Error!.Code);
        Assert.Equal("season.coaches", CoachRoster.Validate([E(longEmail, "Ok")]).Error!.Code);
    }

    [Fact]
    public void ANullEntryFieldIsRejectedLikeABlankOne()
    {
        Assert.Equal("season.coaches", CoachRoster.Validate([new CoachEntry(null, "Pat")]).Error!.Code);
        Assert.Equal("season.coaches", CoachRoster.Validate([new CoachEntry("pat@example.com", null)]).Error!.Code);
    }

    [Fact]
    public void AMembershipCanBeDeactivatedAndReactivated()
    {
        var membership = new Membership(new MembershipId(Guid.NewGuid()), new UserId(Guid.NewGuid()), new SeasonId(Guid.NewGuid()), MembershipRole.Coach);
        Assert.True(membership.IsActive);

        membership.Deactivate();
        Assert.False(membership.IsActive);
        membership.Reactivate();

        Assert.True(membership.IsActive);
        Assert.Equal(MembershipRole.Coach, membership.Role);
    }
}
