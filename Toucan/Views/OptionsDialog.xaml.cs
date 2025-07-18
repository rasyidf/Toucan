﻿using Toucan.Core.Models;
using System;
using System.Globalization;
using System.Windows;
using Wpf.Ui.Controls.Window;
using Toucan.Core.Options;

namespace Toucan;

/// <summary>
/// Interaction logic for Options.xaml
/// </summary>
public partial class OptionDialog : FluentWindow
{
    public AppOptions Config { get; private set; }

    public OptionDialog(AppOptions importOptions)
    {
        Config = importOptions;

        InitializeComponent();

        SaveStyleCombobox.SelectedIndex = (int)Config.SaveStyle;

        PageSizeText.Text = importOptions?.PageSize.ToString(CultureInfo.InvariantCulture);
        TruncateSizeText.Text = importOptions?.TruncateResultsOver.ToString(CultureInfo.InvariantCulture);

    }

    private void SaveOptions(object sender, RoutedEventArgs e)
    {
        AppOptions newOptions = new()
        {
            SaveStyle = (SaveStyles)SaveStyleCombobox.SelectedIndex,
            PageSize = Convert.ToInt32(PageSizeText.Text),
            TruncateResultsOver = Convert.ToInt32(TruncateSizeText.Text),
            DefaultPath = Config.DefaultPath
        };
        Config = newOptions;
        newOptions.ToDisk();
        DialogResult = true;
    }

    private void CloseOptions(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
