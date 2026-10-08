namespace Mdr.Application.Common.Persistence;

/// <summary>An explicit database transaction. Disposing without committing rolls back.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

/// <summary>Persistence boundary for the application layer (implemented by the EF Core DbContext wrapper).</summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Stops tracking the given entities (keeps the change tracker small during batched imports).</summary>
    void Detach<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class;

    /// <summary>Discards all tracked changes, e.g. after a failed save before recording a failure.</summary>
    void ClearChangeTracker();

    /// <summary>True when <paramref name="exception"/> was caused by a unique index violation.</summary>
    bool IsUniqueConstraintViolation(Exception exception);
}
