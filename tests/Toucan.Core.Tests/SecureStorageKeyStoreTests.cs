using System.Runtime.Versioning;
using Toucan.Core.Services;
using Toucan.Core.Services.Secrets;
using Xunit;

namespace Toucan.Core.Tests;

/// <summary>Where the AES key for saved secrets lives on macOS and Linux, and how it moves into a system store.</summary>
public sealed class SecureStorageKeyStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("toucan-keystore-").FullName;
    private string KeyFile => Path.Combine(_root, "secret.key");

    public void Dispose() => TempFolder.TryDelete(_root);

    private sealed class FakeStore : IMasterKeyStore
    {
        public byte[]? Key { get; set; }
        public bool Available { get; set; } = true;
        public bool CorruptReadBack { get; set; }
        public int Writes { get; private set; }
        public string Name => "Fake Store";

        public MasterKeyLookup Read() => !Available ? MasterKeyLookup.Unavailable("locked")
            : Key is null ? MasterKeyLookup.NotFound
            : MasterKeyLookup.Found(CorruptReadBack ? new byte[32] : Key);

        public bool Write(byte[] key, out string? failure)
        {
            if (!Available) { failure = "locked"; return false; }
            Writes++;
            Key = [.. key];
            failure = null;
            return true;
        }
    }

    // AES-GCM is only used off Windows; on Windows these values go through DPAPI and the key store is never consulted.
    private static bool UsesAes => !OperatingSystem.IsWindows();

    [Fact]
    public void FirstUseKeepsTheKeyInTheStoreAndWritesNoKeyFile()
    {
        if (!UsesAes) return;
        var store = new FakeStore();
        var service = new SecureStorageService(KeyFile, store);

        var cipher = service.Protect("sk-1");

        Assert.False(File.Exists(KeyFile));
        Assert.Equal("Fake Store", File.ReadAllText(service.MarkerFile));
        Assert.Equal("Fake Store", service.KeyLocation);
        Assert.Equal("sk-1", new SecureStorageService(KeyFile, store).Unprotect(cipher));
    }

    [Fact]
    public void AnExistingKeyFileMovesIntoTheStoreAndOldSecretsStillDecrypt()
    {
        if (!UsesAes) return;
        var cipher = new SecureStorageService(KeyFile).Protect("sk-old");
        Assert.True(File.Exists(KeyFile));
        var store = new FakeStore();

        var migrated = new SecureStorageService(KeyFile, store);
        Assert.Equal("sk-old", migrated.Unprotect(cipher));

        Assert.False(File.Exists(KeyFile));
        Assert.True(File.Exists(migrated.MarkerFile));
        Assert.Equal("sk-old", new SecureStorageService(KeyFile, store).Unprotect(cipher));
    }

    [Fact]
    public void AnUnavailableStoreFallsBackToTheKeyFileBeforeAnyMigration()
    {
        if (!UsesAes) return;
        var store = new FakeStore { Available = false };
        var service = new SecureStorageService(KeyFile, store);

        var cipher = service.Protect("sk-2");

        Assert.True(File.Exists(KeyFile));
        Assert.False(File.Exists(service.MarkerFile));
        Assert.Equal("key file", service.KeyLocation);

        // When the store comes back, the file's key moves into it.
        store.Available = true;
        Assert.Equal("sk-2", new SecureStorageService(KeyFile, store).Unprotect(cipher));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public void AfterMigrationALockedStoreNeverCreatesANewKey()
    {
        if (!UsesAes) return;
        var store = new FakeStore();
        var cipher = new SecureStorageService(KeyFile, store).Protect("sk-3");
        store.Available = false;

        var locked = new SecureStorageService(KeyFile, store);

        Assert.Equal(string.Empty, locked.Unprotect(cipher));
        var error = Assert.Throws<SecretKeyUnavailableException>(() => locked.Protect("sk-new"));
        Assert.Contains("Fake Store", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(KeyFile));

        store.Available = true;
        Assert.Equal("sk-3", new SecureStorageService(KeyFile, store).Unprotect(cipher));
    }

    [Fact]
    public void AMarkerFromAnotherStoreRefusesInsteadOfGuessing()
    {
        if (!UsesAes) return;
        var store = new FakeStore();
        var cipher = new SecureStorageService(KeyFile, store).Protect("sk-4");

        var fileOnly = new SecureStorageService(KeyFile);

        Assert.Equal(string.Empty, fileOnly.Unprotect(cipher));
        Assert.Throws<SecretKeyUnavailableException>(() => fileOnly.Protect("x"));
        Assert.False(File.Exists(KeyFile));
    }

    [Fact]
    public void AStoreThatDoesNotReadBackTheKeyIsNotTrusted()
    {
        if (!UsesAes) return;
        var cipher = new SecureStorageService(KeyFile).Protect("sk-5");
        var store = new FakeStore { CorruptReadBack = true };

        var service = new SecureStorageService(KeyFile, store);

        Assert.Equal("sk-5", service.Unprotect(cipher));
        Assert.True(File.Exists(KeyFile));
        Assert.False(File.Exists(service.MarkerFile));
        Assert.Equal("key file", service.KeyLocation);
    }

    [Fact]
    public void TheEnvironmentCanKeepTheKeyInAFile()
    {
        var previous = Environment.GetEnvironmentVariable(MasterKeyStores.EnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(MasterKeyStores.EnvironmentVariable, "file");
            Assert.IsType<NoMasterKeyStore>(MasterKeyStores.ForCurrentPlatform(KeyFile));
        }
        finally
        {
            Environment.SetEnvironmentVariable(MasterKeyStores.EnvironmentVariable, previous);
        }
    }

    [Fact]
    public void EachKeyFileLocationHasItsOwnStoreEntry()
    {
        Assert.NotEqual(MasterKeyStores.AccountFor(KeyFile), MasterKeyStores.AccountFor(Path.Combine(_root, "other", "secret.key")));
        Assert.Equal(MasterKeyStores.AccountFor(KeyFile), MasterKeyStores.AccountFor(KeyFile));
    }

    /// <summary>Uses the real login Keychain, so it runs only when asked: <c>TOUCAN_TEST_KEYCHAIN=1</c>.</summary>
    [Fact]
    public void MacKeychainRoundTripsAKey()
    {
        if (!OperatingSystem.IsMacOS() || Environment.GetEnvironmentVariable("TOUCAN_TEST_KEYCHAIN") != "1") return;
        RunKeychainRoundTrip();
    }

    [SupportedOSPlatform("macos")]
    private void RunKeychainRoundTrip()
    {
        var store = new MacKeychainMasterKeyStore(MasterKeyStores.AccountFor(KeyFile));
        try
        {
            Assert.Equal(MasterKeyStatus.NotFound, store.Read().Status);
            var key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            Assert.True(store.Write(key, out var failure), failure);
            Assert.Equal(key, store.Read().Key.ToArray());

            var replacement = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
            Assert.True(store.Write(replacement, out failure), failure);
            Assert.Equal(replacement, store.Read().Key.ToArray());

            var cipher = new SecureStorageService(KeyFile, store).Protect("sk-keychain");
            Assert.Equal("sk-keychain", new SecureStorageService(KeyFile, store).Unprotect(cipher));
        }
        finally
        {
            store.Delete();
        }
    }
}
