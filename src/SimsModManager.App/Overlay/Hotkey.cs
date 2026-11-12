using System.Windows.Input;

namespace SimsModManager.App.Overlay;

/// <summary>Клавиша или сочетание для оверлея: «F9», «Ctrl+Shift+M».</summary>
public sealed record Hotkey(ModifierKeys Modifiers, Key Key)
{
    public static readonly Hotkey Default = new(ModifierKeys.None, Key.F9);

    public uint NativeModifiers =>
        (Modifiers.HasFlag(ModifierKeys.Control) ? Native.MOD_CONTROL : 0) |
        (Modifiers.HasFlag(ModifierKeys.Shift) ? Native.MOD_SHIFT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Alt) ? Native.MOD_ALT : 0) |
        (Modifiers.HasFlag(ModifierKeys.Windows) ? Native.MOD_WIN : 0) |
        Native.MOD_NOREPEAT;

    public uint VirtualKey => (uint)KeyInterop.VirtualKeyFromKey(Key);

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        Key.Oem3 => "`",
        _ => key.ToString(),
    };

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Default;
        var modifiers = ModifierKeys.None;
        Key? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= ModifierKeys.Control; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                case "`": key = Key.Oem3; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Key.D0 + (raw[0] - '0');
                    else if (Enum.TryParse<Key>(raw, ignoreCase: true, out var k)) key = k;
                    break;
            }
        }
        return key is Key value && !IsModifier(value) ? new Hotkey(modifiers, value) : Default;
    }

    /// <summary>Сочетание из нажатия в поле настройки. null — нажат только модификатор, ждём дальше.</summary>
    public static Hotkey? FromKeyPress(Key key, ModifierKeys modifiers)
    {
        if (IsModifier(key)) return null;
        // Одиночные буквы и цифры без модификаторов мешали бы играть
        var plainLetter = modifiers == ModifierKeys.None && (key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9);
        if (plainLetter || key is Key.Escape or Key.Enter or Key.Space or Key.Tab) return null;
        return new Hotkey(modifiers, key);
    }

    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;
}
