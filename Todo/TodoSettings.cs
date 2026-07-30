using System;
using Microsoft.Win32;

namespace CaffeineWin.Todo;

/// <summary>
/// Todo preferences, read straight from the registry on each access so the Settings panel and the
/// view never hold two copies of the same value.
/// </summary>
public static class TodoSettings
{
    private const string SettingsPath = @"Software\CaffeineWin";

    public static TaskSort Sort
    {
        get => Read("TodoSort") is int value && Enum.IsDefined(typeof(TaskSort), value)
            ? (TaskSort)value
            : TaskSort.Manual;
        set => Write("TodoSort", (int)value);
    }

    public static TaskDensity Density
    {
        get => Read("TodoDensity") is int value && Enum.IsDefined(typeof(TaskDensity), value)
            ? (TaskDensity)value
            : TaskDensity.Comfortable;
        set => Write("TodoDensity", (int)value);
    }

    /// <summary>Whether the Completed section starts expanded.</summary>
    public static bool CompletedOpen
    {
        get => Read("TodoCompletedOpen") is not int value || value != 0;
        set => Write("TodoCompletedOpen", value ? 1 : 0);
    }

    /// <summary>The hour a date-only task is treated as due at, and the default for a new time.</summary>
    public static int DefaultDueHour
    {
        get => Read("TodoDueHour") is int value and >= 0 and <= 23 ? value : 9;
        set => Write("TodoDueHour", Math.Clamp(value, 0, 23));
    }

    public static int DefaultDueMinute
    {
        get => Read("TodoDueMinute") is int value and >= 0 and <= 59 ? value : 0;
        set => Write("TodoDueMinute", Math.Clamp(value, 0, 59));
    }

    private static object? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsPath);
        return key?.GetValue(name);
    }

    private static void Write(string name, int value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsPath);
        key?.SetValue(name, value, RegistryValueKind.DWord);
    }
}
