using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Toucan.Core.Services;

namespace Toucan.Core.Tests;

public class AtomicFileServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("toucan-atomic-").FullName;
    private readonly FileService _files = new(NullLogger<FileService>.Instance);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveText_ReplacesExistingFile_AndLeavesNoTempFiles()
    {
        _files.SaveText(_dir, "a.txt", "one");
        _files.SaveText(_dir, "a.txt", "two");

        Assert.Equal("two", _files.ReadText(_dir, "a.txt"));
        Assert.Equal(["a.txt"], Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task SaveAsync_WritesNewFile_WithBom_LikeBefore()
    {
        await _files.SaveTextAsync(_dir, "b.txt", "hi");

        Assert.Equal([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i'], File.ReadAllBytes(Path.Combine(_dir, "b.txt")));
    }

    [Fact]
    public void FailedSave_KeepsOriginalAndRemovesTempFile()
    {
        _files.SaveText(_dir, "c.txt", "original");
        var path = Path.Combine(_dir, "c.txt");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            // Replacement fails on Windows (file locked); on Unix the swap succeeds. Either way nothing is left half-written.
            try { _files.SaveText(_dir, "c.txt", "new"); } catch (IOException) { }
        }

        var text = _files.ReadText(_dir, "c.txt");
        Assert.True(text is "original" or "new");
        Assert.Equal(["c.txt"], Directory.GetFileSystemEntries(_dir).Select(Path.GetFileName));
    }
}
