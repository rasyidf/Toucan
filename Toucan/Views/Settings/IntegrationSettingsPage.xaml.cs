using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Toucan.Services;

namespace Toucan.Views.Settings;

public partial class IntegrationSettingsPage : UserControl
{
    public IntegrationSettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
        SettingsPathText.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");
    }

    private void RefreshStatus()
    {
        var installed = FileAssociationService.IsInstalled();
        StatusText.Text = installed ? "Installed" : "Not installed";
        StatusBadge.Background = installed
            ? new SolidColorBrush(Color.FromArgb(40, 0, 180, 0))
            : new SolidColorBrush(Color.FromArgb(40, 180, 0, 0));
        StatusText.Foreground = installed
            ? new SolidColorBrush(Color.FromRgb(80, 220, 80))
            : new SolidColorBrush(Color.FromRgb(220, 80, 80));

        InstallButton.IsEnabled = !installed;
        UninstallButton.IsEnabled = installed;
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        FileAssociationService.Install();
        RefreshStatus();
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        FileAssociationService.Clear();
        RefreshStatus();
    }

    private void ShellInstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Directory\shell\Toucan");
            key?.SetValue("", "Open with Toucan");
            key?.SetValue("Icon", $"\"{exePath}\",0");

            using var cmdKey = key?.CreateSubKey("command");
            cmdKey?.SetValue("", $"\"{exePath}\" \"%1\"");

            MessageBox.Show("Context menu entry installed.", "Toucan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed: {ex.Message}", "Toucan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShellUninstall_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Directory\shell\Toucan", false);
            MessageBox.Show("Context menu entry removed.", "Toucan", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed: {ex.Message}", "Toucan", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Toucan");
        if (Directory.Exists(path))
            Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        else
            Directory.CreateDirectory(path);
    }
}
