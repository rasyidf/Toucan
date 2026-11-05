using Avalonia.Headless.XUnit;
using Toucan.Avalonia.Services;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class WorkflowModeTests
{
    private const string En = """{"app.title": "My App"}""";
    private const string Fr = """{"app.title": "Mon Application"}""";

    [Theory]
    [InlineData(EditorMode.Editor, EditorMode.Review)]
    [InlineData(EditorMode.Review, EditorMode.Audit)]
    [InlineData(EditorMode.Audit, EditorMode.Editor)]
    public void NextStage_follows_edit_review_audit(EditorMode from, EditorMode expected) =>
        Assert.Equal(expected, MainWindowViewModel.NextStage(from));

    [AvaloniaFact]
    public async Task Pipelined_workflow_cycles_through_all_three_stages()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("wf-cycle", ("en", En), ("fr", Fr)));
        try
        {
            vm.SetWorkflow(WorkflowMode.Pipelined);
            vm.CycleEditorModeCommand.Execute(null);
            Assert.Equal(EditorMode.Review, vm.EditorMode);
            vm.CycleEditorModeCommand.Execute(null);
            Assert.Equal(EditorMode.Audit, vm.EditorMode);
            vm.CycleEditorModeCommand.Execute(null);
            Assert.Equal(EditorMode.Editor, vm.EditorMode);
        }
        finally { vm.SetWorkflow(WorkflowMode.Pipelined); vm.SwitchToEditorModeCommand.Execute(null); }
    }

    [AvaloniaFact]
    public async Task Direct_workflow_stays_in_editor_mode()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("wf-direct", ("en", En), ("fr", Fr)));
        try
        {
            vm.SetWorkflow(WorkflowMode.Pipelined);
            vm.SwitchToReviewModeCommand.Execute(null);
            Assert.Equal(EditorMode.Review, vm.EditorMode);

            vm.SetWorkflow(WorkflowMode.Direct);
            Assert.Equal(EditorMode.Editor, vm.EditorMode);
            Assert.False(PanelService.Instance.ShowsApprovalState);

            vm.SwitchToReviewModeCommand.Execute(null);
            vm.SwitchToAuditModeCommand.Execute(null);
            vm.CycleEditorModeCommand.Execute(null);
            Assert.Equal(EditorMode.Editor, vm.EditorMode);
        }
        finally { vm.SetWorkflow(WorkflowMode.Pipelined); vm.SwitchToEditorModeCommand.Execute(null); }
    }

    [AvaloniaFact]
    public async Task ToggleWorkflow_switches_between_direct_and_pipelined()
    {
        using var host = new TestHost();
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(host.CreateJsonProject("wf-toggle", ("en", En), ("fr", Fr)));
        try
        {
            vm.SetWorkflow(WorkflowMode.Pipelined);
            vm.ToggleWorkflowCommand.Execute(null);
            Assert.Equal(WorkflowMode.Direct, PanelService.Instance.Workflow);
            vm.ToggleWorkflowCommand.Execute(null);
            Assert.Equal(WorkflowMode.Pipelined, PanelService.Instance.Workflow);
            Assert.True(vm.IsPipelinedWorkflow);
        }
        finally { vm.SetWorkflow(WorkflowMode.Pipelined); }
    }
}
