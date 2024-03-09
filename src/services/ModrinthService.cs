using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using mmods.Models;
using mmods.Services;
using Spectre.Console;
using static mmods.Utils;

namespace Modrinth;

public record ModrinthModpack(
    string Name,
    string? Author,
    string? Version,
    string? Description,
    string[]? Dependencies,
    ZipArchive Archive,
    ModrinthManifest Manifest
) : Modpack("Modrinth", Name, Author, Version, Description, Dependencies)
{
    public new void Dispose()
    {
        base.Dispose();
        Archive.Dispose();
    }
}

public record ModrinthModpackFile(
    string Name,
    ModrinthFile Description
) : ModpackFile(Name);

public class ModrinthService : IService
{
    async Task<Modpack> IService.GetModpack(Stream stream) => await GetModpack(stream);
    async private Task<ModrinthModpack> GetModpack(Stream stream)
    {
        var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifestEntry = archive.Entries.First(x => x.FullName == "modrinth.index.json");
        using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<ModrinthManifest>(manifestStream);

        if (manifest is null)
        {
            throw new Exception("Not a modrinth modpack.");
        }

        return new ModrinthModpack(
            manifest.Name,
            "",
            manifest.VersionId,
            manifest.Summary,
            manifest.Dependencies.Select(x => $"{x.Key} {x.Value}").ToArray(),
            archive,
            manifest
        );
    }

    private ZipArchiveEntry[] GetOverrides(ModrinthModpack modpack) => modpack.Archive.Entries
            .Where(x => x.FullName.StartsWith("overrides"))
            .ToArray();

    async Task<(ModpackFile[] files, int overridesCount)> IService.GetFiles(Modpack modpack) => await GetFiles((ModrinthModpack)modpack);
    async public Task<(ModrinthModpackFile[] files, int overridesCount)> GetFiles(ModrinthModpack modpack)
    {
        var manifest = modpack.Manifest;

        var files = manifest.Files
            .Select(x => new ModrinthModpackFile(x.Path, x))
            .ToArray();

        var overridesCount = GetOverrides(modpack).Count();

        return (files, overridesCount);
    }

    async Task<FileType> IService.GetFileType(ModpackFile file) => await GetFileType((ModrinthModpackFile)file);
    async public Task<FileType> GetFileType(ModrinthModpackFile file)
    {
        return file.Description.Path switch
        {
            var x when x.StartsWith("mods") => FileType.Mod,
            var x when x.StartsWith("shaderpacks") => FileType.ShaderPack,
            var x when x.StartsWith("resourcepacks") => FileType.ResourcePack,
            _ => FileType.Unknown
        };
    }

    async Task<Dictionary<FileType, HashSet<string>>> IService.ApplyOverrides(Modpack modpack, string outputPath) => await ApplyOverrides((ModrinthModpack)modpack, outputPath);
    async public Task<Dictionary<FileType, HashSet<string>>> ApplyOverrides(ModrinthModpack modpack, string outputPath)
    {
        var overrides = GetOverrides(modpack);
        var summary = new Dictionary<FileType, HashSet<string>>();

        foreach (var entry in overrides)
        {
            var relativePath = entry.FullName.Replace($"overrides/", "");
            var filePath = Path.Combine(outputPath, relativePath);
            var fileName = Path.GetFileName(filePath);

            if (fileName is null or "")
            {
                AnsiConsole.MarkupLineInterpolated($"Failed to get file name for {Markup.Escape(entry.FullName)}");
                continue;
            }

            var fileType = relativePath switch
            {
                var x when x.StartsWith("mods") => FileType.Mod,
                var x when x.StartsWith("resourcepacks") => FileType.ResourcePack,
                var x when x.StartsWith("shaderpacks") => FileType.ShaderPack,
                _ => FileType.Unknown
            };

            if (fileType is not FileType.Unknown)
            {
                var set = summary.GetValueOrDefault(fileType!, new());
                set.Add(fileName);

                summary[fileType] = set;
            }

            var directoryPath = Path.GetDirectoryName(filePath);
            if (directoryPath is null or "")
            {
                AnsiConsole.MarkupLineInterpolated($"Failed to get directory path for {Markup.Escape(relativePath)}");
                continue;
            }

            EnsureDirectoryExists(directoryPath);

            entry.ExtractToFile(filePath, true);

            AnsiConsole.MarkupLineInterpolated($"[green]Extracted[/] {Markup.Escape(entry.FullName)}");
        }

        return summary;
    }

    async Task<Uri[]> IService.GetDownloadUris(ModpackFile file) => await GetDownloadUris((ModrinthModpackFile)file);
    async public Task<Uri[]> GetDownloadUris(ModrinthModpackFile file)
    {
        return file.Description.Downloads.Select(x => new Uri(x)).ToArray();
    }
}

public record ModrinthEnv(
    [property: JsonPropertyName("client")] string Client,
    [property: JsonPropertyName("server")] string Server
);

public record ModrinthFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("hashes")] ModrinthHashes Hashes,
    [property: JsonPropertyName("env")] ModrinthEnv Env,
    [property: JsonPropertyName("downloads")] IReadOnlyList<string> Downloads,
    [property: JsonPropertyName("fileSize")] int FileSize
);

public record ModrinthHashes(
    [property: JsonPropertyName("sha1")] string Sha1,
    [property: JsonPropertyName("sha512")] string Sha512
);

public record ModrinthManifest(
    [property: JsonPropertyName("game")] string Game,
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("versionId")] string VersionId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("files")] IReadOnlyList<ModrinthFile> Files,
    [property: JsonPropertyName("dependencies")] Dictionary<string, string> Dependencies
);
