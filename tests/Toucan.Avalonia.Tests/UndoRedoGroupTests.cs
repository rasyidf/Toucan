using Toucan.Avalonia.Services;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class UndoRedoGroupTests
{
    [Fact]
    public void EditsInAGroupUndoAndRedoAsOneStepInOrder()
    {
        var undo = new UndoRedoService();
        using (undo.BeginGroup())
        {
            undo.Record("a", "en", "1", "2");
            undo.Record("b", "en", "x", "y");
            undo.Record("a", "en", "2", "3");
        }

        var step = undo.Undo()!;
        Assert.Equal(["a", "b", "a"], step.Select(e => e.Namespace)); // as made; the caller undoes them last to first
        Assert.False(undo.CanUndo);
        Assert.Equal(step, undo.Redo());
        Assert.True(undo.CanUndo);
    }

    [Fact]
    public void PlainEditsAreTheirOwnSteps()
    {
        var undo = new UndoRedoService();
        undo.Record("a", "en", "1", "2");
        undo.Record("a", "en", "2", "3");

        Assert.Single(undo.Undo()!);
        Assert.Single(undo.Undo()!);
        Assert.Null(undo.Undo());
    }

    [Fact]
    public void NestedGroupsMakeOneStepAndEmptyGroupsNone()
    {
        var undo = new UndoRedoService();
        using (undo.BeginGroup())
        {
            undo.Record("a", "en", "1", "2");
            using (undo.BeginGroup()) undo.Record("b", "en", "1", "2");
        }
        using (undo.BeginGroup()) { }
        using (undo.BeginGroup()) undo.Record("c", "en", "same", "same"); // no change, nothing to undo

        Assert.Equal(2, undo.Undo()!.Count);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void AGroupClearsTheRedoStackAndDisposingTwiceIsHarmless()
    {
        var undo = new UndoRedoService();
        undo.Record("a", "en", "1", "2");
        undo.Undo();
        Assert.True(undo.CanRedo);

        var group = undo.BeginGroup();
        undo.Record("b", "en", "1", "2");
        group.Dispose();
        group.Dispose();

        Assert.False(undo.CanRedo);
        Assert.Single(undo.Undo()!);
        Assert.False(undo.CanUndo);
    }
}
