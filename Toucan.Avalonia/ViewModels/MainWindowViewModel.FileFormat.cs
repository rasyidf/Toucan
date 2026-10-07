using System.Text;
using Toucan.Avalonia.Services;
using Toucan.Core.Models;

namespace Toucan.Avalonia.ViewModels;

public partial class MainWindowViewModel
{
    public bool CanChangeFileFormat => HasProject && ProjectSettings != null && FormatIds.TryGetStyle(ProjectSettings.SaveFormat, out _);

    public bool CanChangeTextEncoding => CanChangeFileFormat && ProjectSettings!.SaveFormat != FormatIds.JavaProperties;

    public void SetTextEncoding(string encoding)
    {
        if (!CanChangeTextEncoding || encoding is not ("UTF-8" or "UTF-8 BOM")) return;
        ProjectSettings!.TextEncoding = encoding;
        MarkFileFormatChanged();
    }

    public void SetLineEnding(string lineEnding)
    {
        if (!CanChangeFileFormat || lineEnding is not ("LF" or "CRLF")) return;
        ProjectSettings!.LineEnding = lineEnding;
        MarkFileFormatChanged();
    }

    private void MarkFileFormatChanged()
    {
        _hasUntrackedChanges = true;
        IsDirty = true;
        RefreshFileFormatStatus();
        StatusText = "File format updated. Save to apply to project translation files.";
    }

    private void RefreshFileFormatStatus()
    {
        if (ProjectSettings is not { } settings || StatusBarService.Instance.ViewModel is not { } status) return;
        var encoding = settings.SaveFormat == FormatIds.JavaProperties ? "ISO-8859-1" : "UTF-8";
        var eol = Environment.NewLine == "\r\n" ? "CRLF" : "LF";
        var path = settings.Languages.SelectMany(lang => _projectService.GetLanguageFiles(settings, lang)).FirstOrDefault(File.Exists);
        if (path != null)
        {
            try
            {
                using var stream = File.OpenRead(path);
                Span<byte> prefix = stackalloc byte[3];
                var length = stream.Read(prefix);
                if (length == 3 && prefix.SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })) encoding = "UTF-8 BOM";
                stream.Position = 0;
                using var reader = new StreamReader(stream, settings.SaveFormat == FormatIds.JavaProperties ? Encoding.Latin1 : Encoding.UTF8);
                var sample = new char[8192];
                var read = reader.ReadBlock(sample, 0, sample.Length);
                var text = new string(sample, 0, read);
                if (text.Contains("\r\n", StringComparison.Ordinal)) eol = "CRLF";
                else if (text.Contains('\n')) eol = "LF";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        status.Encoding.Update(settings.TextEncoding ?? encoding);
        status.LineEndings.Update(settings.LineEnding ?? eol);
    }
}
