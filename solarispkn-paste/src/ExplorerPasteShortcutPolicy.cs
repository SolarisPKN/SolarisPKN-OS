namespace SolarisPKN.Paste;

/// <summary>
/// Pure, conservative rules for optional Ctrl+V replacement in a real
/// File Explorer folder window. Non-file clipboard data, injected shortcuts,
/// other apps, and editable fields always use ordinary Windows Ctrl+V.
/// Does not record or store keystrokes.
/// </summary>
internal static class ExplorerPasteShortcutPolicy
{
    internal static bool IsExplorerFolderClass(string? name) =>
        string.Equals(name,"CabinetWClass",StringComparison.Ordinal) ||
        string.Equals(name,"ExploreWClass",StringComparison.Ordinal);

    internal static bool IsTextEditingClass(string? name)
    {
        if(string.IsNullOrEmpty(name))return false;
        return name.Equals("Edit",StringComparison.OrdinalIgnoreCase) ||
            name.Equals("ComboBox",StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RichEdit",StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TextBox",StringComparison.OrdinalIgnoreCase) ||
            name.Contains("InputSite",StringComparison.OrdinalIgnoreCase) ||
            name.Contains("SearchBox",StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldIntercept(bool enabled,bool explorerFolder,
        bool clipboardHasFileDrop,bool editing,bool ctrl,bool alt,
        bool shift,bool win,bool injected) =>
        enabled && explorerFolder && clipboardHasFileDrop && !editing &&
        ctrl && !alt && !shift && !win && !injected;

    internal static bool IsReservedCtrlV(string shortcut) =>
        string.Equals(shortcut?.Trim(),"Ctrl+V",StringComparison.OrdinalIgnoreCase);
}
