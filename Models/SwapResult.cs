namespace StardewPlayerSwitcher.Models;

internal sealed record SwapResult(string BackupDirectoryPath, string PreviousHostName, string NewHostName, bool SaveGameInfoUpdated);
