using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Toucan.ViewModels;

namespace Toucan.Views.Components;

/// <summary>
/// Reusable language list editor with add/remove. Bind Items to an ObservableCollection&lt;LanguageEntry&gt;.
/// Fires LanguageAdded/LanguageRemoved routed events for the host to handle persistence.
/// </summary>
public partial class LanguageListEditor : UserControl
{
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(nameof(Items), typeof(IList), typeof(LanguageListEditor));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(LanguageListEditor),
            new PropertyMetadata("Type a language code or name"));

    public static readonly DependencyProperty AllowSetPrimaryProperty =
        DependencyProperty.Register(nameof(AllowSetPrimary), typeof(bool), typeof(LanguageListEditor),
            new PropertyMetadata(false));

    public IList Items
    {
        get => (IList)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool AllowSetPrimary
    {
        get => (bool)GetValue(AllowSetPrimaryProperty);
        set => SetValue(AllowSetPrimaryProperty, value);
    }

    /// <summary>Raised when a language is added.</summary>
    public event EventHandler<LanguageEntryEventArgs>? LanguageAdded;

    /// <summary>Raised when a language is removed.</summary>
    public event EventHandler<LanguageEntryEventArgs>? LanguageRemoved;

    public LanguageListEditor()
    {
        InitializeComponent();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e) => TryAdd();

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { TryAdd(); e.Handled = true; }
    }

    private void TryAdd()
    {
        var text = InputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text) || Items == null) return;

        // Check duplicate
        foreach (var item in Items)
        {
            if (item is LanguageEntry existing && string.Equals(existing.Code, text, StringComparison.OrdinalIgnoreCase))
                return;
        }

        var entry = new LanguageEntry
        {
            Code = text,
            DisplayName = GetDisplayName(text),
            IsPrimary = false,
            CanRemove = true,
        };

        Items.Add(entry);
        LanguageAdded?.Invoke(this, new LanguageEntryEventArgs(entry));
        InputBox.Text = string.Empty;
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LanguageEntry entry }) return;
        if (!entry.CanRemove) return;

        Items?.Remove(entry);
        LanguageRemoved?.Invoke(this, new LanguageEntryEventArgs(entry));
    }

    private static string GetDisplayName(string code)
    {
        try { return CultureInfo.GetCultureInfo(code).DisplayName; }
        catch { return code; }
    }
}

/// <summary>EventArgs for language add/remove events.</summary>
public class LanguageEntryEventArgs(LanguageEntry entry) : EventArgs
{
    public LanguageEntry Entry { get; } = entry;
}
