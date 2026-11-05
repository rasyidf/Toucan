using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Toucan.Core.Services;
using Toucan.Plugins;
using Toucan.Plugins.Sources;

namespace Toucan.Modules.Sources;

/// <summary>The local folder needs no sign-in; the host treats this method as already satisfied.</summary>
public sealed record LocalFolderAccess() : SourceAuthMethod("none", "No sign-in needed");

/// <summary>
/// The local folder as a document source (<c>toucan.sources.local</c>). It moves bytes with the same rules as the local
/// project reader and store: versions are the SHA-256 of the file (upper-case hex), writes are staged and replaced
/// atomically after the version check, and paths cannot leave the folder or pass through links. Because the host cannot
/// tell it from a remote store, it proves that existing projects keep their fidelity across the source boundary.
/// </summary>
public sealed class LocalFolderSourceAdapter : IContentSourceAdapter
{
    public const string AdapterId = "toucan.sources.local";

    /// <summary>The connection setting that names the folder.</summary>
    public const string FolderSetting = "folder";

    private const int PageSize = 200;

    public SourceAdapterDescriptor Descriptor { get; } = new()
    {
        Id = AdapterId,
        DisplayName = "Local folder",
        Icon = "Folder",
        AllowsCustomBaseUrl = true,
        AuthMethods = [new LocalFolderAccess()],
        Capabilities = SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments | SourceCapabilities.ConditionalWrites,
        ConnectionSettings = new ConfigSchema
        {
            Fields = [new ConfigField { Key = FolderSetting, Label = "Folder", Type = ConfigFieldType.Path, Required = true }],
        },
    };

    public Task<IContentSourceSession> OpenSessionAsync(ISourceSessionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.Connection.Settings.TryGetValue(FolderSetting, out var value) || value is not string { Length: > 0 } folder)
            throw new SourceException(new SourceError(SourceErrorKind.Validation, "The local-folder connection names no folder."));
        return Task.FromResult<IContentSourceSession>(new Session(Path.GetFullPath(folder)));
    }

    private sealed class Session(string root) : IDocumentSourceSession
    {
        public Task<SourceTestResult> TestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Directory.Exists(root)
                ? SourceTestResult.Ok($"Folder {root} is reachable.")
                : SourceTestResult.Failed(new SourceError(SourceErrorKind.NotFound, $"Folder {root} does not exist.")));

        public Task<SourceProjectInfo> DescribeAsync(CancellationToken cancellationToken)
        {
            if (!Directory.Exists(root)) throw new SourceException(new SourceError(SourceErrorKind.NotFound, $"Folder {root} does not exist."));
            return Task.FromResult(new SourceProjectInfo(root, new DirectoryInfo(root).Name, null, [], [],
                SourceCapabilities.ReadDocuments | SourceCapabilities.WriteDocuments | SourceCapabilities.ConditionalWrites));
        }

        public async IAsyncEnumerable<RemoteDocumentPage> ListDocumentsAsync(DocumentListRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (!Directory.Exists(root)) throw new SourceException(new SourceError(SourceErrorKind.NotFound, $"Folder {root} does not exist."));
            var prefix = request.Prefix is null ? null : Normalize(request.Prefix);
            var all = new List<string>();
            Collect(root, string.Empty, prefix, all, cancellationToken);
            all.Sort(StringComparer.Ordinal);
            var start = 0;
            if (request.ContinuationToken is { } token)
            {
                // The token is the last path of the previous page; paths are unique and ordered, so the next page starts after it.
                start = all.FindIndex(p => string.CompareOrdinal(p, token) > 0);
                if (start < 0) start = all.Count;
            }
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = all.Skip(start).Take(PageSize).ToList();
                start += page.Count;
                var done = start >= all.Count;
                var infos = new List<RemoteDocumentInfo>(page.Count);
                foreach (var relative in page)
                {
                    var bytes = await TryReadAsync(relative, cancellationToken).ConfigureAwait(false);
                    if (bytes is not null) infos.Add(new(new(relative), Version(bytes), bytes.Length));
                }
                yield return new(infos, done ? null : page[^1], done);
                if (done) yield break;
            }
            while (true);
        }

        public async Task<RemoteDocumentContent> ReadDocumentAsync(RemoteDocumentRef document, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            var bytes = await TryReadAsync(Normalize(document.Path), cancellationToken).ConfigureAwait(false)
                ?? throw new SourceException(new SourceError(SourceErrorKind.NotFound, $"{document.Path} does not exist."));
            return new(document with { Path = Normalize(document.Path) }, bytes, Version(bytes));
        }

        public async Task<DocumentWriteResult> WriteDocumentAsync(RemoteDocumentWrite write, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(write);
            var relative = Normalize(write.Ref.Path);
            var path = SafePath(relative);
            var bytes = write.Bytes.ToArray();
            var conflicted = false;
            try
            {
                await AtomicFile.WriteAllBytesAsync(path, bytes, () =>
                {
                    SafePath(relative);
                    var actual = VersionOf(path);
                    if (actual == write.ExpectedVersion) return;
                    conflicted = true;
                    throw new OperationCanceledException("The document changed.");
                }, requireExisting: write.ExpectedVersion is not null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (conflicted)
            {
                return new(UnitOperationOutcome.Conflict, Error: new SourceError(SourceErrorKind.Conflict, $"{relative} changed."));
            }
            catch (IOException ex)
            {
                // A moved-in file between the check and the commit also lands here; report it as a conflict, not an ambiguous failure.
                if (File.Exists(path) != (write.ExpectedVersion is not null))
                    return new(UnitOperationOutcome.Conflict, Error: new SourceError(SourceErrorKind.Conflict, $"{relative} changed."));
                return new(UnitOperationOutcome.Failed, Error: new SourceError(SourceErrorKind.Internal, ex.Message));
            }
            return new(UnitOperationOutcome.Applied, Version(bytes));
        }

        public Task<DocumentWriteResult> DeleteDocumentAsync(RemoteDocumentRef document, string expectedVersion, Guid operationId, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Normalize(document.Path);
            var path = SafePath(relative);
            var actual = VersionOf(path);
            if (actual is null) return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.NotFound, Error: new SourceError(SourceErrorKind.NotFound, $"{relative} does not exist.")));
            if (actual != expectedVersion)
                return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.Conflict, actual, new SourceError(SourceErrorKind.Conflict, $"{relative} changed.")));
            File.Delete(path);
            return Task.FromResult(new DocumentWriteResult(UnitOperationOutcome.Applied));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private async Task<byte[]?> TryReadAsync(string relative, CancellationToken cancellationToken)
        {
            var path = SafePath(relative);
            try { return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private static string? VersionOf(string path)
        {
            try { return Version(File.ReadAllBytes(path)); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }

        private static string Version(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        private static string Normalize(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            return path.Replace('\\', '/').TrimStart('/');
        }

        private string SafePath(string relative)
        {
            var absolute = Path.GetFullPath(relative, root);
            var check = Path.GetRelativePath(root, absolute);
            if (check == ".." || check.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(check))
                throw new SourceException(new SourceError(SourceErrorKind.Validation, "Destination is outside the folder."));
            var current = root;
            RejectLink(current);
            foreach (var segment in check.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                RejectLink(current);
            }
            return absolute;
        }

        private static void RejectLink(string path)
        {
            if (new FileInfo(path).LinkTarget is not null || new DirectoryInfo(path).LinkTarget is not null)
                throw new SourceException(new SourceError(SourceErrorKind.Validation, $"Symbolic links are not supported: '{path}'."));
        }

        private static void Collect(string directory, string relative, string? prefix, List<string> into, CancellationToken cancellationToken)
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Links are never followed, so a link cannot pull outside files into a listing.
                if (entry.LinkTarget is not null) continue;
                var name = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                if (entry is DirectoryInfo)
                {
                    // Recovery journals and per-machine sync state are not content; the metadata sidecars under .toucan are.
                    if (name is ".toucan/recovery" or ".toucan/local") continue;
                    Collect(entry.FullName, name, prefix, into, cancellationToken);
                }
                else if (!IsStagingFile(entry.Name))
                {
                    if (prefix is null || name.StartsWith(prefix, StringComparison.Ordinal)) into.Add(name);
                }
            }
        }

        // AtomicFile stages writes as ".name.guid.tmp" next to the target.
        private static bool IsStagingFile(string name) => name.StartsWith('.') && name.EndsWith(".tmp", StringComparison.Ordinal);
    }
}
