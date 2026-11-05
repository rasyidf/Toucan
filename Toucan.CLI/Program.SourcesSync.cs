using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Core.Contracts.Services;
using Toucan.Core.Models;
using Toucan.Extensions;
using Toucan.Core.Plugins;
using Toucan.Core.Services;
using Toucan.Core.Sources;
using Toucan.Plugins.Sources;

namespace Toucan.CLI;

internal static class SyncExit
{
    public const int Done = 0;
    public const int Error = 1;
    public const int Conflicts = 2;
    public const int SignInRequired = 3;
    public const int PartialFailure = 4;
}

/// <summary><c>toucan sources status|pull|push|sync</c>: plan, confirm and apply, with the same planner the desktop app uses.</summary>
internal static partial class Program
{
    private static readonly JsonSerializerOptions s_syncJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static int RunSync(string sub, string connectionId, string folder, string[] args)
    {
        var json = args.Contains("--json", StringComparer.Ordinal);
        var settings = LoadProjectSettings(folder);
        var connection = FindConnection(settings, connectionId);
        if (string.IsNullOrWhiteSpace(connection.Adapter))
            return Error($"Connection {connectionId} is managed by plugin {connection.Plugin ?? "(none)"}; sync is not available from the CLI.");

        var take = Option(args, "--take");
        if (take is not null and not ("remote" or "local")) return Error("--take must be 'remote' or 'local'.");

        var activation = Get<IPluginActivationService>();
        var connections = new SourceConnectionService(activation, Get<ISourceAdapterCatalog>(), Get<SourceSignInService>());
        var workspace = Get<WorkspaceService>();
        var loaded = Get<IProjectService>().LoadProject(folder);
        Get<ITranslationManagementService>().Initialize(loaded.Translations);
        workspace.Open(loaded.Settings, loaded.Source);
        using var coordinator = new SyncCoordinator(workspace, connections, Get<ISourceAdapterCatalog>());
        activation.OpenWorkspaceAsync(folder).GetAwaiter().GetResult();
        try
        {
            connections.OpenWorkspaceAsync(folder, settings.Sources).GetAwaiter().GetResult();
            coordinator.Open(folder, settings.Sources);
            return sub == "status" ? PrintStatus(coordinator, connection, json) : SyncOnce(coordinator, workspace, loaded, folder, connection, sub, take, args, json);
        }
        catch (SourceException ex) when (ex.Error.Kind == SourceErrorKind.Authentication)
        {
            Console.Error.WriteLine(ex.Error.Message + " Run 'toucan sources login " + connectionId + "'.");
            return SyncExit.SignInRequired;
        }
        finally
        {
            coordinator.Close();
            connections.CloseWorkspaceAsync().GetAwaiter().GetResult();
            activation.CloseWorkspaceAsync(folder).GetAwaiter().GetResult();
            workspace.Close();
        }
    }

    private static int PrintStatus(SyncCoordinator coordinator, SourceConnectionSettings connection, bool json)
    {
        var status = coordinator.Status(connection.Id);
        var store = new SyncStateStore(coordinator.ProjectRoot!, connection.Id);
        var journal = store.LoadJournal();
        var pending = journal.Entries.Count(e => e.State != JournalState.Confirmed);
        var conflicts = journal.Entries.Count(e => e.State == JournalState.Conflict);
        var baseline = store.LoadBaseline();
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                connection = connection.Id, name = connection.Name, role = connection.Role, firstSync = baseline is null,
                lastPull = baseline?.LastPull, lastPush = baseline?.LastPush, boundUnits = baseline?.Units.Count ?? 0,
                journal = new { pending, conflicts, failed = journal.Entries.Count(e => e.State == JournalState.Failed), unknown = journal.Entries.Count(e => e.State is JournalState.Unknown or JournalState.InFlight) },
            }, s_syncJson));
        }
        else
        {
            Console.WriteLine($"{connection.Id}  {connection.Name ?? string.Empty}".TrimEnd());
            Console.WriteLine($"  last pull:  {baseline?.LastPull?.LocalDateTime.ToString("g") ?? "never"}");
            Console.WriteLine($"  last push:  {baseline?.LastPush?.LocalDateTime.ToString("g") ?? "never"}");
            Console.WriteLine($"  bound:      {baseline?.Units.Count ?? 0} translations{(baseline is null ? " (first sync still to do)" : string.Empty)}");
            Console.WriteLine($"  journal:    {pending} operations not confirmed, {conflicts} conflicts");
            if (status is not null && status.Message.Length > 0) Console.WriteLine($"  state:      {status.Message}");
        }
        return conflicts > 0 ? SyncExit.Conflicts : SyncExit.Done;
    }

    private static int SyncOnce(SyncCoordinator coordinator, WorkspaceService workspace, ProjectLoadResult loaded, string folder, SourceConnectionSettings connection,
        string sub, string? take, string[] args, bool json)
    {
        var kind = sub switch { "pull" => SyncPlanKind.Pull, "push" => SyncPlanKind.Push, _ => SyncPlanKind.Sync };
        var plan = coordinator.PlanAsync(connection.Id, kind).GetAwaiter().GetResult();
        if (take is not null)
            plan = SyncPlanResolver.Resolve(plan, plan.Conflicts.Select(c => new SyncResolution(c.Id, take == "remote" ? SyncChoice.TakeRemote : SyncChoice.TakeLocal)));

        if (args.Contains("--plan", StringComparer.Ordinal))
        {
            PrintPlan(plan, connection, json, applied: null);
            return plan.HasConflicts ? SyncExit.Conflicts : SyncExit.Done;
        }
        if (!plan.HasChanges)
        {
            // Agreement is still recorded, so the next plan starts from it.
            if (plan.HasBookkeeping) coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
            PrintPlan(plan, connection, json, applied: null);
            return SyncExit.Done;
        }
        if (!args.Contains("--yes", StringComparer.Ordinal))
        {
            // A script must say so explicitly; nothing is applied on a guess.
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                return Error("Refusing to apply without --yes in a non-interactive terminal. Use --plan to see the plan first.");
            PrintPlan(plan, connection, false, applied: null);
            Console.Write("Apply? [y/N] ");
            if (!string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase)) { Console.WriteLine("Nothing was changed."); return SyncExit.Done; }
        }

        var result = coordinator.ApplyAsync(plan).GetAwaiter().GetResult();
        // A document connection already wrote the files; saving the workspace over them would undo the pull.
        if (!result.Plan.IsDocumentPlan && result.Pull is { Applied: > 0 } or { Created: > 0 } or { Deleted: > 0 }) SavePulled(workspace, loaded, folder);
        PrintPlan(result.Plan, connection, json, result);

        var remainingConflicts = result.Plan.Conflicts.Count + (result.Push?.Conflicts.Count ?? 0);
        if (result.Pull is { Succeeded: false } || result.Push is { Failed: > 0 } or { Unknown: > 0 } or { Pending: > 0 }) return SyncExit.PartialFailure;
        return remainingConflicts > 0 ? SyncExit.Conflicts : SyncExit.Done;
    }

    /// <summary>Pulled values mark the workspace dirty only; the files change here, through the same save the app uses.</summary>
    private static void SavePulled(WorkspaceService workspace, ProjectLoadResult loaded, string folder)
    {
        if (workspace.Source is { } source)
        {
            Get<WorkspaceDocumentSaveCoordinator>().SaveAsync(source).GetAwaiter().GetResult();
            return;
        }
        var translations = Get<ITranslationManagementService>().Translations;
        Get<IProjectService>().Save(loaded.Settings, translations.ToNsTree().ToList(), translations);
    }

    private static void PrintPlan(SyncPlan plan, SourceConnectionSettings connection, bool json, SyncRunResult? applied)
    {
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                connection = connection.Id,
                kind = plan.Kind.ToString().ToLowerInvariant(),
                firstSync = plan.IsFirstSync,
                remoteReadComplete = plan.RemoteReadComplete,
                pull = plan.Pull.Where(c => c.Kind != PullKind.Converge).Select(c => new { kind = c.Kind.ToString(), field = c.Field.ToString(), key = c.Remote.Name, language = c.Language, include = c.Include, value = c.NewValue }),
                push = plan.Push.Select(c => new { kind = c.Kind.ToString(), key = c.Remote.Name, language = c.Language, include = c.Include, value = c.Value }),
                conflicts = plan.Conflicts.Select(c => new { kind = c.Kind.ToString(), field = c.Field.ToString(), key = c.Remote.Name, language = c.Language, @base = c.Base, local = c.Local, remote = c.RemoteValue }),
                notices = plan.Notices.Select(n => n.Message),
                applied = applied is null ? null : new
                {
                    pulled = (applied.Pull?.Applied ?? 0) + (applied.Pull?.Created ?? 0),
                    skipped = applied.Pull?.Skipped.Count ?? 0,
                    pushed = applied.Push?.Confirmed ?? 0,
                    failed = applied.Push?.Failed ?? 0,
                    unknown = applied.Push?.Unknown ?? 0,
                    conflicts = applied.Push?.Conflicts.Count ?? 0,
                },
            }, s_syncJson));
            return;
        }
        Console.WriteLine($"{(applied is null ? "Plan" : "Applied")} for {connection.Name ?? connection.Id}: {plan.PullCount} to pull, {plan.PushCount} to push, {plan.Conflicts.Count} conflicts.");
        if (plan.IsFirstSync) Console.WriteLine("  First sync: there is no common version yet, so every difference is a conflict (use --take remote or --take local).");
        foreach (var c in plan.Pull.Where(c => c.Kind != PullKind.Converge).Take(50))
            Console.WriteLine($"  pull  {c.Kind,-7} {c.Remote.Name} ({c.Language}){(c.Include ? string.Empty : "  [not included]")}");
        foreach (var c in plan.Push.Take(50))
            Console.WriteLine($"  push  {c.Kind,-7} {c.Remote.Name} ({c.Language}){(c.Include ? string.Empty : "  [not included]")}");
        foreach (var c in plan.Conflicts.Take(50))
            Console.WriteLine($"  conflict  {c.Remote.Name} ({c.Language}): base '{c.Base}', local '{c.Local}', remote '{c.RemoteValue}'");
        foreach (var n in plan.Notices) Console.WriteLine($"  note  {n.Message}");
        if (applied is { Pull: { } pull }) Console.WriteLine($"  pulled {pull.Applied + pull.Created}{(pull.Skipped.Count > 0 ? $", {pull.Skipped.Count} skipped because they changed meanwhile" : string.Empty)}{(pull.Refusal is { } r ? $"; refused: {r}" : string.Empty)}");
        if (applied is { Push: { } push })
        {
            Console.WriteLine($"  pushed {push.Confirmed}, failed {push.Failed}, unclear {push.Unknown}, conflicts {push.Conflicts.Count}");
            foreach (var error in push.Errors.Take(10)) Console.WriteLine($"  error  {error}");
            if (push.Failed + push.Unknown > 0) Console.WriteLine("  Nothing is undone at the service; failed changes stay pending for the next run.");
        }
    }
}
