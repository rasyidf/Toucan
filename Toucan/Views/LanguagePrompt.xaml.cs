using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Wpf.Ui.Controls.Window;
using System.Linq;
using System;
using Toucan.ViewModels;
using Toucan.Core.Models;
using Toucan.Core;

namespace Toucan;

/// <summary>
/// Interaction logic for PromptDialog.xaml
/// </summary>
partial class LanguagePrompt : FluentWindow
{
    public List<TranslationItem> LanguageList { get; set; }

    public LanguagePromptViewModel ViewModel { get; }

    public LanguagePrompt(string title, string message, List<TranslationItem> languageList)
    {
        InitializeComponent();
        titleBarPrompt.Title = title;
        messageLabel.Text = message;
        ResponseLanguage.Focus();

        RoutedCommand saveCommand = new();
        saveCommand.InputGestures.Add(new KeyGesture(Key.Enter, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(saveCommand, OKButton_Click));

        RoutedCommand refreshCommand = new();
        refreshCommand.InputGestures.Add(new KeyGesture(Key.Escape, ModifierKeys.None));
        CommandBindings.Add(new CommandBinding(refreshCommand, CancelDialog));

        ViewModel = new LanguagePromptViewModel();
        LanguageList = languageList;
    }

    public string ResponseText
    {
        get { return (ResponseLanguage?.SelectedValue as LanguageModel)?.Language; }
        set { ResponseLanguage.SelectedItem = value; }
    }

    private void CancelDialog(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
    private void OKButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
