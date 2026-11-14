using System.IO;

namespace SimsModManager.App.Services;

/// <summary>
/// Ярлыки после переименования Sims Mod Manager → Ancora. Установщик создавал «Sims Mod Manager» на рабочем столе
/// и в «Пуске», и они вели на SimsModManager.exe, которого после обновления больше нет.
/// Такой ярлык заменяем на «Ancora», ведущий на текущую программу. Чужие ярлыки с тем же именем не трогаем.
/// </summary>
internal static class LegacyShortcuts
{
    public static void Fix()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null) return;
            var installRoot = Path.GetDirectoryName(Path.GetDirectoryName(exe));   // …\SimsModManager\current\Ancora.exe
            if (installRoot is null || !File.Exists(Path.Combine(installRoot, "Update.exe"))) return;   // не установлена — portable

            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            };
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;
            dynamic shell = Activator.CreateInstance(shellType)!;

            foreach (var folder in folders)
            {
                var old = Path.Combine(folder, Core.AppIdentity.LegacyName + ".lnk");
                if (!File.Exists(old)) continue;
                dynamic oldLink = shell.CreateShortcut(old);
                string target = oldLink.TargetPath ?? "";
                if (!target.StartsWith(installRoot, StringComparison.OrdinalIgnoreCase)) continue;

                var fresh = Path.Combine(folder, Core.AppIdentity.Name + ".lnk");
                if (!File.Exists(fresh))
                {
                    dynamic link = shell.CreateShortcut(fresh);
                    link.TargetPath = exe;
                    link.WorkingDirectory = Path.GetDirectoryName(exe);
                    link.IconLocation = exe + ",0";
                    link.Description = Core.AppIdentity.Name;
                    link.Save();
                }
                File.Delete(old);
            }
        }
        catch (Exception)
        {
            // Ярлыки — мелочь: если не вышло, программа всё равно работает
        }
    }
}
