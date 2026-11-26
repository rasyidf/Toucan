using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>How a save treats the project's files (V1-11).</summary>
public enum FormatSaveMode
{
    /// <summary>Only the edited entries change in the file; comments, order and formatting stay.</summary>
    EditsInPlace,

    /// <summary>The file is written again from the project's entries; what the format keeps is listed with it.</summary>
    RewritesFile,

    /// <summary>Toucan can write the format but not read it back.</summary>
    ExportOnly,

    /// <summary>Generated files Toucan reads but does not write.</summary>
    ReadOnly,
}

public static class FormatSaving
{
    /// <summary>Formats whose exact-resource writer changes only edited entries, as document saving does.</summary>
    private static readonly IReadOnlySet<string> s_exactInPlace = new HashSet<string>(FormatIds.Comparer) { FormatIds.Json, FormatIds.JavaProperties };

    /// <summary>The mode of a format regardless of project: what it can do at best, and when.</summary>
    public static FormatSaveMode Best(ISaveStrategy writer, ILoadStrategy? reader)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (reader is null) return FormatSaveMode.ExportOnly;
        if (writer is Contracts.IResourceCapabilities capabilities && !capabilities.Permissions(new() { FormatId = writer.FormatId }).EditValues) return FormatSaveMode.ReadOnly;
        return writer.SaveScope == FormatSaveScope.DeclaredDocuments || s_exactInPlace.Contains(writer.FormatId) ? FormatSaveMode.EditsInPlace : FormatSaveMode.RewritesFile;
    }

    /// <summary>The mode for one open project, which depends on how it was opened.</summary>
    public static FormatSaveMode ForProject(ISaveStrategy writer, ILoadStrategy? reader, bool documentProject, bool exactResources)
    {
        var best = Best(writer, reader);
        if (best != FormatSaveMode.EditsInPlace) return best;
        return documentProject || exactResources && s_exactInPlace.Contains(writer.FormatId) ? FormatSaveMode.EditsInPlace : FormatSaveMode.RewritesFile;
    }

    public static string Label(FormatSaveMode mode) => mode switch
    {
        FormatSaveMode.EditsInPlace => "Edits in place",
        FormatSaveMode.RewritesFile => "Rewrites the file",
        FormatSaveMode.ExportOnly => "Export only",
        _ => "Read only",
    };

    public static string Explain(FormatSaveMode mode) => mode switch
    {
        FormatSaveMode.EditsInPlace => "Saving changes only the entries you edited. Comments, order and formatting in the file stay as they are.",
        FormatSaveMode.RewritesFile => "Saving writes the whole file again from the project. What the format keeps is listed below; file comments and formatting may change.",
        FormatSaveMode.ExportOnly => "Toucan can write this format but cannot open it again.",
        _ => "These files are generated from source code. Toucan reads them but does not write them.",
    };
}
