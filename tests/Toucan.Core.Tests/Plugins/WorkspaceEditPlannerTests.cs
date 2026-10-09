using NSubstitute;
using Toucan.Core.Contracts;
using Toucan.Core.Models;
using Toucan.Core.Plugins;
using Toucan.Plugins;
using Xunit;

namespace Toucan.Core.Tests.Plugins;

public class WorkspaceEditPlannerTests
{
    private static readonly string[] s_languages = ["en", "fr"];

    private static List<TranslationItem> Project() =>
    [
        new() { Namespace = "app.title", Language = "en", Value = "My App" },
        new() { Namespace = "app.title", Language = "fr", Value = "Mon Appli" },
        new() { Namespace = "app.save", Language = "en", Value = "Save" },
        new() { Namespace = "app.save", Language = "fr", Value = "" },
        new() { Namespace = "hello", Language = "en", Value = "Hi {name}" },
        new() { Namespace = "hello", Language = "fr", Value = "Salut {nom}" },
    ];

    private static EditPlan Plan(WorkspaceEdit edit, List<TranslationItem>? items = null, long revision = 5, bool strict = false, IValidationPipeline? pipeline = null) =>
        WorkspaceEditPlanner.Plan(new PlanInput(items ?? Project(), s_languages, "en", revision, strict, pipeline), edit);

    [Fact]
    public void ValidStepsAreAllPlannedAndTheProjectItselfIsNotTouched()
    {
        var items = Project();
        var edit = new WorkspaceEdit().SetValue("app.save", "fr", "Enregistrer").SetComment("app.title", "en", "Shown in the title bar").SetReview("app.title", "fr", ReviewState.Approved);

        var plan = Plan(edit, items);

        Assert.True(plan.CanApply);
        Assert.Empty(plan.Issues);
        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal(3, plan.ChangedUnits);
        Assert.Equal("", items.Single(i => i.Namespace == "app.save" && i.Language == "fr").Value); // the planner worked on a copy
        Assert.False(items.Single(i => i.Namespace == "app.title" && i.Language == "fr").IsApproved);
    }

    [Fact]
    public void NoOpStepsAreLeftOutWithoutAnIssue()
    {
        var plan = Plan(new WorkspaceEdit().SetValue("app.title", "en", "My App").SetReview("app.title", "en", ReviewState.Draft).SetComment("app.title", "en", ""));

        Assert.True(plan.CanApply);
        Assert.Empty(plan.Steps);
        Assert.Equal(0, plan.ChangedUnits);
    }

    [Theory]
    [InlineData("missing", "en", EditIssueKind.UnknownKey)]
    [InlineData("app.title", "de", EditIssueKind.UnknownLanguage)]
    public void UnknownKeysAndLanguagesRefuseTheWholeEdit(string key, string language, EditIssueKind kind)
    {
        var plan = Plan(new WorkspaceEdit().SetValue("app.save", "fr", "ok").SetValue(key, language, "x"));

        Assert.False(plan.CanApply);
        Assert.Empty(plan.Steps);
        var issue = Assert.Single(plan.Issues);
        Assert.Equal(kind, issue.Kind);
        Assert.Equal(1, issue.OperationIndex);
    }

    [Fact]
    public void APartialEditAppliesTheGoodStepsAndReportsTheRest()
    {
        var plan = Plan(new WorkspaceEdit { AllowPartial = true }.SetValue("app.save", "fr", "ok").SetValue("missing", "en", "x"));

        Assert.True(plan.CanApply);
        Assert.Equal([0], plan.Steps.Select(s => s.Index));
        Assert.Equal(EditIssueKind.UnknownKey, Assert.Single(plan.Issues).Kind);
        Assert.Equal(1, plan.ChangedUnits);
    }

    [Fact]
    public void ARevisionThatMovedOnRefusesTheEditEvenWhenPartial()
    {
        var plan = Plan(new WorkspaceEdit { BasedOnRevision = 4, AllowPartial = true }.SetValue("app.save", "fr", "ok"), revision: 5);

        Assert.False(plan.CanApply);
        Assert.Equal(EditIssueKind.Stale, Assert.Single(plan.Issues).Kind);
        Assert.True(Plan(new WorkspaceEdit { BasedOnRevision = 5 }.SetValue("app.save", "fr", "ok"), revision: 5).CanApply);
    }

    [Fact]
    public void AnExpectedValueThatNoLongerHoldsIsAConflict()
    {
        var plan = Plan(new WorkspaceEdit().SetValue("app.title", "fr", "Nouveau", expectedValue: "Old text"));

        Assert.Equal(EditIssueKind.Conflict, Assert.Single(plan.Issues).Kind);
        Assert.True(Plan(new WorkspaceEdit().SetValue("app.title", "fr", "Nouveau", expectedValue: "Mon Appli")).CanApply);
    }

    [Fact]
    public void LaterStepsSeeEarlierOnes()
    {
        var edit = new WorkspaceEdit()
            .AddKey("menu.open", new Dictionary<string, string> { ["en"] = "Open", ["fr"] = "Ouvrir" })
            .SetValue("menu.open", "fr", "Ouvrir…", expectedValue: "Ouvrir")
            .RenameKey("menu.open", "menu.openFile")
            .SetComment("menu.openFile", "en", "Opens a file");

        var plan = Plan(edit);

        Assert.True(plan.CanApply, string.Join("; ", plan.Issues.Select(i => i.Message)));
        Assert.Equal(4, plan.Steps.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".bad")]
    [InlineData("bad.")]
    [InlineData("a..b")]
    [InlineData(" spaced . key")]
    public void MalformedNewKeysAreRefused(string key) =>
        Assert.Equal(EditIssueKind.InvalidKey, Assert.Single(Plan(new WorkspaceEdit().AddKey(key)).Issues).Kind);

    [Theory]
    [InlineData("app.title")]  // already a key
    [InlineData("app")]        // already a group
    [InlineData("hello.world")] // below a key: a value cannot also be a group
    public void KeysThatClashAreRefused(string key) =>
        Assert.Equal(EditIssueKind.KeyExists, Assert.Single(Plan(new WorkspaceEdit().AddKey(key)).Issues).Kind);

    [Fact]
    public void AddingAKeyWithAnUnknownLanguageIsRefused() =>
        Assert.Equal(EditIssueKind.UnknownLanguage, Assert.Single(Plan(new WorkspaceEdit().AddKey("x.y", new Dictionary<string, string> { ["de"] = "x" })).Issues).Kind);

    [Fact]
    public void RenamingMovesAKeyAndItsGroupAndRefusesCollisions()
    {
        var group = Plan(new WorkspaceEdit().RenameKey("app", "ui"));
        Assert.True(group.CanApply);
        Assert.Equal(4, group.ChangedUnits); // app.title and app.save, in two languages

        Assert.Equal(EditIssueKind.KeyExists, Assert.Single(Plan(new WorkspaceEdit().RenameKey("app.title", "app.save")).Issues).Kind);
        Assert.Equal(EditIssueKind.InvalidKey, Assert.Single(Plan(new WorkspaceEdit().RenameKey("app", "app.inner")).Issues).Kind);
        Assert.Equal(EditIssueKind.UnknownKey, Assert.Single(Plan(new WorkspaceEdit().RenameKey("nothing", "x")).Issues).Kind);
    }

    [Fact]
    public void DeletingRemovesAKeyAndItsGroup()
    {
        Assert.Equal(4, Plan(new WorkspaceEdit().DeleteKey("app")).ChangedUnits);
        Assert.Equal(EditIssueKind.UnknownKey, Assert.Single(Plan(new WorkspaceEdit().DeleteKey("nothing")).Issues).Kind);
        // Deleting twice: the second step finds nothing.
        Assert.Equal(EditIssueKind.UnknownKey, Assert.Single(Plan(new WorkspaceEdit().DeleteKey("hello").DeleteKey("hello")).Issues).Kind);
    }

    [Fact]
    public void AnEmptyTranslationCannotBeApproved() =>
        Assert.Equal(EditIssueKind.Empty, Assert.Single(Plan(new WorkspaceEdit().SetReview("app.save", "fr", ReviewState.Approved)).Issues).Kind);

    private static IValidationPipeline PipelineFlaggingMismatchedPlaceholders()
    {
        var pipeline = Substitute.For<IValidationPipeline>();
        pipeline.RunAll(Arg.Any<ValidationContext>()).Returns(call =>
        {
            var items = call.Arg<ValidationContext>().Items.ToList();
            return items.Where(i => i.Language == "fr" && i.Namespace == "hello" && !i.Value.Contains("{name}", StringComparison.Ordinal))
                .Select(i => new ValidationResult("placeholders", ValidationSeverity.Error, "Missing {name}", i.Namespace, i.Language)).ToList();
        });
        return pipeline;
    }

    [Fact]
    public void TheStrictPolicyRefusesApprovingATranslationWithErrors()
    {
        var plan = Plan(new WorkspaceEdit().SetReview("hello", "fr", ReviewState.Approved), strict: true, pipeline: PipelineFlaggingMismatchedPlaceholders());

        Assert.False(plan.CanApply);
        Assert.Equal(EditIssueKind.ApprovalRefused, Assert.Single(plan.Issues).Kind);
    }

    [Fact]
    public void TheStrictPolicyJudgesTheProjectAsTheEditLeavesIt()
    {
        // Fixing the placeholder and approving in one edit works; the policy is not applied to the stale text.
        var plan = Plan(new WorkspaceEdit().SetValue("hello", "fr", "Salut {name}").SetReview("hello", "fr", ReviewState.Approved), strict: true, pipeline: PipelineFlaggingMismatchedPlaceholders());

        Assert.True(plan.CanApply, string.Join("; ", plan.Issues.Select(i => i.Message)));
        Assert.Equal(2, plan.Steps.Count);
    }

    [Fact]
    public void ApprovalsAreNotCheckedWhenThePolicyIsOff()
    {
        var plan = Plan(new WorkspaceEdit().SetReview("hello", "fr", ReviewState.Approved), strict: false, pipeline: PipelineFlaggingMismatchedPlaceholders());

        Assert.True(plan.CanApply);
    }

    [Fact]
    public void APartialEditSkipsOnlyTheRefusedApproval()
    {
        var plan = Plan(new WorkspaceEdit { AllowPartial = true }.SetReview("hello", "fr", ReviewState.Approved).SetReview("app.title", "fr", ReviewState.Approved),
            strict: true, pipeline: PipelineFlaggingMismatchedPlaceholders());

        Assert.True(plan.CanApply);
        Assert.Equal([1], plan.Steps.Select(s => s.Index));
        Assert.Equal(1, plan.ChangedUnits);
        Assert.Equal(EditIssueKind.ApprovalRefused, Assert.Single(plan.Issues).Kind);
    }

    [Fact]
    public void FindingsAreLimitedToWhatTheEditTouched()
    {
        var results = new List<ValidationResult>
        {
            new("r", ValidationSeverity.Error, "bad", "hello", "fr"),
            new("r", ValidationSeverity.Warning, "elsewhere", "app.title", "fr"),
            new("r", ValidationSeverity.Info, "key level", "hello"),
        };

        var findings = WorkspaceEditPlanner.Findings(results, new HashSet<(string, string)> { ("hello", "fr") }, new HashSet<string> { "hello" });

        Assert.Equal(["bad", "key level"], findings.Select(f => f.Message));
    }
}
