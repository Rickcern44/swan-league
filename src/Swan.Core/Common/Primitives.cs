namespace Swan.Core.Common;

public sealed record Error(string Code, string Message);

public sealed class Result<T>
{
    private Result(T? value, Error? error) { Value = value; Error = error; }
    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(string code, string message) => new(default, new Error(code, message));
}

public interface IDomainEvent;

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> domainEvents = [];
    public IReadOnlyCollection<IDomainEvent> DomainEvents => domainEvents.AsReadOnly();
    protected void Raise(IDomainEvent domainEvent) => domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => domainEvents.Clear();
}

public abstract class Entity<TId>(TId id) where TId : notnull
{
    public TId Id { get; } = id;
}
