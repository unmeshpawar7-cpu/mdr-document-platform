using Mdr.Application.Common.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Mdr.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/>. Connection resiliency (EnableRetryOnFailure) is deliberately
/// not enabled: the batched import uses an explicit transaction, and retries happen at job level instead
/// (crash recovery + attempt counter on the upload).
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly MdrDbContext _db;

    public UnitOfWork(MdrDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => _db.SaveChangesAsync(cancellationToken);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransaction(transaction);
    }

    public void Detach<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class
    {
        foreach (var entity in entities)
        {
            _db.Entry(entity).State = EntityState.Detached;
        }
    }

    public void ClearChangeTracker() => _db.ChangeTracker.Clear();

    public bool IsUniqueConstraintViolation(Exception exception)
    {
        if (exception is not DbUpdateException { InnerException: { } inner })
        {
            return false;
        }

        if (inner is SqlException sql)
        {
            return sql.Number is 2601 or 2627;
        }

        // SQLite (tests / local runs): SqliteException error 19 "UNIQUE constraint failed".
        return inner.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EfTransaction : IUnitOfWorkTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfTransaction(IDbContextTransaction transaction) => _transaction = transaction;

        public Task CommitAsync(CancellationToken cancellationToken) => _transaction.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
