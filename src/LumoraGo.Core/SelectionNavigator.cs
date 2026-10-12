namespace LumoraGo.Core;

/// <summary>Sonuç listesinde ↑/↓ gezinmesi. Uçlarda başa/sona sarar.</summary>
public static class SelectionNavigator
{
    /// <returns>Yeni seçili indeks; liste boşsa -1.</returns>
    public static int Move(int current, int delta, int count)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count)
        {
            return delta >= 0 ? 0 : count - 1;
        }

        var next = (current + delta) % count;
        return next < 0 ? next + count : next;
    }
}
