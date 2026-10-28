using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Universum.Core;

namespace Universum.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    readonly GitHubService _github = new();
    readonly List<AppItem> _all;

    public MainViewModel()
    {
        _all = Catalog.Load().Select(d => new AppItem(d, this)).ToList();
        RefreshCommand = new RelayCommand(async () => await RefreshAsync(), () => !IsRefreshing);
        UpdateAllCommand = new RelayCommand(async () => await UpdateAllAsync(), () => HasUpdates && !_all.Any(a => a.IsBusy));
        SortCommand = new RelayCommand(p => SortMode = int.Parse((string)p!));
        SelfUpdateCommand = new RelayCommand(async () => await ApplySelfUpdateAsync(), () => _selfUpdate != null && !IsSelfUpdating);
        Regroup();
    }

    // ---------------------------------------------------------------- обновление самого Universum

    (ReleaseInfo Release, ReleaseAsset Asset)? _selfUpdate;

    public ICommand SelfUpdateCommand { get; }
    public string AppVersionLabel => $"v{SelfUpdate.CurrentVersion}";
    public bool HasSelfUpdate => _selfUpdate != null;
    public string SelfUpdateTitle => _selfUpdate is { } u ? $"Доступен Universum {u.Release.Version}" : "";
    public string SelfUpdateSubtitle => _selfUpdate is { } u
        ? $"Сейчас v{SelfUpdate.CurrentVersion} · {FormatSize(u.Asset.Size)} · хаб перезапустится сам"
        : "";

    bool _isSelfUpdating;
    public bool IsSelfUpdating { get => _isSelfUpdating; private set => Set(ref _isSelfUpdating, value); }

    double _selfUpdateProgress;
    public double SelfUpdateProgress { get => _selfUpdateProgress; private set => Set(ref _selfUpdateProgress, value); }

    string _selfUpdateStatus = "";
    public string SelfUpdateStatus { get => _selfUpdateStatus; private set => Set(ref _selfUpdateStatus, value); }

    async Task CheckSelfUpdateAsync()
    {
        try
        {
            _selfUpdate = await SelfUpdate.CheckAsync(_github, CancellationToken.None);
        }
        catch
        {
            // без сети — проверим в следующий раз
        }
        OnPropertyChanged(nameof(HasSelfUpdate));
        OnPropertyChanged(nameof(SelfUpdateTitle));
        OnPropertyChanged(nameof(SelfUpdateSubtitle));
        CommandManager.InvalidateRequerySuggested();
    }

    async Task ApplySelfUpdateAsync()
    {
        if (_selfUpdate is not { } update || IsSelfUpdating) return;
        if (_all.Any(a => a.IsBusy))
        {
            MessageBox.Show("Дождитесь окончания установки приложений — затем Universum обновится.", "Universum",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        IsSelfUpdating = true;
        SelfUpdateStatus = "Загрузка…";
        try
        {
            var progress = new Progress<(long Done, long Total)>(p =>
            {
                SelfUpdateProgress = p.Total > 0 ? p.Done * 100.0 / p.Total : 0;
                SelfUpdateStatus = p.Total > 0 ? $"Загрузка · {FormatSize(p.Done)} из {FormatSize(p.Total)}" : $"Загрузка · {FormatSize(p.Done)}";
            });
            await SelfUpdate.ApplyAsync(update.Release, update.Asset, progress, CancellationToken.None);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            IsSelfUpdating = false;
            var answer = MessageBox.Show($"Не удалось обновить Universum: {ex.Message}\n\nОткрыть страницу релиза, чтобы скачать вручную?",
                "Universum", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes) InstallService.OpenUrl(update.Release.HtmlUrl);
        }
    }

    static string FormatSize(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.#} МБ" : $"{bytes / 1024.0:0} КБ";

    public ObservableCollection<AppItem> InstalledApps { get; } = [];
    public ObservableCollection<AppItem> AvailableApps { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand SortCommand { get; }

    string _search = "";
    public string Search { get => _search; set { if (Set(ref _search, value)) Regroup(); } }

    int _sortMode;
    /// <summary>0 — по имени, 1 — сначала обновления, 2 — по дате релиза.</summary>
    public int SortMode { get => _sortMode; set { if (Set(ref _sortMode, value)) Regroup(); } }

    bool _isRefreshing;
    public bool IsRefreshing { get => _isRefreshing; private set => Set(ref _isRefreshing, value); }

    string _footer = "Проверка обновлений…";
    public string Footer { get => _footer; private set => Set(ref _footer, value); }

    public int UpdateCount => _all.Count(a => a.HasUpdate);
    public bool HasUpdates => UpdateCount > 0;
    public string UpdateAllText => $"Обновить {UpdateCount} {Plural(UpdateCount, "приложение", "приложения", "приложений")}";
    public bool HasInstalled => InstalledApps.Count > 0;
    public bool HasAvailable => AvailableApps.Count > 0;
    public bool NothingFound => !HasInstalled && !HasAvailable;
    public string InstalledHeader => $"УСТАНОВЛЕНЫ · {InstalledApps.Count}";
    public string AvailableHeader => $"НЕ УСТАНОВЛЕНЫ · {AvailableApps.Count}";

    public async Task RefreshAsync()
    {
        if (IsRefreshing) return;
        IsRefreshing = true;
        Footer = "Проверка обновлений…";

        foreach (var app in _all) app.RefreshInstalled();

        var failed = 0;
        var cached = 0;
        var self = CheckSelfUpdateAsync();
        await Task.WhenAll(_all.Select(async app =>
        {
            try
            {
                var (release, fromCache) = await _github.GetLatestAsync(app.Def.Repo, CancellationToken.None);
                app.ApplyRelease(release, fromCache);
                if (fromCache) cached++;
            }
            catch
            {
                failed++;
            }
        }));

        _ = Task.WhenAll(_all.Select(a => a.LoadIconAsync()));
        await self;

        var time = DateTime.Now.ToString("HH:mm");
        Footer = failed + cached == 0
            ? $"Проверено в {time} · через GitHub Releases"
            : failed == _all.Count
                ? $"Нет связи с GitHub · {time}"
                : $"Часть данных из кэша — GitHub недоступен · {time}";

        IsRefreshing = false;
        Regroup();
    }

    /// <summary>Быстрая проверка реестра при возврате в окно — если приложение поставили или удалили вручную.</summary>
    public void RefreshInstalledOnly()
    {
        if (IsRefreshing || _all.Any(a => a.IsBusy)) return;
        foreach (var app in _all) app.RefreshInstalled();
        Regroup();
    }

    async Task UpdateAllAsync()
    {
        foreach (var app in _all.Where(a => a.HasUpdate).ToList())
            await app.InstallAsync();
    }

    public void OnItemChanged()
    {
        Regroup();
        CommandManager.InvalidateRequerySuggested();
    }

    void Regroup()
    {
        IEnumerable<AppItem> items = _all;
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var q = Search.Trim();
            items = items.Where(a =>
                a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Tagline.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        items = SortMode switch
        {
            1 => items.OrderByDescending(a => a.HasUpdate).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => items.OrderByDescending(a => a.Release?.PublishedAt ?? DateTimeOffset.MinValue),
            _ => items.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase),
        };

        var list = items.ToList();
        Sync(InstalledApps, list.Where(a => a.IsInstalled));
        Sync(AvailableApps, list.Where(a => !a.IsInstalled));

        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(UpdateAllText));
        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(HasAvailable));
        OnPropertyChanged(nameof(NothingFound));
        OnPropertyChanged(nameof(InstalledHeader));
        OnPropertyChanged(nameof(AvailableHeader));
    }

    /// <summary>Обновляет коллекцию без пересоздания карточек, которые остаются на месте.</summary>
    static void Sync(ObservableCollection<AppItem> target, IEnumerable<AppItem> source)
    {
        var wanted = source.ToList();
        for (var i = target.Count - 1; i >= 0; i--)
            if (!wanted.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            var current = target.IndexOf(wanted[i]);
            if (current == -1) target.Insert(i, wanted[i]);
            else if (current != i) target.Move(current, i);
        }
    }

    static string Plural(int n, string one, string few, string many)
    {
        var mod100 = n % 100;
        var mod10 = n % 10;
        if (mod100 is >= 11 and <= 14) return many;
        return mod10 switch { 1 => one, >= 2 and <= 4 => few, _ => many };
    }
}
