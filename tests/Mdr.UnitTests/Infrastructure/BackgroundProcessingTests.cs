using System.Collections.Concurrent;
using FluentAssertions;
using Mdr.Application.Common.Persistence;
using Mdr.Application.Options;
using Mdr.Application.Processing;
using Mdr.Infrastructure.Background;
using Mdr.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Infrastructure;

public sealed class BackgroundProcessingTests
{
    private sealed class RecordingProcessor : IMdrUploadProcessor
    {
        private readonly ConcurrentBag<Guid> _processed;
        private readonly ConcurrentBag<Guid> _scopes;
        private readonly Guid _scopeId = Guid.NewGuid();

        public RecordingProcessor(ConcurrentBag<Guid> processed, ConcurrentBag<Guid> scopes)
        {
            _processed = processed;
            _scopes = scopes;
        }

        public static Guid Poison { get; } = Guid.NewGuid();

        public Task<ProcessingOutcome> ProcessAsync(Guid uploadId, CancellationToken cancellationToken)
        {
            _scopes.Add(_scopeId);
            if (uploadId == Poison)
            {
                throw new InvalidOperationException("poison job");
            }

            _processed.Add(uploadId);
            return Task.FromResult(ProcessingOutcome.Completed);
        }
    }

    private readonly ConcurrentBag<Guid> _processed = new();
    private readonly ConcurrentBag<Guid> _scopes = new();
    private readonly FakeUploadRepository _uploads = new();
    private readonly IOptions<MdrProcessingOptions> _options = Options.Create(new MdrProcessingOptions { QueueCapacity = 2, MaxParallelUploads = 2 });

    private (UploadProcessingWorker Worker, ChannelUploadQueue Queue) CreateWorker()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IUploadRepository>(_uploads);
        services.AddScoped<IMdrUploadProcessor>(_ => new RecordingProcessor(_processed, _scopes));
        var provider = services.BuildServiceProvider();
        var queue = new ChannelUploadQueue(_options);
        var worker = new UploadProcessingWorker(queue, provider.GetRequiredService<IServiceScopeFactory>(), _options, NullLogger<UploadProcessingWorker>.Instance);
        return (worker, queue);
    }

    private async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        condition().Should().BeTrue("the worker should have processed the queued uploads in time");
    }

    [Fact]
    public async Task Queue_is_fifo_and_bounded()
    {
        var queue = new ChannelUploadQueue(_options);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await queue.EnqueueAsync(first, default);
        await queue.EnqueueAsync(second, default);
        queue.Count.Should().Be(2);

        var blocked = queue.EnqueueAsync(Guid.NewGuid(), default);
        blocked.IsCompleted.Should().BeFalse("a full bounded channel applies back-pressure");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new List<Guid>();
        await foreach (var id in queue.DequeueAllAsync(cts.Token))
        {
            received.Add(id);
            if (received.Count == 3)
            {
                break;
            }
        }

        received.Take(2).Should().Equal(first, second);
        await blocked;
    }

    [Fact]
    public async Task Worker_processes_queued_uploads_each_in_its_own_scope()
    {
        var (worker, queue) = CreateWorker();
        await worker.StartAsync(default);
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        foreach (var id in ids)
        {
            await queue.EnqueueAsync(id, default);
        }

        await WaitForAsync(() => _processed.Count == 3);
        await worker.StopAsync(default);

        _processed.Should().BeEquivalentTo(ids);
        _scopes.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task A_failing_job_does_not_stop_the_worker()
    {
        var (worker, queue) = CreateWorker();
        await worker.StartAsync(default);
        var good = Guid.NewGuid();
        await queue.EnqueueAsync(RecordingProcessor.Poison, default);
        await queue.EnqueueAsync(good, default);

        await WaitForAsync(() => _processed.Contains(good));
        await worker.StopAsync(default);
    }

    [Fact]
    public async Task Startup_recovery_re_enqueues_pending_uploads()
    {
        var pending = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        _uploads.Pending.AddRange(pending);
        var (worker, _) = CreateWorker();

        // Three pending uploads with a queue capacity of two: recovery must not deadlock.
        await worker.StartAsync(default);
        await WaitForAsync(() => _processed.Count == 3);
        await worker.StopAsync(default);

        _processed.Should().BeEquivalentTo(pending);
    }

    [Fact]
    public async Task Stop_cancels_the_consumer_cleanly()
    {
        var (worker, _) = CreateWorker();
        await worker.StartAsync(default);
        var stop = worker.StopAsync(default);
        (await Task.WhenAny(stop, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(stop);
    }
}
