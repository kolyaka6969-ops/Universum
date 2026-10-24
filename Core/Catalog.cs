using System.Reflection;
using System.Text.Json;

namespace Universum.Core;

public sealed class AppDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Description { get; set; } = "";
    public string Repo { get; set; } = "";
    /// <summary>AppId из Inno Setup (с фигурными скобками) — по нему ищем установку в реестре.</summary>
    public string AppId { get; set; } = "";
    public string Exe { get; set; } = "";
    public string AssetPattern { get; set; } = @"\.exe$";
    /// <summary>Отдельная сборка для Windows 10, если она есть в релизе.</summary>
    public string? Win10AssetPattern { get; set; }
    /// <summary>Путь к иконке внутри репозитория.</summary>
    public string? Icon { get; set; }
    public string Accent { get; set; } = "#8B7CFF";
}

public static class Catalog
{
    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };

    /// <summary>
    /// catalog.json рядом с exe имеет приоритет — так новые приложения можно добавить без пересборки.
    /// </summary>
    public static List<AppDefinition> Load()
    {
        var local = Path.Combine(AppContext.BaseDirectory, "catalog.json");
        string json;
        if (File.Exists(local))
        {
            json = File.ReadAllText(local);
        }
        else
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().First(n => n.EndsWith("catalog.json", StringComparison.OrdinalIgnoreCase));
            using var stream = asm.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }
        return JsonSerializer.Deserialize<List<AppDefinition>>(json, Options) ?? [];
    }
}

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Universum");
    public static string Downloads => Ensure(Path.Combine(Root, "downloads"));
    public static string Icons => Ensure(Path.Combine(Root, "icons"));
    public static string Cache => Ensure(Root);

    static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}

public static class VersionUtil
{
    public static Version? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var core = new string(s.Trim().TrimStart('v', 'V').TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
        if (!core.Contains('.')) core += ".0";
        if (!Version.TryParse(core, out var v)) return null;
        // 1.0 и 1.0.0 должны считаться одинаковыми
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
    }

    public static bool IsNewer(string? latest, string? installed) =>
        Parse(latest) is { } l && Parse(installed) is { } i && l > i;
}
