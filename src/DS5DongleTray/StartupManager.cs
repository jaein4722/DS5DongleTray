using Microsoft.Win32;
using System.Diagnostics;

namespace DS5DongleTray;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DS5DongleTray";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public static string CurrentCommand()
    {
        return $"\"{Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? Application.ExecutablePath}\"";
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Could not open the current-user startup registry key.");

        if (enabled)
        {
            key.SetValue(ValueName, CurrentCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
