using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Mdr.Application.Common.Abstractions;
using Mdr.Infrastructure.Storage;
using Mdr.UnitTests.TestDoubles;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Mdr.UnitTests.Infrastructure;

public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "mdr-storage-tests", Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public StorageTests()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(Path.GetTempPath());
        _storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root }), environment, new TestClock(TestData.Now));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Save_uses_a_generated_name_and_computes_sha256()
    {
        var bytes = Encoding.UTF8.GetBytes("DocumentNumber,Title\n");
        var stored = await _storage.SaveAsync(new MemoryStream(bytes), ".csv", 1024, default);

        stored.Path.Should().StartWith("2026/10/").And.EndWith(".csv");
        stored.SizeBytes.Should().Be(bytes.Length);
        stored.Sha256.Should().Be(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

        await using var read = await _storage.OpenReadAsync(stored.Path, default);
        using var reader = new StreamReader(read);
        (await reader.ReadToEndAsync()).Should().Be("DocumentNumber,Title\n");
    }

    [Fact]
    public async Task Oversized_stream_is_rejected_and_the_partial_file_removed()
    {
        var act = () => _storage.SaveAsync(new MemoryStream(new byte[2048]), ".csv", 1024, default);
        await act.Should().ThrowAsync<FileSizeLimitExceededException>();
        Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("../outside.csv")]
    [InlineData("2026/../../outside.csv")]
    public async Task Path_traversal_is_blocked(string path)
    {
        var act = () => _storage.OpenReadAsync(path, default);
        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Delete_is_idempotent()
    {
        var stored = await _storage.SaveAsync(new MemoryStream(new byte[] { 1 }), ".csv", 10, default);
        await _storage.DeleteAsync(stored.Path, default);
        await _storage.DeleteAsync(stored.Path, default);
        var act = () => _storage.OpenReadAsync(stored.Path, default);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }
}
