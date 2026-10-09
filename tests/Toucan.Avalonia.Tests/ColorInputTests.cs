using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Toucan.Avalonia.ViewModels;
using Toucan.Avalonia.Views.Components;
using Toucan.Avalonia.Views.Dialogs;
using Toucan.Core.Contracts;
using Xunit;

namespace Toucan.Avalonia.Tests;

public class ColorInputTests
{
    private static TextBox Box(ColorInput input) => input.GetVisualDescendants().OfType<TextBox>().Single();

    private static Border Chip(ColorInput input) => input.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("colorChip"));

    private static ColorInput Shown(string hex)
    {
        var input = new ColorInput { Hex = hex };
        new Window { Content = input }.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        return input;
    }

    [AvaloniaFact]
    public void TheSwatchAndTheBoxShowTheHex()
    {
        var input = Shown("#0097A7");

        Assert.Equal("#0097A7", Box(input).Text);
        Assert.Equal(Color.Parse("#0097A7"), ((SolidColorBrush)Chip(input).Background!).Color);
    }

    [AvaloniaFact]
    public void TypingUpdatesTheHexAndTheSwatch()
    {
        var input = Shown("#0097A7");

        Box(input).Text = "#FF8800";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // TextBox reports changes through the dispatcher

        Assert.Equal("#FF8800", input.Hex);
        Assert.Equal(Color.Parse("#FF8800"), ((SolidColorBrush)Chip(input).Background!).Color);
    }

    [AvaloniaFact]
    public void AValueThatIsNotAColorIsKeptAsTypedAndLeavesTheSwatchAlone()
    {
        var input = Shown("#0097A7");

        Box(input).Text = "#12";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("#12", input.Hex);
        Assert.Equal(Color.Parse("#0097A7"), ((SolidColorBrush)Chip(input).Background!).Color);
    }

    [AvaloniaFact]
    public void ThePickerWritesBackAnOpaqueHex()
    {
        var input = Shown("#0097A7");
        var picker = ((Flyout)input.GetVisualDescendants().OfType<Button>().Single().Flyout!).Content as ColorView;
        Assert.NotNull(picker);
        Assert.Equal(Color.Parse("#0097A7"), picker.Color);

        picker.Color = Color.FromArgb(0x80, 0x11, 0x22, 0x33);

        Assert.Equal("#112233", input.Hex);
        Assert.Equal("#112233", Box(input).Text);
    }

    [AvaloniaFact]
    public void ThePickerRendersUnderTheAppTheme()
    {
        var view = new ColorView { IsAlphaVisible = false, Color = Color.Parse("#0097A7") };
        var window = new Window { Width = 400, Height = 500, Content = view };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // An unstyled ColorView has no template, so it would open as an empty popup.
        Assert.NotEmpty(view.GetVisualDescendants());
        Assert.True(view.Bounds.Height > 100);
        window.Close();
    }

    [AvaloniaFact]
    public void TheInvalidFlagMarksTheBox()
    {
        var input = Shown("#0097A7");
        input.IsInvalid = true;
        Assert.Contains("invalid", Box(input).Classes);
        input.IsInvalid = false;
        Assert.DoesNotContain("invalid", Box(input).Classes);
    }

    [AvaloniaFact]
    public void EveryColorSettingOnTheAppearancePageUsesIt()
    {
        using var host = new TestHost();
        var vm = new OptionsViewModel(host.Services.GetRequiredService<IPreferenceService>(), host.Services.GetRequiredService<IProjectDefaultsService>(), host.Dialogs, host.Messages);
        vm.SelectedPageIndex = 1;
        var dialog = new OptionsDialog(vm);
        dialog.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var inputs = dialog.GetVisualDescendants().OfType<ColorInput>().ToList();

        // The accent color, plus one per editable theme color.
        Assert.Equal(1 + vm.SchemeColorItems.Count, inputs.Count);
        Assert.Contains(inputs, i => i.Hex == vm.AccentHex);

        // Choosing a color in the accent picker changes the setting.
        var accent = inputs.First(i => i.Hex == vm.AccentHex);
        accent.Hex = "#112233";
        Assert.Equal("#112233", vm.AccentHex);
        dialog.Close();
    }
}
