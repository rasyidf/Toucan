namespace Toucan.Core.Models;

/// <summary>One document-local edit. Rename/move uses both the old path and the replacement translation.</summary>
public sealed record DocumentTranslationEdit(TranslationPath? OriginalPath, DocumentTranslation? Translation);
