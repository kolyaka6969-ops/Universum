using System.Diagnostics;
using System.Reflection;

namespace Universum.Core;

/// <summary>
/// Обновление самого Universum из его GitHub Releases. Запущенный exe нельзя перезаписать, но можно переименовать:
/// старый становится Universum.exe.old, на его место кладётся новый, он запускается, а этот процесс закрывается.
/// Новый при старте удаляет .old.
/// </summary>
public static class SelfUpdate
{
    public const string Repo = "kolyaka6969-ops/Universum";
    const string AssetName = "Universum.exe";

    public static string CurrentVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{Math.Max(v.Build, 0)}" : "0.0.0";

    static string? ExePath => Environment.ProcessPath;
    static string? OldPath => ExePath is { } exe ? exe + ".old" : null;

    /// <summary>Релиз новее запущенной версии, в котором есть Universum.exe; иначе null.</summary>
    public static async Task<(ReleaseInfo Release, ReleaseAsset Asset)?> CheckAsync(GitHubService github, CancellationToken ct)
    {
        var (release, _) = await github.GetLatestAsync(Repo, ct);
        if (release == null || !VersionUtil.IsNewer(release.Version, CurrentVersion)) return null;
        var asset = release.Assets.FirstOrDefault(a => a.Name.Equals(AssetName, StringComparison.OrdinalIgnoreCase));
        return asset == null ? null : (release, asset);
    }

    /// <summary>Скачивает, проверяет, подменяет exe и запускает новую версию. После успеха приложение нужно закрыть.</summary>
    public static async Task ApplyAsync(ReleaseInfo release, ReleaseAsset asset, IProgress<(long Done, long Total)> progress, CancellationToken ct)
    {
        var exe = ExePath ?? throw new InvalidOperationException("Не удалось определить путь к Universum.exe.");
        var old = OldPath!;
        var file = Path.Combine(AppPaths.Downloads, $"Universum-{release.Version}.exe");

        await GitHubService.DownloadAsync(asset.Url, file, progress, ct);
        if (release.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) is { } sums)
            await GitHubService.VerifySha256Async(sums.Url, file, AssetName, ct);

        GitHubService.TryDelete(old);
        File.Move(exe, old);
        try
        {
            File.Move(file, exe);
        }
        catch
        {
            // Вернуть как было, чтобы не остаться без exe
            File.Move(old, exe);
            throw;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>Удаляет exe прошлой версии. Старый процесс может ещё закрываться — пробуем несколько раз.</summary>
    public static async Task CleanupAsync()
    {
        if (OldPath is not { } old) return;
        for (var i = 0; i < 10 && File.Exists(old); i++)
        {
            GitHubService.TryDelete(old);
            if (File.Exists(old)) await Task.Delay(500);
        }
    }
}
