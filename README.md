# Stardew Player Switcher

A SMAPI mod for Stardew Valley co-op that lets you choose which farmer should host a save right after clicking a host save slot.

## What It Does

The in-game flow is:

1. Open `Co-op`.
2. Click `Host`.
3. Click a save slot.
4. The mod opens a `Choose Host` menu instead of starting immediately.
5. Pick the farmer who should become the host for this launch.
6. The mod swaps the save owner if needed, then starts the game right away.

This is designed for saves where the current `<player>` farmer is not the person you want to host with for the next session.

## Design

The mod does not try to change host identity after the save is already loaded. Instead, it edits the save on disk before the game starts.

The core design choices are:

- Patch `CoopMenu.HostFileSlot.Activate()` with Harmony.
- Intercept the exact point where the vanilla co-op host menu would normally start loading a save.
- Show a lightweight `Choose Host` menu inside the same co-op flow.
- If the selected farmer is not already the current host, swap:
  - `SaveGame/player`
  - the matching `SaveGame/farmhands/Farmer`
- Update `SaveGameInfo` when present.
- Create a timestamped backup before writing anything.
- Resume the vanilla load flow immediately after the swap.

## Why This Approach

Changing the active host after the world has already loaded is much more invasive and error-prone. Stardew Valley expects the save's primary player to already be established before load.

By moving the switch to the `Co-op -> Host -> save slot` step, the mod stays aligned with the vanilla game flow:

- the user chooses a save in the same place as vanilla;
- the mod only inserts one extra decision screen;
- the final world load still uses the game's own save-loading path.

This keeps the implementation smaller, easier to reason about, and safer to recover from through backups.

## Project Structure

- [ModEntry.cs](./ModEntry.cs)
  Entry point, Harmony patch setup, and host-slot interception.
- [Services/SaveSwapService.cs](./Services/SaveSwapService.cs)
  Save parsing, backup creation, XML swap logic, and `SaveGameInfo` updates.
- [UI/CoopHostPickerMenu.cs](./UI/CoopHostPickerMenu.cs)
  The in-game host picker shown after clicking a host save slot.
- [Models/](./Models)
  Small data models for save summaries and host candidates.

## Save Format Assumptions

The mod currently assumes the standard Stardew save layout:

- the current host is stored in `SaveGame/player`;
- other playable farmers are stored in `SaveGame/farmhands/Farmer`.

That is enough for many normal co-op saves, but there are still edge cases where other related ownership data may matter.

## Limitations

- This is a disk-level save edit, not a temporary runtime switch.
- If you want to switch back later, you need to run the flow again.
- The mod currently swaps the main farmer records, but it does not attempt a full rewrite of every ownership-related field in the save.
- The mod has only been validated against the current implementation path for `Co-op -> Host`.

## Safety

- A backup is created before every swap.
- Backups are timestamped.
- If you pick the current host, the save is loaded without rewriting it.

## Build

This project uses `Pathoschild.Stardew.ModBuildConfig`, following the standard SMAPI packaging approach.

Typical build command:

```powershell
dotnet build
```

The current project expects a local Stardew Valley + SMAPI installation so the SMAPI/game references can resolve during build.

## Notes

- The repository intentionally ignores `examples/` so local sample saves are not pushed publicly by default.
- The in-repo version is currently focused on the co-op host flow, not the title screen prototype used earlier during development.
