using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Toucan.Core.Models;

namespace Toucan.Core.Services;

/// <summary>Detached configuration snapshots shared by dialogs, history and recovery.</summary>
public static class ProjectConfiguration
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static ProjectSettings Copy(ProjectSettings settings)
    {
        var copy = JsonSerializer.Deserialize<ProjectSettings>(JsonSerializer.Serialize(settings, Json), Json)!;
        copy.ExpectedResourceFiles = new(settings.ExpectedResourceFiles);
        copy.ProjectPath = settings.ProjectPath;
        copy.DefaultPathResolver = settings.DefaultPathResolver;
        return copy;
    }

    public static void Apply(ProjectSettings destination, ProjectSettings source)
    {
        var detached = Copy(source);
        foreach (var property in typeof(ProjectSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite && p.GetCustomAttribute<JsonIgnoreAttribute>() == null && p.Name != nameof(ProjectSettings.TranslationStates)))
            property.SetValue(destination, property.GetValue(detached));
        destination.ExpectedResourceFiles = new(detached.ExpectedResourceFiles);
        destination.IsDirty = true;
    }

    public static string Signature(ProjectSettings settings)
    {
        var copy = Copy(settings);
        copy.TranslationStates = [];
        // Detected document state changes on write; only configuration belongs in history/recovery comparisons.
        foreach (var document in copy.ResourceDocuments) { document.Data = []; document.DetectedOptions = new(); }
        return JsonSerializer.Serialize(copy, Json);
    }
}
