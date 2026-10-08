using System.Threading.Channels;
using Mdr.Application.Options;
using Mdr.Application.Processing;
using Microsoft.Extensions.Options;

namespace Mdr.Infrastructure.Background;

/// <summary>
/// Bounded in-process queue backed by <see cref="Channel{T}"/>. When full, producers wait (back-pressure).
/// Durability comes from the database: uploads stay Queued/Processing until finished and are re-enqueued at startup.
/// </summary>
public sealed class ChannelUploadQueue : IUploadQueue
{
    private readonly Channel<Guid> _channel;

    public ChannelUploadQueue(IOptions<MdrProcessingOptions> options)
    {
        _channel = Channel.CreateBounded<Guid>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false,
        });
    }

    public int Count => _channel.Reader.Count;

    public ValueTask EnqueueAsync(Guid uploadId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(uploadId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
