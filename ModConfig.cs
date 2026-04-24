using StardewModdingAPI;

namespace StardewPlayerSwitcher;

internal sealed class ModConfig
{
    public SButton OpenMenuKey { get; set; } = SButton.F8;

    public bool DrawTitleButton { get; set; } = true;
}
