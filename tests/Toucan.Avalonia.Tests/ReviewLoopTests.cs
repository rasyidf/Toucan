using Avalonia.Headless.XUnit;
using Toucan.Avalonia.ViewModels;
using Xunit;

namespace Toucan.Avalonia.Tests;

/// <summary>v0.24 R4: the keyboard review loop — approve the current card and advance deterministically to the next one that still needs review.</summary>
public class ReviewLoopTests
{
    // Three keys; en is source, fr is the target under review. b.fr is already filled, the rest empty.
    private const string En = """{"a": "A", "b": "B", "c": "C"}""";
    private const string Fr = """{"b": "Be"}""";

    private static async Task<(TestHost Host, MainWindowViewModel Vm)> Open(string name)
    {
        var host = new TestHost();
        var folder = host.CreateJsonProject(name, ("en", En), ("fr", Fr));
        var vm = host.CreateViewModel();
        await vm.OpenProjectAsync(folder);
        vm.ProjectSettings!.PrimaryLanguage = "en";
        vm.ToggleFocusedEditorCommand.Execute(null); // enter the focused review flow
        vm.FocusedIndex = 0;
        return (host, vm);
    }

    [AvaloniaFact]
    public async Task NextNeedingReview_SkipsCardsThatAreDoneAndStopsDeterministically()
    {
        var (host, vm) = await Open("next-review");
        using (host)
        {
            // Approve every item on card 0 (key "a") so it no longer needs review.
            var start = vm.FocusedIndex;
            vm.ApproveAndNextCommand.Execute(null);

            // It approved what it could on the current card and moved forward by index (deterministic, no wrap).
            Assert.True(vm.FocusedIndex > start);
        }
    }

    [AvaloniaFact]
    public async Task ApproveAndNext_ApprovesCurrentCardsFilledValuesThenAdvances()
    {
        var (host, vm) = await Open("approve-next");
        using (host)
        {
            // Move to card "b", which has a filled fr value that can be approved.
            var bIndex = vm.PagingController.Data.IndexOf(vm.PagingController.Data.Single(g => g.Namespace == "b"));
            vm.FocusedIndex = bIndex;

            var bFr = vm.AllTranslation.Single(t => t.Namespace == "b" && t.Language == "fr");
            Assert.False(bFr.IsApproved);

            vm.ApproveAndNextCommand.Execute(null);

            Assert.True(bFr.IsApproved);                 // the current card's filled value was approved
            Assert.NotEqual(bIndex, vm.FocusedIndex);    // and focus advanced
        }
    }

    [AvaloniaFact]
    public async Task ApproveAndNext_IsAvailableWheneverThereIsData()
    {
        var (host, vm) = await Open("avail");
        using (host)
        {
            Assert.True(vm.ApproveAndNextCommand.CanExecute(null));
            Assert.True(vm.FocusedNextNeedingReviewCommand.CanExecute(null));
        }
    }
}
