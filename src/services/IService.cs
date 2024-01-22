using mmods.Models;

namespace mmods.Services;

public interface IService
{
    Task<Modpack> GetModpack(Stream stream);
    Task<(ModpackFile[] files, int overridesCount)> GetFiles(Modpack modpack);
    Task<Dictionary<FileType, HashSet<string>>> ApplyOverrides(Modpack modpack, string outputPath);

    Task<FileType> GetFileType(ModpackFile file);
    Task<Uri[]> GetDownloadUris(ModpackFile file);
}