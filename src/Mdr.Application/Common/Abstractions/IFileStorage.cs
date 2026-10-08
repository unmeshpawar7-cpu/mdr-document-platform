namespace Mdr.Application.Common.Abstractions;

/// <summary>Result of storing an uploaded file under a generated name.</summary>
/// <param name="Path">Storage-relative path; never derived from the client file name.</param>
/// <param name="SizeBytes">Number of bytes written.</param>
/// <param name="Sha256">Lower-case hex SHA-256 computed while streaming.</param>
public sealed record StoredFile(string Path, long SizeBytes, string Sha256);

/// <summary>Raised when a stream exceeds the allowed size while it is being stored.</summary>
public sealed class FileSizeLimitExceededException : Exception
{
    public FileSizeLimitExceededException(long limitBytes)
        : base($"The file exceeds the maximum allowed size of {limitBytes} bytes.")
    {
        LimitBytes = limitBytes;
    }

    public long LimitBytes { get; }
}

/// <summary>Binary storage for uploaded MDR files (local disk today, blob storage later).</summary>
public interface IFileStorage
{
    /// <summary>
    /// Streams <paramref name="content"/> to a new file with a generated name, computing the SHA-256 on the fly.
    /// Throws <see cref="FileSizeLimitExceededException"/> (and removes the partial file) when more than
    /// <paramref name="maxBytes"/> are read.
    /// </summary>
    Task<StoredFile> SaveAsync(Stream content, string extension, long maxBytes, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken);

    Task DeleteAsync(string path, CancellationToken cancellationToken);
}
