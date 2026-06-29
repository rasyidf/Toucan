using FsCheck;
using FsCheck.Fluent;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Tests.IO.Generators;

/// <summary>
/// FsCheck generator for TranslationItem instances with realistic,
/// constrained random data suitable for property-based testing.
/// </summary>
public static class TranslationItemGenerator
{
    /// <summary>Pool of language codes used for generation.</summary>
    private static readonly string[] LanguagePool =
        ["en", "fr", "de", "es", "ja", "zh", "ko", "pt", "it", "ru"];

    /// <summary>Pool of namespace segments for building dot-separated namespaces.</summary>
    private static readonly string[] NamespaceSegments =
        ["ui", "buttons", "labels", "messages", "errors", "forms", "nav", "settings",
         "common", "dialogs", "tooltips", "menu", "header", "footer", "submit",
         "cancel", "confirm", "title", "description", "placeholder"];

    /// <summary>
    /// Generates a random language code from the pool.
    /// </summary>
    public static Gen<string> LanguageGen =>
        Gen.Elements(LanguagePool);

    /// <summary>
    /// Generates a dot-separated namespace with 1-4 segments.
    /// Example: "ui.buttons.submit"
    /// </summary>
    public static Gen<string> NamespaceGen =>
        Gen.Choose(1, 4).SelectMany(depth =>
            Gen.ArrayOf(Gen.Elements(NamespaceSegments), depth)
                .Select(segments => string.Join(".", segments)));

    /// <summary>
    /// Generates a unicode string of 0-500 characters for translation values.
    /// </summary>
    public static Gen<string> ValueGen =>
        Gen.Choose(0, 500).SelectMany(length =>
            Gen.ArrayOf(Gen.Choose(0x20, 0xD7FF).Select(i => (char)i), length)
                .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a unicode string of 0-3000 characters for comments.
    /// This exceeds the 2000-char persistence limit to test truncation behavior.
    /// </summary>
    public static Gen<string> CommentGen =>
        Gen.Choose(0, 3000).SelectMany(length =>
            Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(i => (char)i), length)
                .Select(chars => new string(chars)));

    /// <summary>
    /// Generates a single TranslationItem with random properties.
    /// </summary>
    public static Gen<TranslationItem> TranslationItemGen =>
        from language in LanguageGen
        from ns in NamespaceGen
        from value in ValueGen
        from comment in CommentGen
        select new TranslationItem
        {
            Language = language,
            Namespace = ns,
            Value = value,
            Comment = comment,
            ChangeType = ChangeType.DirectEdit
        };

    /// <summary>
    /// Generates a list of TranslationItems with unique (Language, Namespace) keys.
    /// </summary>
    public static Gen<List<TranslationItem>> UniqueItemListGen(int maxCount = 20) =>
        Gen.Choose(1, maxCount).SelectMany(count =>
            Gen.ListOf(TranslationItemGen, count)
                .Select(DeduplicateByKey));

    /// <summary>
    /// Generates a non-empty array of TranslationItems with unique keys.
    /// </summary>
    public static Gen<TranslationItem[]> TranslationItemArrayGen =>
        UniqueItemListGen().Select(list => list.ToArray());

    /// <summary>
    /// Arbitrary instance for TranslationItem to be used with FsCheck property tests.
    /// </summary>
    public static Arbitrary<TranslationItem> TranslationItemArbitrary() =>
        Arb.From(TranslationItemGen);

    /// <summary>
    /// Generates a TranslationItem with a short value (for fast test runs).
    /// </summary>
    public static Gen<TranslationItem> SmallTranslationItemGen =>
        from language in LanguageGen
        from ns in NamespaceGen
        from value in ShortStringGen(50)
        from comment in ShortStringGen(100)
        select new TranslationItem
        {
            Language = language,
            Namespace = ns,
            Value = value,
            Comment = comment,
            ChangeType = ChangeType.DirectEdit
        };

    /// <summary>
    /// Generates a short printable ASCII string of the given max length.
    /// </summary>
    private static Gen<string> ShortStringGen(int maxLength) =>
        Gen.Choose(0, maxLength).SelectMany(len =>
            Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(i => (char)i), len)
                .Select(chars => new string(chars)));

    /// <summary>
    /// Deduplicates items by their (Language, Namespace) composite key,
    /// keeping the first occurrence.
    /// </summary>
    private static List<TranslationItem> DeduplicateByKey(List<TranslationItem> items)
    {
        var seen = new HashSet<(string, string)>();
        var result = new List<TranslationItem>();

        foreach (var item in items)
        {
            if (seen.Add((item.Language, item.Namespace)))
            {
                result.Add(item);
            }
        }

        return result;
    }
}
