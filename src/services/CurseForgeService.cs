using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using mmods.Models;
using mmods.Services;
using Spectre.Console;
using static mmods.Configuration;
using static mmods.Utils;

namespace CurseForge;

public record CurseForgeModpack(
    string Name,
    string? Author,
    string? Version,
    string? Description,
    string[]? Dependencies,
    ZipArchive Archive,
    Manifest Manifest
) : Modpack("CurseForge", Name, Author, Version, Description, Dependencies)
{
    public new void Dispose()
    {
        base.Dispose();
        Archive.Dispose();
    }
}

public record CurseForgeModpackFile(
    string Name,
    ModFileDescription Description
) : ModpackFile(Name);

public class CurseForgeService : IService
{
    CurseForgeClient Client = new();

    async Task<Modpack> IService.GetModpack(Stream stream) => await GetModpack(stream);
    async private Task<CurseForgeModpack> GetModpack(Stream stream)
    {
        var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var manifestEntry = archive.Entries.First(x => x.FullName == "manifest.json");
        using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(manifestStream);

        if (manifest is null || manifest.ManifestType != "minecraftModpack")
        {
            throw new Exception("Not a curseforge modpack.");
        }

        return new CurseForgeModpack(
            manifest.Name,
            manifest.Author,
            manifest.Version,
            null,
            [
                $"minecraft {manifest.Minecraft.Version}",
                ..manifest.Minecraft.ModLoaders.Select(x => x.Id)
            ],
            archive,
            manifest
        );
    }

    private ZipArchiveEntry[] GetOverrides(CurseForgeModpack modpack) => modpack.Archive.Entries
            .Where(x => x.FullName.StartsWith(modpack.Manifest.Overrides))
            .ToArray();

    async Task<(ModpackFile[] files, int overridesCount)> IService.GetFiles(Modpack modpack) => await GetFiles((CurseForgeModpack)modpack);
    async private Task<(CurseForgeModpackFile[] files, int overridesCount)> GetFiles(CurseForgeModpack modpack)
    {
        var manifest = modpack.Manifest;

        var files = manifest.Files
            .Where(x => x.Required)
            .Select(x => new CurseForgeModpackFile($"ProjectID:{x.ProjectID}, FileID:{x.FileID}", x))
            .ToArray();

        var overridesCount = GetOverrides(modpack).Count();

        return (files, overridesCount);
    }

    async Task<Dictionary<FileType, HashSet<string>>> IService.ApplyOverrides(Modpack modpack, string outputPath) => await ApplyOverrides((CurseForgeModpack)modpack, outputPath);
    async private Task<Dictionary<FileType, HashSet<string>>> ApplyOverrides(CurseForgeModpack modpack, string outputPath)
    {
        var manifest = modpack.Manifest;
        var overrides = GetOverrides(modpack);
        var summary = new Dictionary<FileType, HashSet<string>>();

        foreach (var entry in overrides)
        {
            var relativePath = entry.FullName.Replace($"{manifest.Overrides}/", "");
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

    async Task<FileType> IService.GetFileType(ModpackFile file) => await GetFileType((CurseForgeModpackFile)file);
    public async Task<FileType> GetFileType(CurseForgeModpackFile file)
    {
        try
        {
            var mod = await Client.GetMod(file.Description);

            return mod.ClassId switch
            {
                12 or 4559 or 4546 => FileType.ResourcePack,
                6552 => FileType.ShaderPack,
                6 => FileType.Mod,
                _ => throw new Exception($"I dont know what this is. ClassId:{mod.ClassId}")
            };
        }
        catch (Exception exception)
        {
            AnsiConsole.WriteException(exception);
            AnsiConsole.MarkupLineInterpolated($"[red]{file.Name}. Couldn't figure out what this is. Let's assume that this is a mod.[/]");

            return FileType.Mod;
        }
    }

    async Task<Uri[]> IService.GetDownloadUris(ModpackFile file) => await GetDownloadUris((CurseForgeModpackFile)file);
    public async Task<Uri[]> GetDownloadUris(CurseForgeModpackFile file)
    {
        try
        {
            var downloadUrl = await Client.GetModFileDownloadUrl(file.Description);
            var parsed = Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri);
            if (!parsed || uri is null)
            {
                throw new Exception("Failed to parse download url");
            }

            return [uri];
        }
        catch (Exception exception)
        {
            AnsiConsole.WriteException(exception);
            AnsiConsole.MarkupLineInterpolated($"[red]{file.Name}. Retrying with fallback.[/]");
        }

        try
        {
            var modFile = await Client.GetModFile(file.Description);
            var parsed = Uri.TryCreate(modFile.DownloadUrl, UriKind.Absolute, out var uri);
            if (parsed && uri is not null)
            {
                return [uri];
            }

            AnsiConsole.MarkupLineInterpolated($"[red]{file.Name}. {Markup.Escape(modFile.DisplayName)} has no download url. Trying to generate one.[/]");

            return Client.TryGeneratingDownloadUri(modFile);
        }
        catch (Exception exception)
        {
            throw new Exception($"{file.Name}. There is nothing we can do. Bail out.", exception);
        }
    }
}

public class CurseForgeClient
{
    const string BaseUrl = "https://api.curseforge.com";
    HttpClient HttpClient;

    public CurseForgeClient()
    {
        var apiToken = GetCurseForgeToken();

        HttpClient = new HttpClient(Limiter)
        {
            BaseAddress = new Uri(BaseUrl),
            DefaultRequestHeaders =
            {
                { "x-api-key", apiToken },
                { "Accept", "application/json" }
            }
        };
    }

    public async Task<Mod> GetMod(ModFileDescription file)
    {
        var response = await HttpClient.GetFromJsonAsync<ModResponse>($"/v1/mods/{file.ProjectID}")
            ?? throw new Exception("Failed to get mod info.");

        return response.Data;
    }

    public async Task<ModFile> GetModFile(ModFileDescription file)
    {
        var response = await HttpClient.GetFromJsonAsync<ModFileResponse>($"/v1/mods/{file.ProjectID}/files/{file.FileID}")
            ?? throw new Exception("Failed to get mod file.");

        return response.Data;
    }

    public async Task<string> GetModFileDownloadUrl(ModFileDescription file)
    {
        var response = await HttpClient.GetFromJsonAsync<StringResponse>($"/v1/mods/{file.ProjectID}/files/{file.FileID}/download-url")
                ?? throw new Exception("Failed to get download url.");

        return response.Data;
    }

    /*
        Examples:
        https://mediafilez.forgecdn.net/files/4593/548/jei-1.18.2-forge-10.2.1.1005.jar
        https://mediafilez.forgecdn.net/files/4849/63/expanded_ecosphere-3.2.3-forge.jar
        https://mediafilez.forgecdn.net/files/4765/566/evenmoreinstruments-1.20.2-2.1.jar
        https://mediafilez.forgecdn.net/files/4765/565/evenmoreinstruments-1.20+1.20.1-2.1.jar
        https://mediafilez.forgecdn.net/files/4811/98/Butchersdelight+beta+1.20.1+2.0.8f.jar
        https://edge.forgecdn.net/files/4397/900/WDA-NoFlyingStructures-1.18.2-1.19.2.zip
    */
    public Uri[] TryGeneratingDownloadUri(ModFile data)
    {
        var id = data.Id.ToString();

        string[] idPrefixTransforms = [
            id.Substring(0, 4)
        ];

        string[] idSuffixTransforms = [
            id.Substring(4),
            id.Substring(4).TrimStart('0')
        ];

        string[] baseFileNameTransforms = [
            data.FileName,
            data.FileName.Replace(' ', '+'),
        ];

        string[] fileNameTransforms = [
            ..baseFileNameTransforms,
            ..baseFileNameTransforms.Select(x => Uri.EscapeDataString(x))
        ];

        var mediafilez = idPrefixTransforms.SelectMany(idPrefixTransform =>
            idSuffixTransforms.SelectMany(idSuffixTransform =>
                fileNameTransforms.Select(fileNameTransform =>
                    $"https://mediafilez.forgecdn.net/files/{idPrefixTransform}/{idSuffixTransform}/{fileNameTransform}"
                )
            )
        );

        var edge = idPrefixTransforms.SelectMany(idPrefixTransform =>
            idSuffixTransforms.SelectMany(idSuffixTransform =>
                fileNameTransforms.Select(fileNameTransform =>
                    $"https://edge.forgecdn.net/files/{idPrefixTransform}/{idSuffixTransform}/{fileNameTransform}"
                )
            )
        );

        string[] variants = [
            ..edge,
            ..mediafilez
        ];

        return variants
            .Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) ? uri : null)
            .Where(x => x is not null)
            .Cast<Uri>()
            .ToArray();
    }
}

public record Manifest(
    [property: JsonPropertyName("minecraft")] Minecraft Minecraft,
    [property: JsonPropertyName("manifestType")] string ManifestType,
    [property: JsonPropertyName("manifestVersion")] long ManifestVersion,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("files")] IReadOnlyList<ModFileDescription> Files,
    [property: JsonPropertyName("overrides")] string Overrides
);

public record StringResponse(
    [property: JsonPropertyName("data")] string Data
);

public record ModFileResponse(
    [property: JsonPropertyName("data")] ModFile Data
);

public record ModFileDescription(
   [property: JsonPropertyName("projectID")] long ProjectID,
   [property: JsonPropertyName("fileID")] long FileID,
   [property: JsonPropertyName("required")] bool Required
);

public record Minecraft(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("modLoaders")] IReadOnlyList<ModLoader> ModLoaders
);

public record ModLoader(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("primary")] bool Primary
);

public record ModFile(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("gameId")] long GameId,
    [property: JsonPropertyName("modId")] long ModId,
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("releaseType")] long ReleaseType,
    [property: JsonPropertyName("fileStatus")] long FileStatus,
    [property: JsonPropertyName("hashes")] IReadOnlyList<Hash> Hashes,
    [property: JsonPropertyName("fileDate")] DateTime FileDate,
    [property: JsonPropertyName("fileLength")] long FileLength,
    [property: JsonPropertyName("downloadCount")] long DownloadCount,
    [property: JsonPropertyName("downloadUrl")] string? DownloadUrl,
    [property: JsonPropertyName("gameVersions")] IReadOnlyList<string> GameVersions,
    [property: JsonPropertyName("sortableGameVersions")] IReadOnlyList<SortableGameVersion> SortableGameVersions,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<Dependency> Dependencies,
    [property: JsonPropertyName("alternateFileId")] long AlternateFileId,
    [property: JsonPropertyName("isServerPack")] bool IsServerPack,
    [property: JsonPropertyName("fileFingerprint")] long FileFingerprint,
    [property: JsonPropertyName("modules")] IReadOnlyList<Module> Modules
);

public record Dependency(
    [property: JsonPropertyName("modId")] long ModId,
    [property: JsonPropertyName("relationType")] long RelationType
);

public record Hash(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("algo")] long Algo
);

public record Module(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fingerprint")] long Fingerprint
);

public record SortableGameVersion(
    [property: JsonPropertyName("gameVersionName")] string GameVersionName,
    [property: JsonPropertyName("gameVersionPadded")] string GameVersionPadded,
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("gameVersionReleaseDate")] DateTime GameVersionReleaseDate,
    [property: JsonPropertyName("gameVersionTypeId")] long GameVersionTypeId
);

public record Author(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string Url
);

public record Category(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("gameId")] int GameId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("iconUrl")] string IconUrl,
    [property: JsonPropertyName("dateModified")] DateTime DateModified,
    [property: JsonPropertyName("isClass")] bool IsClass,
    [property: JsonPropertyName("classId")] int ClassId,
    [property: JsonPropertyName("parentCategoryId")] int ParentCategoryId
);

public record LatestFile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("gameId")] int GameId,
    [property: JsonPropertyName("modId")] int ModId,
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("releaseType")] int ReleaseType,
    [property: JsonPropertyName("fileStatus")] int FileStatus,
    [property: JsonPropertyName("hashes")] IReadOnlyList<Hash> Hashes,
    [property: JsonPropertyName("fileDate")] DateTime FileDate,
    [property: JsonPropertyName("fileLength")] int FileLength,
    [property: JsonPropertyName("downloadCount")] int DownloadCount,
    [property: JsonPropertyName("downloadUrl")] string DownloadUrl,
    [property: JsonPropertyName("gameVersions")] IReadOnlyList<string> GameVersions,
    [property: JsonPropertyName("sortableGameVersions")] IReadOnlyList<SortableGameVersion> SortableGameVersions,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<object> Dependencies,
    [property: JsonPropertyName("alternateFileId")] int AlternateFileId,
    [property: JsonPropertyName("isServerPack")] bool IsServerPack,
    [property: JsonPropertyName("fileFingerprint")] long FileFingerprint,
    [property: JsonPropertyName("modules")] IReadOnlyList<Module> Modules
);

public record LatestFilesIndex(
    [property: JsonPropertyName("gameVersion")] string GameVersion,
    [property: JsonPropertyName("fileId")] int FileId,
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("releaseType")] int ReleaseType,
    [property: JsonPropertyName("gameVersionTypeId")] int GameVersionTypeId
);

public record Links(
    [property: JsonPropertyName("websiteUrl")] string WebsiteUrl,
    [property: JsonPropertyName("wikiUrl")] string WikiUrl,
    [property: JsonPropertyName("issuesUrl")] object IssuesUrl,
    [property: JsonPropertyName("sourceUrl")] object SourceUrl
);

public record Logo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("modId")] int ModId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("thumbnailUrl")] string ThumbnailUrl,
    [property: JsonPropertyName("url")] string Url
);

public record Mod(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("gameId")] int GameId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("links")] Links Links,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("downloadCount")] int DownloadCount,
    [property: JsonPropertyName("isFeatured")] bool IsFeatured,
    [property: JsonPropertyName("primaryCategoryId")] int PrimaryCategoryId,
    [property: JsonPropertyName("categories")] IReadOnlyList<Category> Categories,
    [property: JsonPropertyName("classId")] int ClassId,
    [property: JsonPropertyName("authors")] IReadOnlyList<Author> Authors,
    [property: JsonPropertyName("logo")] Logo Logo,
    [property: JsonPropertyName("screenshots")] IReadOnlyList<object> Screenshots,
    [property: JsonPropertyName("mainFileId")] int MainFileId,
    [property: JsonPropertyName("latestFiles")] IReadOnlyList<LatestFile> LatestFiles,
    [property: JsonPropertyName("latestFilesIndexes")] IReadOnlyList<LatestFilesIndex> LatestFilesIndexes,
    [property: JsonPropertyName("latestEarlyAccessFilesIndexes")] IReadOnlyList<object> LatestEarlyAccessFilesIndexes,
    [property: JsonPropertyName("dateCreated")] DateTime DateCreated,
    [property: JsonPropertyName("dateModified")] DateTime DateModified,
    [property: JsonPropertyName("dateReleased")] DateTime DateReleased,
    [property: JsonPropertyName("allowModDistribution")] bool AllowModDistribution,
    [property: JsonPropertyName("gamePopularityRank")] int GamePopularityRank,
    [property: JsonPropertyName("isAvailable")] bool IsAvailable,
    [property: JsonPropertyName("thumbsUpCount")] int ThumbsUpCount
);

public record ModResponse(
   [property: JsonPropertyName("data")] Mod Data
);