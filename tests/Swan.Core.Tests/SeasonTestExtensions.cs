using Swan.Core.Common;
using Swan.Core.League;

namespace Swan.Core.Tests;

internal static class SeasonTestExtensions
{
    // Activation needs a persisted double-dog selection (Week 2 plus two more); tests that only need a live season give it the usual one.
    public static Result<bool> ActivateWithDogs(this Season season)
    {
        var saved = season.SaveDoubleDogWeeks([2, 12, 17]);
        return saved.IsSuccess ? season.Activate() : saved;
    }
}
