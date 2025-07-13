﻿using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace Toucan.Services;

internal interface IDialogService
{
    string? SelectFolder(string initialPath);
    bool? ShowDialog(Window dialog);
}

internal class DialogService : IDialogService
{
    public string? SelectFolder(string initialPath)
    {
        VistaFolderBrowserDialog dialog = new() { SelectedPath = initialPath };
        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.SelectedPath
            : null;
    }

    public bool? ShowDialog(Window dialog)
    {
        dialog.Owner = Application.Current.MainWindow;
        return dialog.ShowDialog();
    }

    public string SelectFile(string initialPath, string filter = "All Files (*.*)|*.*")
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            InitialDirectory = initialPath,
            Filter = filter,
            Multiselect = false
        };
        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }


}
