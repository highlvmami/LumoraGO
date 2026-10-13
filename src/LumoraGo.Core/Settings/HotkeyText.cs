namespace LumoraGo.Core.Settings;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>"Ctrl+Shift+Space" gibi kısayol metnini okur ve yazar.</summary>
public static class HotkeyText
{
    /// <param name="key">Tuşun adı ("Space", "K", "F1"); uygulama bunu kendi tuş türüne çevirir.</param>
    /// <returns>En az bir değiştirici ve bir tuş varsa true.</returns>
    public static bool TryParse(string? text, out HotkeyModifiers modifiers, out string key)
    {
        modifiers = HotkeyModifiers.None;
        key = "";
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var modifier = part.ToLowerInvariant() switch
            {
                "alt" => HotkeyModifiers.Alt,
                "ctrl" or "control" => HotkeyModifiers.Ctrl,
                "shift" => HotkeyModifiers.Shift,
                "win" or "windows" => HotkeyModifiers.Win,
                _ => HotkeyModifiers.None,
            };

            if (modifier != HotkeyModifiers.None)
            {
                modifiers |= modifier;
            }
            else if (key.Length == 0)
            {
                key = part;
            }
            else
            {
                return false;
            }
        }

        return modifiers != HotkeyModifiers.None && key.Length > 0;
    }

    public static string Format(HotkeyModifiers modifiers, string key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(key);
        return string.Join('+', parts);
    }
}
