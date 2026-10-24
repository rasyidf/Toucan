using Toucan.Core.Contracts;

namespace Toucan.Core.Services;

/// <summary>Legacy generic fallback. It intentionally cannot certify unused keys.</summary>
internal sealed class GenericSourceScanner : ISourceScanner
{
    public string Id => "generic";
    public SourceFileResult Extract(SourceScanFile file)
    {
        var (tokens, diagnostics) = SourceSyntax.Tokenize(file, Path.GetExtension(file.File) is ".py" or ".rb");
        var references = new List<SourceReference>();
        for (var i = 0; i + 2 < tokens.Count; i++)
        {
            if ((i & 255) == 0) file.CancellationToken.ThrowIfCancellationRequested();
            if (tokens[i].Literal) continue;
            if ((tokens[i].Text is "t" or "$t" or "GetString" or "NSLocalizedString" or "tr" or "_" or "__") && tokens[i + 1].Text == "(" || tokens[i].Text == "Localizer" && tokens[i + 1].Text == "[")
            {
                var token = tokens[i + 2];
                if (token.Literal && !token.Dynamic) references.Add(SourceSyntax.Reference(file, token, token.Text, file.Options.DefaultPackage));
                else diagnostics.Add(new("source.unresolved", "Generic scanner cannot resolve this expression.", file.File));
            }
        }
        return new(references, diagnostics);
    }
}
