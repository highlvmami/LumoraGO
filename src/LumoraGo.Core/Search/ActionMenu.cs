using LumoraGo.Plugin;

namespace LumoraGo.Core.Search;

/// <summary>Seçili sonucun ek eylemlerini (sağ ok menüsü) sonuç satırlarına çevirir.</summary>
public static class ActionMenu
{
    public static bool HasActions(Result? result) => result?.ContextMenu is { Count: > 0 };

    /// <summary>Her eylem bir satır olur; alt yazıda hangi sonuç için olduğu görünür. Sıra korunur.</summary>
    public static IReadOnlyList<Result> ToResults(Result parent)
    {
        var actions = parent.ContextMenu ?? [];
        return actions
            .Select((action, i) => new Result(action.Title, parent.Title, parent.IconPath, actions.Count - i, action.Action)
            {
                PluginId = parent.PluginId,
            })
            .ToList();
    }
}
