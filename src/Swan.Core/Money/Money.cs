using Swan.Core.Common;

namespace Swan.Core.Finance;

public enum LedgerItemStatus { Pending, Confirmed, Waived, Paid }

public sealed class LedgerItem(
    Guid id,
    Money amount,
    string memo,
    MembershipId payerId,
    MembershipId payeeId,
    MembershipId adminId)
{
    public Guid Id { get; } = id;
    public Money Amount { get; } = amount;
    public string Memo { get; } = memo;
    public MembershipId PayerId { get; } = payerId;
    public MembershipId PayeeId { get; } = payeeId;
    public MembershipId AdminId { get; } = adminId;
    public LedgerItemStatus Status { get; private set; } = LedgerItemStatus.Pending;

    public Result<LedgerItem> Confirm() => Transition(LedgerItemStatus.Pending, LedgerItemStatus.Confirmed);
    public Result<LedgerItem> Waive() => Transition(LedgerItemStatus.Pending, LedgerItemStatus.Waived);
    public Result<LedgerItem> Pay() => Transition(LedgerItemStatus.Confirmed, LedgerItemStatus.Paid);

    private Result<LedgerItem> Transition(LedgerItemStatus expected, LedgerItemStatus next)
    {
        if (Status != expected)
            return Result<LedgerItem>.Failure("ledger-item.transition.invalid", "The ledger item cannot make that transition.");

        Status = next;
        return Result<LedgerItem>.Success(this);
    }
}

public sealed class Ledger(LedgerId id, SeasonId seasonId) : AggregateRoot
{
    private readonly List<LedgerItem> items = [];

    public LedgerId Id { get; } = id;
    public SeasonId SeasonId { get; } = seasonId;
    public IReadOnlyList<LedgerItem> Items => items.AsReadOnly();

    public Result<LedgerItem> AssessFine(Money amount, string memo, MembershipId payerId, MembershipId adminId)
    {
        if (amount.Amount <= 0)
            return Result<LedgerItem>.Failure("ledger-item.amount.invalid", "Ledger item amounts must be positive.");

        var item = new LedgerItem(Guid.NewGuid(), amount, memo, payerId, default, adminId);
        items.Add(item);
        Raise(new FineAssessed(Id, item.Id, payerId, amount));
        return Result<LedgerItem>.Success(item);
    }

    public Result<LedgerItem> AddTransfer(Money amount, string memo, MembershipId payerId, MembershipId payeeId, MembershipId adminId)
    {
        if (amount.Amount <= 0)
            return Result<LedgerItem>.Failure("ledger-item.amount.invalid", "Ledger item amounts must be positive.");

        var item = new LedgerItem(Guid.NewGuid(), amount, memo, payerId, payeeId, adminId);
        items.Add(item);
        return Result<LedgerItem>.Success(item);
    }
}

public sealed record Placing(MembershipId MembershipId, int Position, Money Payout);

public sealed class PoolSettlement(PoolSettlementId id, SeasonId seasonId, Money pot) : AggregateRoot
{
    private readonly List<Placing> placements = [];

    public PoolSettlementId Id { get; } = id;
    public SeasonId SeasonId { get; } = seasonId;
    public Money Pot { get; } = pot;
    public IReadOnlyList<Placing> Placements => placements.AsReadOnly();
    public bool IsSettled { get; private set; }

    public Result<PoolSettlement> Settle(IEnumerable<Placing> proposedPlacements)
    {
        if (IsSettled)
            return Result<PoolSettlement>.Failure("pool-settlement.already-settled", "The pool has already been settled.");

        var proposed = proposedPlacements.ToList();
        if (proposed.Any(placing => placing.Position <= 0 || placing.Payout.Amount < 0 || placing.Payout.Currency != Pot.Currency) ||
            proposed.Sum(placing => placing.Payout.Amount) != Pot.Amount)
            return Result<PoolSettlement>.Failure("pool-settlement.payouts.invalid", "Payouts must equal the pool pot.");

        placements.AddRange(proposed);
        IsSettled = true;
        Raise(new PoolSettled(Id, SeasonId, Pot));
        return Result<PoolSettlement>.Success(this);
    }
}

public sealed record FineAssessed(LedgerId LedgerId, Guid LedgerItemId, MembershipId PayerId, Money Amount) : IDomainEvent;
public sealed record PoolSettled(PoolSettlementId PoolSettlementId, SeasonId SeasonId, Money Pot) : IDomainEvent;
