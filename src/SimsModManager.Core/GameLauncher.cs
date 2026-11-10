using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SimsModManager.Core;

/// <summary>Ищет установленную The Sims 4 (EA app, Origin, Steam) и запускает её.</summary>
public static partial class GameLauncher
{
    public const string SteamAppId = "1222670";

    /// <param name="Exe">Путь к TS4_x64.exe.</param>
    /// <param name="IsSteam">Игра из Steam — запускаем через Steam, иначе он перезапустит её сам.</param>
    public sealed record GameInstall(string Exe, bool IsSteam);

    /// <summary>Находит игру: сначала путь из настроек, потом реестр EA/Origin, библиотеки Steam и обычные папки.</summary>
    public static GameInstall? Find(string? savedExe)
    {
        if (!string.IsNullOrEmpty(savedExe) && File.Exists(savedExe)) return From(savedExe);
        foreach (var dir in CandidateDirs())
        {
            if (ExeIn(dir) is { } exe) return From(exe);
        }
        return null;
    }

    /// <summary>Папка установки игры (где лежат EP01, GP02… и Game\Bin).</summary>
    public static string InstallRoot(GameInstall game)
    {
        var bin = Path.GetDirectoryName(game.Exe) ?? "";
        var gameDir = Path.GetDirectoryName(bin);
        return string.Equals(Path.GetFileName(bin), "Bin", StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetFileName(gameDir), "Game", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(gameDir) ?? bin
            : bin;
    }

    public static GameInstall From(string exe) => new(exe, exe.Contains("steamapps", StringComparison.OrdinalIgnoreCase));

    /// <summary>Исполняемый файл игры внутри папки установки (или самой папки Bin).</summary>
    public static string? ExeIn(string installDir)
    {
        foreach (var relative in new[] { @"Game\Bin\TS4_x64.exe", "TS4_x64.exe", @"Game\Bin\TS4.exe" })
        {
            var path = Path.Combine(installDir, relative.Replace('\\', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static IEnumerable<string> CandidateDirs()
    {
        if (OperatingSystem.IsWindows())
        {
            // EA app и Origin записывают папку установки сюда
            foreach (var key in new[] { @"SOFTWARE\Maxis\The Sims 4", @"SOFTWARE\WOW6432Node\Maxis\The Sims 4" })
            {
                string? dir = null;
                try
                {
                    using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
                    dir = k?.GetValue("Install Dir") as string;
                }
                catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
                if (!string.IsNullOrEmpty(dir)) yield return dir;
            }

            // Steam: все библиотеки из libraryfolders.vdf
            string? steam = null;
            try
            {
                using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                steam = k?.GetValue("SteamPath") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
            if (!string.IsNullOrEmpty(steam))
            {
                var libraries = new List<string> { steam };
                var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                try
                {
                    if (File.Exists(vdf)) libraries.AddRange(SteamLibraryFolders(File.ReadAllText(vdf)));
                }
                catch (IOException) { }
                foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
                    yield return Path.Combine(library, "steamapps", "common", "The Sims 4");
            }
        }

        // Обычные места установки
        foreach (var root in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var pf = Environment.GetFolderPath(root);
            if (string.IsNullOrEmpty(pf)) continue;
            yield return Path.Combine(pf, "EA Games", "The Sims 4");
            yield return Path.Combine(pf, "Origin Games", "The Sims 4");
            yield return Path.Combine(pf, "Steam", "steamapps", "common", "The Sims 4");
        }
    }

    /// <summary>Пути библиотек из steamapps\libraryfolders.vdf.</summary>
    internal static IEnumerable<string> SteamLibraryFolders(string vdf) =>
        SteamPathRegex().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPathRegex();

    /// <summary>Запускает игру. Steam-версию — через Steam, остальные — напрямую (EA app подхватит сам).</summary>
    public static void Launch(GameInstall game)
    {
        var info = game.IsSteam
            ? new ProcessStartInfo("steam://rungameid/" + SteamAppId) { UseShellExecute = true }
            : new ProcessStartInfo(game.Exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(game.Exe) };
        using var _ = Process.Start(info);
    }
}
