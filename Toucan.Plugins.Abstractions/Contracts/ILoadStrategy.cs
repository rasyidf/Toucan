using Toucan.Core.Models;

namespace Toucan.Core.Contracts.Services;

public interface ILoadStrategy
{
    /// <summary>Stable format identifier (see <see cref="FormatIds"/>).</summary>
    string FormatId { get; }
    IEnumerable<TranslationItem> Load(string folder);
    /// <summary>Optional project input encoding. Existing plugins keep their own reader behavior.</summary>
    IEnumerable<TranslationItem> Load(string folder, string? textEncoding) => Load(folder);
}
