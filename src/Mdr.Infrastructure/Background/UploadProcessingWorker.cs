using Mdr.Application.Common.Persistence;
using Mdr.Application.Options;
using Mdr.Application.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Infrastructure.Background;

/// <summary>
/// Consumes the upload queue with bounded parallelism. Each job gets its own DI scope (and DbContext); failures are
/// contained per job. On startup, uploads left Queued/Processing by a previous crash are re-enqueued.
/// </summary>
public sealed class UploadProcessingWorker : BackgroundService
{
    private readonly IUploadQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<MdrProcessingOptions> _options;
    private readonly ILogger<UploadProcessingWorker> _logger;

    public UploadProcessingWorker(
        IUploadQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<MdrProcessingOptions> options,
        ILogger<UploadProcessingWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Do not block host startup.
        await Task.Yield();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.Value.MaxParallelUploads,
            CancellationToken = stoppingToken,
        };

        _logger.LogInformation("Upload worker started with max parallelism {MaxParallel}", parallelOptions.MaxDegreeOfParallelism);

        // Start consuming before recovery so recovery never deadlocks on a full bounded queue.
        var consumer = Parallel.ForEachAsync(
            _queue.DequeueAllAsync(stoppingToken),
            parallelOptions,
            async (uploadId, ct) => await ProcessOneAsync(uploadId, ct));

        await RecoverPendingUploadsAsync(stoppingToken);

        try
        {
            await consumer;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Upload worker stopping");
        }
    }

    /// <summary>Re-enqueues uploads that were Queued or Processing when the previous process stopped.</summary>
    public async Task<int> RecoverPendingUploadsAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<Guid> pending;
            await using (var scope = _scopeFactory.CreateAsyncScope())
            {
                var uploads = scope.ServiceProvider.GetRequiredService<IUploadRepository>();
                pending = await uploads.GetPendingIdsAsync(cancellationToken);
            }

            foreach (var uploadId in pending)
            {
                await _queue.EnqueueAsync(uploadId, cancellationToken);
            }

            if (pending.Count > 0)
            {
                _logger.LogWarning("Recovered {Count} pending uploads after restart", pending.Count);
            }

            return pending.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery of pending uploads failed; they will be retried on next start");
            return 0;
        }
    }

    private async Task ProcessOneAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object> { ["UploadId"] = uploadId });
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var processor = scope.ServiceProvider.GetRequiredService<IMdrUploadProcessor>();
            var outcome = await processor.ProcessAsync(uploadId, cancellationToken);
            _logger.LogInformation("Upload {UploadId} processed with outcome {Outcome}", uploadId, outcome);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Upload {UploadId} interrupted by shutdown; it will be recovered on next start", uploadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Upload {UploadId} job crashed", uploadId);
        }
    }
}
