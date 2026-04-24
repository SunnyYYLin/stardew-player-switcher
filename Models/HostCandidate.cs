namespace StardewPlayerSwitcher.Models;

internal sealed record HostCandidate(long UniqueMultiplayerId, string Name, string HomeLocation, bool IsCurrentHost);
