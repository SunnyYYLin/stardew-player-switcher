using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewPlayerSwitcher.Models;
using StardewPlayerSwitcher.Services;
using StardewPlayerSwitcher.UI;
using StardewValley;
using StardewValley.Menus;
using StardewValley.SaveSerialization;

namespace StardewPlayerSwitcher;

public sealed class ModEntry : Mod
{
    private static ModEntry? Instance;

    private readonly Harmony harmony = new("sunnylin.StardewPlayerSwitcher");

    private SaveSwapService saveSwapService = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;

        string backupRootPath = Path.Combine(this.Helper.DirectoryPath, "backups");
        this.saveSwapService = new SaveSwapService(this.Monitor, Constants.SavesPath, backupRootPath);

        this.PatchHostFileSlotActivation();

        helper.ConsoleCommands.Add(
            "sps_choose_host",
            "Open the host picker for the currently visible co-op host menu save.",
            this.OnChooseHostCommand);
    }

    private static bool OnHostFileSlotActivatePrefix(object __instance)
    {
        return Instance?.InterceptHostFileSlotActivation(__instance) ?? true;
    }

    private bool InterceptHostFileSlotActivation(object slotInstance)
    {
        try
        {
            if (Game1.activeClickableMenu is not TitleMenu && Game1.activeClickableMenu is not CoopMenu)
                return true;

            CoopMenu? coopMenu = AccessTools.Field(slotInstance.GetType(), "menu")?.GetValue(slotInstance) as CoopMenu;
            Farmer? farmer = AccessTools.Field(slotInstance.GetType(), "Farmer")?.GetValue(slotInstance) as Farmer;
            bool isMultiplayer = (bool?)AccessTools.Field(slotInstance.GetType(), "_multiplayer")?.GetValue(slotInstance) ?? true;
            if (coopMenu is null || farmer is null || string.IsNullOrWhiteSpace(farmer.slotName))
                return true;

            string saveDirectoryPath = Path.Combine(Constants.SavesPath, farmer.slotName);
            SaveSlotSummary summary = this.saveSwapService.LoadSaveSlot(saveDirectoryPath);

            this.ShowMenu(new CoopHostPickerMenu(
                summary,
                this.saveSwapService,
                candidate => this.StartSelectedSave(farmer.slotName, isMultiplayer, summary, candidate),
                () => this.ShowMenu(coopMenu)));

            return false;
        }
        catch (Exception ex)
        {
            this.Monitor.Log($"Failed to open host picker, falling back to the game's default host flow.\n{ex}", LogLevel.Error);
            return true;
        }
    }

    private void StartSelectedSave(string slotName, bool isMultiplayer, SaveSlotSummary summary, HostCandidate candidate)
    {
        if (!candidate.IsCurrentHost)
        {
            this.saveSwapService.SwapHost(summary.SaveDirectoryPath, candidate.UniqueMultiplayerId);
        }

        Game1.multiplayerMode = (byte)(isMultiplayer ? 2 : 0);
        SaveGame.Load(slotName);
        Game1.exitActiveMenu();
    }

    private void ShowMenu(IClickableMenu menu)
    {
        if (Game1.activeClickableMenu is TitleMenu)
        {
            TitleMenu.subMenu = menu;
            return;
        }

        Game1.activeClickableMenu = menu;
    }

    private void PatchHostFileSlotActivation()
    {
        Type? hostFileSlotType = AccessTools.TypeByName("StardewValley.Menus.CoopMenu+HostFileSlot");
        MethodInfo? activateMethod = hostFileSlotType is null ? null : AccessTools.Method(hostFileSlotType, "Activate");
        MethodInfo? prefixMethod = AccessTools.Method(typeof(ModEntry), nameof(OnHostFileSlotActivatePrefix));

        if (activateMethod is null || prefixMethod is null)
            throw new InvalidOperationException("Couldn't find CoopMenu.HostFileSlot.Activate to patch.");

        this.harmony.Patch(activateMethod, prefix: new HarmonyMethod(prefixMethod));
    }

    private void OnChooseHostCommand(string command, string[] args)
    {
        if (TitleMenu.subMenu is not CoopMenu coopMenu)
        {
            this.Monitor.Log("Open Co-op -> Host first, then use this command if you need to test the picker manually.", LogLevel.Info);
            return;
        }

        object? slot = coopMenu.MenuSlots.FirstOrDefault(slot => slot.GetType().FullName == "StardewValley.Menus.CoopMenu+HostFileSlot");
        if (slot is null)
        {
            this.Monitor.Log("No host save slot is currently available in the co-op menu.", LogLevel.Info);
            return;
        }

        this.InterceptHostFileSlotActivation(slot);
    }
}
