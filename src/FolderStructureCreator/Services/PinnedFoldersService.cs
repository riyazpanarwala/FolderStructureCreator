using System.IO;
using System.Text.Json;
using FolderStructureCreator.Models;

namespace FolderStructureCreator.Services;

public static class PinnedFoldersService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FolderStructureCreator");

    private static readonly string PinnedFoldersFilePath = Path.Combine(AppDataFolder, "pinned_folders.json");

    private class PinnedFolderDto
    {
        public string Path { get; set; } = string.Empty;
        public string? Name { get; set; }
    }

    public static List<PinnedFolder> LoadPinnedFolders()
    {
        var result = new List<PinnedFolder>();

        try
        {
            if (!File.Exists(PinnedFoldersFilePath))
                return result;

            var json = File.ReadAllText(PinnedFoldersFilePath);
            var dtos = JsonSerializer.Deserialize<List<PinnedFolderDto>>(json);
            if (dtos != null)
            {
                foreach (var dto in dtos)
                {
                    if (!string.IsNullOrWhiteSpace(dto.Path))
                        result.Add(new PinnedFolder(dto.Path, dto.Name));
                }
            }
        }
        catch
        {
            // Ignore corrupted json file or read errors gracefully
        }

        return result;
    }

    public static void SavePinnedFolders(IEnumerable<PinnedFolder> folders)
    {
        try
        {
            if (!Directory.Exists(AppDataFolder))
                Directory.CreateDirectory(AppDataFolder);

            var dtos = folders.Select(f => new PinnedFolderDto
            {
                Path = f.Path,
                Name = f.Name
            }).ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(dtos, options);
            File.WriteAllText(PinnedFoldersFilePath, json);
        }
        catch
        {
            // Ignore write failures gracefully
        }
    }
}
