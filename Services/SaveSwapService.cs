using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using StardewModdingAPI;
using StardewPlayerSwitcher.Models;

namespace StardewPlayerSwitcher.Services;

internal sealed class SaveSwapService
{
    private readonly IMonitor monitor;
    private readonly string savesRootPath;
    private readonly string backupRootPath;

    public SaveSwapService(IMonitor monitor, string savesRootPath, string backupRootPath)
    {
        this.monitor = monitor;
        this.savesRootPath = savesRootPath;
        this.backupRootPath = backupRootPath;
    }

    public IReadOnlyList<SaveSlotSummary> LoadAllSaveSlots()
    {
        List<SaveSlotSummary> slots = new();

        if (!Directory.Exists(this.savesRootPath))
            return slots;

        foreach (string directoryPath in Directory.EnumerateDirectories(this.savesRootPath))
        {
            try
            {
                slots.Add(this.LoadSaveSlot(directoryPath));
            }
            catch (Exception ex)
            {
                this.monitor.Log($"Skipping save '{directoryPath}': {ex.Message}", LogLevel.Warn);
            }
        }

        return slots
            .OrderByDescending(slot => slot.LastWriteTimeUtc)
            .ToList();
    }

    public SaveSlotSummary LoadSaveSlot(string saveDirectoryPath)
    {
        string saveFilePath = GetSaveFilePath(saveDirectoryPath);
        if (!File.Exists(saveFilePath))
            throw new FileNotFoundException("Expected a save file that matches the folder name.", saveFilePath);

        XDocument document = XDocument.Load(saveFilePath, LoadOptions.None);
        XElement root = document.Root ?? throw new InvalidDataException("Save XML has no root node.");
        XElement playerElement = root.Element("player") ?? throw new InvalidDataException("Save XML is missing the <player> node.");

        List<HostCandidate> candidates = new()
        {
            ReadCandidate(playerElement, true),
        };

        XElement? farmhandsElement = root.Element("farmhands");
        if (farmhandsElement is not null)
        {
            foreach (XElement farmhandElement in farmhandsElement.Elements("Farmer"))
            {
                long uniqueId = ReadLong(farmhandElement, "UniqueMultiplayerID");
                if (uniqueId == 0)
                    continue;

                candidates.Add(ReadCandidate(farmhandElement, false));
            }
        }

        string saveFolderName = Path.GetFileName(saveDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string farmName = ReadString(playerElement, "farmName");
        if (string.IsNullOrWhiteSpace(farmName))
            farmName = saveFolderName;

        HostCandidate currentHost = candidates[0];

        return new SaveSlotSummary
        {
            SaveDirectoryPath = saveDirectoryPath,
            SaveFilePath = saveFilePath,
            SaveFolderName = saveFolderName,
            FarmName = farmName,
            CurrentHostName = currentHost.Name,
            CurrentHostId = currentHost.UniqueMultiplayerId,
            Candidates = candidates,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(saveFilePath),
        };
    }

    public SwapResult SwapHost(string saveDirectoryPath, long newHostId)
    {
        SaveSlotSummary summary = this.LoadSaveSlot(saveDirectoryPath);
        if (summary.CurrentHostId == newHostId)
        {
            return new SwapResult(string.Empty, summary.CurrentHostName, summary.CurrentHostName, false);
        }

        string saveFilePath = summary.SaveFilePath;
        XDocument document = XDocument.Load(saveFilePath, LoadOptions.PreserveWhitespace);
        XElement root = document.Root ?? throw new InvalidDataException("Save XML has no root node.");
        XElement playerElement = root.Element("player") ?? throw new InvalidDataException("Save XML is missing the <player> node.");
        XElement farmhandsElement = root.Element("farmhands") ?? throw new InvalidDataException("Save XML is missing the <farmhands> node.");
        XElement targetFarmhandElement = farmhandsElement
            .Elements("Farmer")
            .FirstOrDefault(element => ReadLong(element, "UniqueMultiplayerID") == newHostId)
            ?? throw new InvalidOperationException("The selected farmer is not stored as a farmhand in this save.");

        string previousHostName = ReadString(playerElement, "name", "Unknown Host");
        string newHostName = ReadString(targetFarmhandElement, "name", "Unknown Farmer");

        string backupDirectoryPath = this.CreateBackup(summary);

        XElement newPlayerElement = CloneFarmerElement(targetFarmhandElement, "player");
        XElement newFarmhandElement = CloneFarmerElement(playerElement, "Farmer");
        XElement saveGameInfoFarmer = CloneFarmerElement(targetFarmhandElement, "Farmer");

        playerElement.ReplaceWith(newPlayerElement);
        targetFarmhandElement.ReplaceWith(newFarmhandElement);
        document.Save(saveFilePath, SaveOptions.DisableFormatting);

        bool saveGameInfoUpdated = this.TryUpdateSaveGameInfo(summary.SaveDirectoryPath, root, saveGameInfoFarmer);

        return new SwapResult(backupDirectoryPath, previousHostName, newHostName, saveGameInfoUpdated);
    }

    private string CreateBackup(SaveSlotSummary summary)
    {
        string timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string backupDirectoryPath = Path.Combine(this.backupRootPath, summary.SaveFolderName, timestamp);
        Directory.CreateDirectory(backupDirectoryPath);

        File.Copy(summary.SaveFilePath, Path.Combine(backupDirectoryPath, summary.SaveFolderName), overwrite: true);

        string saveGameInfoPath = Path.Combine(summary.SaveDirectoryPath, "SaveGameInfo");
        if (File.Exists(saveGameInfoPath))
            File.Copy(saveGameInfoPath, Path.Combine(backupDirectoryPath, "SaveGameInfo"), overwrite: true);

        return backupDirectoryPath;
    }

    private bool TryUpdateSaveGameInfo(string saveDirectoryPath, XElement saveRoot, XElement farmerElement)
    {
        string saveGameInfoPath = Path.Combine(saveDirectoryPath, "SaveGameInfo");
        if (!File.Exists(saveGameInfoPath))
            return false;

        foreach (XAttribute attribute in saveRoot.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
            farmerElement.SetAttributeValue(attribute.Name, attribute.Value);

        XDocument saveGameInfoDocument = new(new XDeclaration("1.0", "utf-8", null), farmerElement);
        saveGameInfoDocument.Save(saveGameInfoPath, SaveOptions.DisableFormatting);
        return true;
    }

    private static HostCandidate ReadCandidate(XElement farmerElement, bool isCurrentHost)
    {
        return new HostCandidate(
            ReadLong(farmerElement, "UniqueMultiplayerID"),
            ReadString(farmerElement, "name", "Unnamed Farmer"),
            ReadString(farmerElement, "homeLocation", "Unknown Home"),
            isCurrentHost);
    }

    private static XElement CloneFarmerElement(XElement source, string elementName)
    {
        XElement clone = new(source);
        clone.Name = elementName;
        return clone;
    }

    private static string GetSaveFilePath(string saveDirectoryPath)
    {
        string folderName = Path.GetFileName(saveDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(saveDirectoryPath, folderName);
    }

    private static string ReadString(XElement parent, string elementName, string fallback = "")
    {
        string? value = parent.Element(elementName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
            return fallback;

        return value.Trim();
    }

    private static long ReadLong(XElement parent, string elementName)
    {
        string rawValue = parent.Element(elementName)?.Value ?? string.Empty;
        return long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
    }
}
