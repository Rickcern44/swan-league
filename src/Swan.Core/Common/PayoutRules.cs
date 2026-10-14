namespace Swan.Core.Common;

public sealed record PayoutPlace(int Position, decimal Percent);

public sealed record PayoutPlan(string Pool, IReadOnlyList<PayoutPlace> Places);

// How each pool's pot is shared. A plan is one or more finishing positions with positive percentages (two decimals at most) that total exactly 100.00.
public static class PayoutRules
{
    // The four canonical pools, in display order.
    public static readonly IReadOnlyList<string> Pools = ["Main", "Dog", "Regular Season", "Playoff"];

    public static Result<Money> ValidateFine(Money fine) =>
        fine.Currency != "USD" ? Failure<Money>("The missed-pick fine must be in USD.")
        : fine.Amount <= 0 || decimal.Round(fine.Amount, 2) != fine.Amount ? Failure<Money>("The missed-pick fine must be greater than zero and in whole cents.")
        : Result<Money>.Success(fine);

    public static Result<IReadOnlyList<PayoutPlan>> Validate(IReadOnlyCollection<PayoutPlan> plans)
    {
        if (plans.Count != Pools.Count || plans.Select(plan => plan.Pool).Distinct().Count() != plans.Count || plans.Any(plan => !Pools.Contains(plan.Pool)))
            return Failure<IReadOnlyList<PayoutPlan>>($"Give each pool exactly one payout plan: {string.Join(", ", Pools)}.");
        var ordered = new List<PayoutPlan>();
        foreach (var pool in Pools)
        {
            var places = plans.Single(plan => plan.Pool == pool).Places;
            if (places.Count == 0) return Failure<IReadOnlyList<PayoutPlan>>($"{pool} needs at least one paid placing.");
            if (places.Any(place => place.Position < 1)) return Failure<IReadOnlyList<PayoutPlan>>($"{pool}: placings must be 1st, 2nd, 3rd and so on.");
            if (places.Select(place => place.Position).Distinct().Count() != places.Count) return Failure<IReadOnlyList<PayoutPlan>>($"{pool}: each placing can appear only once.");
            if (places.Any(place => place.Percent <= 0 || decimal.Round(place.Percent, 2) != place.Percent)) return Failure<IReadOnlyList<PayoutPlan>>($"{pool}: every percentage must be greater than zero with at most two decimals.");
            var total = places.Sum(place => place.Percent);
            if (total != 100m) return Failure<IReadOnlyList<PayoutPlan>>($"{pool}: percentages must total exactly 100.00 (they total {total:0.00}).");
            ordered.Add(new PayoutPlan(pool, places.OrderBy(place => place.Position).ToList()));
        }

        return Result<IReadOnlyList<PayoutPlan>>.Success(ordered);
    }

    // Amounts in position order for a pot. Every place except the lowest position is its percent of the pot rounded down to the cent; the lowest position takes the remainder, so the amounts always add up to the pot exactly and none is ever negative.
    public static IReadOnlyList<decimal> Split(decimal pot, IReadOnlyCollection<PayoutPlace> places)
    {
        var ordered = places.OrderBy(place => place.Position).ToList();
        var amounts = ordered.Select(place => Math.Floor(pot * place.Percent) / 100m).ToList();
        if (amounts.Count > 0) amounts[0] = pot - amounts.Skip(1).Sum();
        return amounts;
    }

    private static Result<T> Failure<T>(string message) => Result<T>.Failure("season.payouts", message);
}
