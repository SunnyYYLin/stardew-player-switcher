using System;
using System.Collections.Generic;

namespace StardewPlayerSwitcher.Models;

internal sealed class SaveSlotSummary
{
    public string SaveDirectoryPath { get; init; } = string.Empty;

    public string SaveFilePath { get; init; } = string.Empty;

    public string SaveFolderName { get; init; } = string.Empty;

    public string FarmName { get; init; } = string.Empty;

    public string CurrentHostName { get; init; } = string.Empty;

    public long CurrentHostId { get; init; }

    public IReadOnlyList<HostCandidate> Candidates { get; init; } = Array.Empty<HostCandidate>();

    public DateTime LastWriteTimeUtc { get; init; }
}
