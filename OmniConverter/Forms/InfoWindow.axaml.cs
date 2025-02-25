using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ManagedBass;
using ManagedBass.Midi;
using System;
using System.Diagnostics;
using System.Reflection;

namespace OmniConverter;

public partial class InfoWindow : Window
{
    public InfoWindow()
    {
        InitializeComponent();

        var pp = Environment.ProcessPath;
        var cv = Assembly.GetExecutingAssembly().GetName().Version;
        bool isDev = false;

        var dummy = new Version(0, 0, 0, 0);
        Version? bassVer = dummy;
        Version? bmidiVer = dummy;
        Version? xsynthVer = dummy;
        Version convVer = cv != null ? cv : dummy;

        try { bassVer = Bass.Version; } catch { }
        try { bmidiVer = BassMidi.Version; } catch { }
        try { xsynthVer = XSynth.Version; } catch { }

        if (Environment.ProcessPath != null)
        {
            var fcv = FileVersionInfo.GetVersionInfo(Environment.ProcessPath);

            if (fcv.ProductVersion != null && fcv.ProductVersion.Contains("dev"))
            {
                ToolTip.SetTip(ConvBrand, fcv.ProductVersion);
                isDev = true;
            }
        }

        ConvBrand.Content = MiscFunctions.ReturnAssemblyVersion($"OmniConverter{(isDev ? " DEV" : "")}", "PR", [convVer.Major, convVer.Minor, convVer.Build, convVer.Revision]);
        BASSVersion.Content = MiscFunctions.ReturnAssemblyVersion(string.Empty, "Rev. ", [bassVer.Major, bassVer.Minor, bassVer.Build, bassVer.Revision]);
        BMIDIVersion.Content = MiscFunctions.ReturnAssemblyVersion(string.Empty, "Rev. ", [bmidiVer.Major, bmidiVer.Minor, bmidiVer.Build, bmidiVer.Revision]);
        XSynthVersion.Content = MiscFunctions.ReturnAssemblyVersion(string.Empty, "Rev. ", [xsynthVer.Major, xsynthVer.Minor, xsynthVer.Build, xsynthVer.Revision]);

        SetBranchColor();
    }

    private void SetBranchColor()
    {
        SolidColorBrush brchCol = new SolidColorBrush();
        brchCol.Color = UpdateSystem.GetCurrentBranchColor();
        CurUpdateBranch.Content = UpdateSystem.GetCurrentBranch();
        CurUpdateBranch.Foreground = brchCol;
    }

    private void GitHubPage(object? sender, PointerPressedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/BlackMIDIDevs/OmniConverter") { UseShellExecute = true });
    }

    private void LicensePage(object? sender, PointerPressedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("https://github.com/BlackMIDIDevs/OmniConverter/blob/master/LICENSE.md") { UseShellExecute = true });
    }

    private async void ChangeBranch(object? sender, RoutedEventArgs e)
    {
        await new ChangeBranch().ShowDialog(this);
        SetBranchColor();
    }

    private void CheckForUpdates(object? sender, RoutedEventArgs e)
    {
        UpdateSystem.CheckForUpdates(false, false, this);
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}