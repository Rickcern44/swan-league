namespace Swan.Core.Picks;

public static class PickVisibilityPolicy
{
    public static bool CanSeeTeamPick(IEnumerable<TeamPick> viewerPicks, TeamPick targetPick, DateTimeOffset now, bool isAdmin, bool adminOverride) =>
        isAdmin || adminOverride || IsLocked(targetPick.LockTime, now) && viewerPicks.Any() && viewerPicks.All(pick => IsLocked(pick.LockTime, now));

    public static bool CanSeeDogPick(IEnumerable<DogPick> viewerDogs, DogPick targetDog, DateTimeOffset now, bool isAdmin, bool adminOverride) =>
        isAdmin || adminOverride || IsLocked(targetDog.LockTime, now) && viewerDogs.Any() && viewerDogs.All(dog => IsLocked(dog.LockTime, now));

    private static bool IsLocked(DateTimeOffset lockTime, DateTimeOffset now) => now >= lockTime;
}
