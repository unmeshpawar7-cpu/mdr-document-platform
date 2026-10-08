using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Mdr.Application.Common.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Mdr.Infrastructure.Storage;

public sealed class FileStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root folder for uploaded files; relative paths are resolved against the content root.</summary>
    [Required]
    public string RootPath { get; set; } = "data/uploads";
}

/// <summary>
/// Stores uploads on local disk under generated names (<c>yyyy/MM/{guid}.csv</c>). The client file name is never
/// used to build a path, and every resolved path is checked to stay inside the storage root.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private const int BufferSize = 81920;
    private static readonly Regex ExtensionPattern = new(@"^\.[a-z0-9]{1,8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _root;
    private readonly TimeProvider _timeProvider;

    public LocalFileStorage(IOptions<FileStorageOptions> options, IHostEnvironment environment, TimeProvider timeProvider)
    {
        _root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.RootPath));
        _timeProvider = timeProvider;
        Directory.CreateDirectory(_root);
    }

    public string RootPath => _root;

    public async Task<StoredFile> SaveAsync(Stream content, string extension, long maxBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var ext = (extension ?? string.Empty).ToLowerInvariant();
        if (!ExtensionPattern.IsMatch(ext))
        {
            throw new ArgumentException("Extension must look like '.csv'.", nameof(extension));
        }

        var now = _timeProvider.GetUtcNow();
        var relative = string.Create(
            CultureInfo.InvariantCulture,
            $"{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{ext}");
        var fullPath = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long total = 0;
        var completed = false;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var target = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                    {
                        throw new FileSizeLimitExceededException(maxBytes);
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            completed = true;
            var checksum = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new StoredFile(relative, total, checksum);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            if (!completed && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Stored upload not found.", path);
        }

        Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = Resolve(path);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException("Storage paths must be relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Storage path escapes the storage root.");
        }

        return fullPath;
    }
}
