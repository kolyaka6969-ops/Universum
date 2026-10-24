using System.Diagnostics;
using Microsoft.Win32;

namespace Universum.Core;

public sealed record InstalledInfo(string Version, string? Location, string? Uninstaller);

/// <summary>Работа с приложениями, установленными через Inno Setup.</summary>
public static class InstallService
{
    const string SilentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";

    public static InstalledInfo? Find(string appId)
    {
        var keyPath = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{appId}_is1";
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(keyPath);
                if (key == null) continue;
                return new InstalledInfo(
                    key.GetValue("DisplayVersion") as string ?? "",
                    key.GetValue("InstallLocation") as string,
                    key.GetValue("UninstallString") as string);
            }
            catch
            {
                // нет доступа к ветке — пробуем следующую
            }
        }
        return null;
    }

    public static string? ExePath(InstalledInfo? info, string exe)
    {
        if (info?.Location is not { Length: > 0 } dir) return null;
        var path = Path.Combine(dir, exe);
        return File.Exists(path) ? path : null;
    }

    public static Process[] GetRunning(string exe) =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));

    public static async Task CloseAsync(Process[] processes)
    {
        foreach (var p in processes)
        {
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(1500)) p.Kill(entireProcessTree: true);
                await p.WaitForExitAsync();
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    public static void Launch(string path) =>
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static async Task RunInstallerAsync(string setupPath)
    {
        using var p = Process.Start(new ProcessStartInfo(setupPath, SilentArgs) { UseShellExecute = true })
                      ?? throw new InvalidOperationException("Не удалось запустить установщик.");
        await p.WaitForExitAsync();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"Установщик завершился с кодом {p.ExitCode}.");
    }

    public static async Task UninstallAsync(string appId, string uninstallString)
    {
        var exe = uninstallString.Trim().Trim('"');
        using (var p = Process.Start(new ProcessStartInfo(exe, SilentArgs.Replace(" /SP-", "")) { UseShellExecute = true }))
        {
            if (p != null) await p.WaitForExitAsync();
        }
        // Деинсталлятор Inno перезапускает себя из временной папки, поэтому ждём исчезновения записи в реестре.
        for (var i = 0; i < 120 && Find(appId) != null; i++)
            await Task.Delay(500);
        if (Find(appId) != null)
            throw new InvalidOperationException("Удаление не завершилось.");
    }
}
