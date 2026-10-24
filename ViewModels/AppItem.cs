using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Universum.Core;

namespace Universum.ViewModels;

public sealed class AppItem : ObservableObject
{
    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    static readonly bool IsWindows10 = Environment.OSVersion.Version.Build < 22000;

    readonly MainViewModel _owner;
    CancellationTokenSource? _cts;

    public AppItem(AppDefinition def, MainViewModel owner)
    {
        Def = def;
        _owner = owner;

        var color = (Color)ColorConverter.ConvertFromString(def.Accent);
        Accent = Freeze(new SolidColorBrush(color));
        Glow = Freeze(new LinearGradientBrush(Color.FromArgb(0x2A, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B), new Point(0, 0), new Point(0.7, 1)));

        PrimaryCommand = new RelayCommand(async () => await PrimaryAsync(), () => PrimaryEnabled);
        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => CanCancel);
        OpenGitHubCommand = new RelayCommand(() => InstallService.OpenUrl($"https://github.com/{Def.Repo}"));
        OpenReleaseCommand = new RelayCommand(() => InstallService.OpenUrl(Release?.HtmlUrl ?? $"https://github.com/{Def.Repo}/releases"));
        OpenFolderCommand = new RelayCommand(() => InstallService.OpenUrl(Installed!.Location!), () => Installed?.Location is { Length: > 0 });
        ReinstallCommand = new RelayCommand(async () => await InstallAsync(), () => IsInstalled && Asset != null && !IsBusy);
        UninstallCommand = new RelayCommand(async () => await UninstallAsync(), () => Installed?.Uninstaller != null && !IsBusy);
    }

    public AppDefinition Def { get; }
    public string Name => Def.Name;
    public string Tagline => Def.Tagline;
    public string Description => Def.Description;
    public string Monogram => Def.Name[..1];
    public Brush Accent { get; }
    public Brush Glow { get; }

    ImageSource? _icon;
    public ImageSource? Icon { get => _icon; private set { if (Set(ref _icon, value)) OnPropertyChanged(nameof(HasIcon)); } }
    public bool HasIcon => Icon != null;

    public InstalledInfo? Installed { get; private set; }
    public ReleaseInfo? Release { get; private set; }
    public ReleaseAsset? Asset { get; private set; }
    public bool ReleaseFromCache { get; private set; }

    public bool IsInstalled => Installed != null;
    public string? InstalledVersion => Installed?.Version;
    public string? LatestVersion => Release?.Version;
    public bool HasUpdate => IsInstalled && Asset != null && VersionUtil.IsNewer(LatestVersion, InstalledVersion);

    public string VersionLabel =>
        HasUpdate ? $"v{InstalledVersion}  →  v{LatestVersion}"
        : IsInstalled ? $"v{InstalledVersion}"
        : LatestVersion != null ? $"v{LatestVersion}"
        : "—";

    public string Badge =>
        HasUpdate ? "Доступно обновление"
        : IsInstalled ? "Установлено"
        : Asset != null ? "Можно установить"
        : Release == null ? "Нет данных о релизе"
        : "Нет установщика";

    /// <summary>update / installed / available / none — для цвета значка.</summary>
    public string BadgeKind => HasUpdate ? "update" : IsInstalled ? "installed" : Asset != null ? "available" : "none";

    public string ReleaseText
    {
        get
        {
            if (Release == null) return "Релизы на GitHub не найдены";
            var text = $"Релиз от {Release.PublishedAt.ToLocalTime().ToString("d MMMM yyyy", Ru)}";
            if (Asset != null) text += $" · {FormatSize(Asset.Size)}";
            return text;
        }
    }

    public string PrimaryText => HasUpdate ? $"Обновить до {LatestVersion}" : IsInstalled ? "Открыть" : "Установить";
    public bool PrimaryIsOutline => IsInstalled && !HasUpdate;
    public bool PrimaryEnabled => !IsBusy && (IsInstalled || Asset != null);

    bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) OnPropertyChanged(null); } }

    bool _canCancel;
    public bool CanCancel { get => _canCancel; private set => Set(ref _canCancel, value); }

    double _progress;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }

    bool _isIndeterminate;
    public bool IsIndeterminate { get => _isIndeterminate; private set => Set(ref _isIndeterminate, value); }

    string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    public ICommand PrimaryCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand OpenGitHubCommand { get; }
    public ICommand OpenReleaseCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ReinstallCommand { get; }
    public ICommand UninstallCommand { get; }

    public string ReleaseMenuText => LatestVersion != null ? $"Что нового в {LatestVersion}" : "Все релизы";

    public void RefreshInstalled()
    {
        Installed = InstallService.Find(Def.AppId);
        OnPropertyChanged(null);
    }

    public void ApplyRelease(ReleaseInfo? release, bool fromCache)
    {
        Release = release;
        ReleaseFromCache = fromCache;
        Asset = release == null ? null : PickAsset(release);
        OnPropertyChanged(null);
    }

    ReleaseAsset? PickAsset(ReleaseInfo release)
    {
        if (IsWindows10 && Def.Win10AssetPattern is { } win10
            && release.Assets.FirstOrDefault(a => Regex.IsMatch(a.Name, win10, RegexOptions.IgnoreCase)) is { } w)
            return w;
        return release.Assets.FirstOrDefault(a => Regex.IsMatch(a.Name, Def.AssetPattern, RegexOptions.IgnoreCase));
    }

    public async Task LoadIconAsync()
    {
        if (Icon != null || Def.Icon is not { Length: > 0 } iconPath) return;
        var cache = Path.Combine(AppPaths.Icons, Def.Id + Path.GetExtension(iconPath));

        byte[]? bytes = File.Exists(cache) ? await File.ReadAllBytesAsync(cache) : null;
        if (bytes == null)
        {
            bytes = await GitHubService.TryGetBytesAsync($"https://raw.githubusercontent.com/{Def.Repo}/HEAD/{iconPath}", CancellationToken.None);
            if (bytes == null) return;
            try { await File.WriteAllBytesAsync(cache, bytes); } catch { }
        }
        Icon = Decode(bytes);
    }

    static ImageSource? Decode(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).ThenByDescending(f => f.Format.BitsPerPixel).First();
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }

    async Task PrimaryAsync()
    {
        if (IsInstalled && !HasUpdate)
        {
            if (InstallService.ExePath(Installed, Def.Exe) is { } exe) InstallService.Launch(exe);
            else MessageBox.Show($"Не найден {Def.Exe}. Попробуйте переустановить {Name}.", "Universum", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await InstallAsync();
    }

    public async Task InstallAsync()
    {
        if (Asset == null || Release == null || IsBusy) return;

        var running = InstallService.GetRunning(Def.Exe);
        var wasRunning = running.Length > 0;
        if (wasRunning)
        {
            var answer = MessageBox.Show($"{Name} сейчас запущен. Закрыть его и продолжить? После обновления он запустится снова.",
                "Universum", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                foreach (var p in running) p.Dispose();
                return;
            }
        }

        var updating = IsInstalled;
        _cts = new CancellationTokenSource();
        IsBusy = true;
        CanCancel = true;
        var file = Path.Combine(AppPaths.Downloads, Asset.Name);
        try
        {
            Status = "Загрузка…";
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                Progress = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                IsIndeterminate = p.Total <= 0;
                Status = p.Total > 0 ? $"Загрузка · {FormatSize(p.Done)} из {FormatSize(p.Total)}" : $"Загрузка · {FormatSize(p.Done)}";
            });
            await GitHubService.DownloadAsync(Asset.Url, file, progress, _cts.Token);

            if (Release.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) is { } sums)
            {
                Status = "Проверка файла…";
                IsIndeterminate = true;
                await GitHubService.VerifySha256Async(sums.Url, file, Asset.Name, _cts.Token);
            }

            CanCancel = false;
            IsIndeterminate = true;
            if (wasRunning)
            {
                Status = $"Закрываем {Name}…";
                await InstallService.CloseAsync(running);
            }
            Status = updating ? "Обновление…" : "Установка…";
            await InstallService.RunInstallerAsync(file);
            GitHubService.TryDelete(file);

            RefreshInstalled();
            if (!IsInstalled) throw new InvalidOperationException("Установщик завершился, но приложение не найдено.");
            if (wasRunning && InstallService.ExePath(Installed, Def.Exe) is { } exe) InstallService.Launch(exe);
        }
        catch (OperationCanceledException)
        {
            GitHubService.TryDelete(file);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{(updating ? "Не удалось обновить" : "Не удалось установить")} {Name}.\n\n{ex.Message}", "Universum", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            CanCancel = false;
            IsIndeterminate = false;
            Progress = 0;
            Status = "";
            IsBusy = false;
            RefreshInstalled();
            _owner.OnItemChanged();
        }
    }

    async Task UninstallAsync()
    {
        if (Installed?.Uninstaller is not { } uninstaller) return;
        if (MessageBox.Show($"Удалить {Name} с компьютера?", "Universum", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        IsBusy = true;
        IsIndeterminate = true;
        try
        {
            var running = InstallService.GetRunning(Def.Exe);
            if (running.Length > 0)
            {
                Status = $"Закрываем {Name}…";
                await InstallService.CloseAsync(running);
            }
            Status = "Удаление…";
            await InstallService.UninstallAsync(Def.AppId, uninstaller);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось удалить {Name}.\n\n{ex.Message}", "Universum", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsIndeterminate = false;
            Status = "";
            IsBusy = false;
            RefreshInstalled();
            _owner.OnItemChanged();
        }
    }

    static string FormatSize(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.0} МБ" : $"{Math.Max(1, bytes / 1024)} КБ";

    static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
