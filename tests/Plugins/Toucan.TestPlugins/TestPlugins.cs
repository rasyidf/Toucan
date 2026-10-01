using Toucan.Core.Contracts;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Plugins;

namespace Toucan.TestPlugins;

/// <summary>A tiny real format: one "{lang}.tfmt" file per language, "key=value" per line.</summary>
public sealed class TestFormat : ISaveStrategy, ILoadStrategy
{
    public string FormatId => "test-fmt";
    public string DisplayName => "Test format";
    public IReadOnlyList<string> FileExtensions => [".tfmt"];
    public string DefaultFilePath(string language) => $"{language}.tfmt";
    public FormatDetection Detection { get; } = new(5, [".tfmt"], []);

    public void Save(string path, SaveContext context)
    {
        Directory.CreateDirectory(path);
        foreach (var (language, items) in context.LanguageDictionary)
            File.WriteAllLines(Path.Combine(path, DefaultFilePath(language)), items.Select(i => $"{i.Namespace}={i.Value}"));
    }

    public Task SaveAsync(string path, SaveContext context) { Save(path, context); return Task.CompletedTask; }

    public IEnumerable<TranslationItem> Load(string folder) =>
        Directory.GetFiles(folder, "*.tfmt").SelectMany(file =>
        {
            var language = Path.GetFileNameWithoutExtension(file);
            return File.ReadAllLines(file).Where(l => l.Contains('=')).Select(l =>
            {
                var i = l.IndexOf('=');
                return new TranslationItem { Language = language, Namespace = l[..i], Value = l[(i + 1)..] };
            });
        }).ToList();
}

public sealed class TestRule : IValidationRule
{
    public string Id => "test.rule";
    public string Name => "Test rule";
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;
    public IEnumerable<ValidationResult> Validate(ValidationContext context) =>
        context.Items.Where(i => i.Value == "forbidden").Select(i => new ValidationResult(Id, DefaultSeverity, "forbidden", i.Namespace, i.Language));
}

public sealed class TestProvider : ITranslationProvider
{
    public string Name => "TestMt";
    public ProviderDefinition? Definition { get; } = new() { Name = "TestMt", DisplayName = "Test MT", Description = "Fixture provider" };

    public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<PretranslationItemResult>>(jobs.Select(j => new PretranslationItemResult
        {
            Namespace = j.Namespace, Language = j.TargetLanguage, SourceText = j.SourceText, Succeeded = true,
            TranslatedValue = $"[testmt] {j.SourceText}", Provider = Name,
        }).ToList());
}

public sealed class TestProfile : IFrameworkProfile
{
    public string Id => "test-profile";
    public string DisplayName => "Test profile";
    public string DefaultFormatId => "test-fmt";
    public IEnumerable<string> FilePatterns => ["*.tfmt"];
    public IEnumerable<DiscoveredFile> DiscoverFiles(string rootFolder) => [];
    public string? ExtractLanguage(string relativePath) => Path.GetFileNameWithoutExtension(relativePath);
    public string GetFilePath(string rootFolder, string language, string? package = null) => Path.Combine(rootFolder, $"{language}.tfmt");
    public int DetectionScore(string rootFolder) => 0;
}

// ---- scenarios -------------------------------------------------------------------------------------------------

/// <summary>Registers one of everything (declare all four capabilities).</summary>
public sealed class FullPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        var format = new TestFormat();
        context.AddFormat(format, format);
        context.AddProvider(new TestProvider());
        context.AddValidationRule(new TestRule());
        context.AddFrameworkProfile(new TestProfile());
    }
}

/// <summary>Registers the same format as <see cref="FullPlugin"/> (collision between plugins).</summary>
public sealed class DuplicateFormatPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        var format = new TestFormat();
        context.AddFormat(format, format);
    }
}

/// <summary>Registers a format, then throws: nothing from this plugin may become active.</summary>
public sealed class ThrowingPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        var format = new TestFormat();
        context.AddFormat(format, format);
        throw new InvalidOperationException("boom");
    }
}

public sealed class ConstructorThrowsPlugin : IToucanPlugin
{
    public ConstructorThrowsPlugin() => throw new InvalidOperationException("ctor boom");
    public void Initialize(IPluginContext context) { }
}

/// <summary>Manifest declares only "formats", but this registers a rule.</summary>
public sealed class UndeclaredCapabilityPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context) => context.AddValidationRule(new TestRule());
}

public sealed class ShadowBuiltInFormatPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        var format = new Shadow("json");
        context.AddFormat(format, format);
    }
}

public sealed class ShadowBuiltInRulePlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context) => context.AddValidationRule(new Rule("missing-translation"));
}

public sealed class ShadowBuiltInProviderPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context) => context.AddProvider(new Provider("google"));
}

public sealed class MismatchedFormatIdsPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context) => context.AddFormat(new Shadow("fmt-a"), new Shadow("fmt-b"));
}

public sealed class BadFormatIdPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        var format = new Shadow("Bad Id!");
        context.AddFormat(format, format);
    }
}

public sealed class BuiltInClaimingProviderPlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context) =>
        context.AddProvider(new Provider("claimant", new ProviderDefinition { Name = "claimant", IsBuiltIn = true }));
}

public sealed class RegistersTwicePlugin : IToucanPlugin
{
    public void Initialize(IPluginContext context)
    {
        context.AddValidationRule(new Rule("twice.rule"));
        context.AddValidationRule(new Rule("twice.rule"));
    }
}

public sealed class NoParameterlessCtorPlugin : IToucanPlugin
{
    public NoParameterlessCtorPlugin(int unused) { _ = unused; }
    public void Initialize(IPluginContext context) { }
}

// ---- helpers ---------------------------------------------------------------------------------------------------

internal sealed class Shadow(string id) : ISaveStrategy, ILoadStrategy
{
    public string FormatId => id;
    public string DefaultFilePath(string language) => language;
    public void Save(string path, SaveContext context) { }
    public Task SaveAsync(string path, SaveContext context) => Task.CompletedTask;
    public IEnumerable<TranslationItem> Load(string folder) => [];
}

internal sealed class Rule(string id) : IValidationRule
{
    public string Id => id;
    public string Name => id;
    public ValidationSeverity DefaultSeverity => ValidationSeverity.Info;
    public IEnumerable<ValidationResult> Validate(ValidationContext context) => [];
}

internal sealed class Provider(string name, ProviderDefinition? definition = null) : ITranslationProvider
{
    public string Name => name;
    public ProviderDefinition? Definition => definition;
    public Task<IEnumerable<PretranslationItemResult>> PretranslateAsync(IEnumerable<PretranslationJob> jobs, PretranslationOptions? options = null, IProgress<PretranslationProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IEnumerable<PretranslationItemResult>>([]);
}
