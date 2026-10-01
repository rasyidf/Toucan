namespace Toucan.Core.Models;

public record PretranslationJob(
    string Namespace,
    string SourceText,
    string SourceLanguage,
    string TargetLanguage);
