namespace Mdr.Application.Processing;

/// <summary>In-process work queue of upload ids awaiting background processing.</summary>
public interface IUploadQueue
{
    /// <summary>Enqueues an upload; waits (back-pressure) when the bounded queue is full.</summary>
    ValueTask EnqueueAsync(Guid uploadId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);

    int Count { get; }
}
