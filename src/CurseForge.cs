using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using static mmods.Configuration;

namespace CurseForge;

public class CurseForgeClient
{
    const string BaseUrl = "https://api.curseforge.com";
    HttpClient HttpClient;

    public CurseForgeClient(DelegatingHandler handler)
    {
        var apiToken = GetCurseForgeToken();

        HttpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl),
            DefaultRequestHeaders =
            {
                { "x-api-key", apiToken },
                { "Accept", "application/json" }
            }
        };
    }

    public async Task<Uri[]> GetDownloadUris(ModFileDescription file)
    {
        try
        {
            var downloadUrl = await HttpClient.GetFromJsonAsync<StringResponse>($"/v1/mods/{file.ProjectID}/files/{file.FileID}/download-url")
                ?? throw new Exception("Failed to get download url.");
            var parsed = Uri.TryCreate(downloadUrl.Data, UriKind.Absolute, out var uri);
            if (!parsed || uri is null)
            {
                throw new Exception("Failed to parse download url");
            }

            return [uri];
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]ProjectID: {file.ProjectID} FileID:{file.FileID}. Retrying with fallback. \n{exception.Message}[/]");
        }

        try
        {
            var response = await HttpClient.GetFromJsonAsync<ModFileResponse>($"/v1/mods/{file.ProjectID}/files/{file.FileID}")
                ?? throw new Exception("Failed to get mod file.");
            var parsed = Uri.TryCreate(response.Data.DownloadUrl, UriKind.Absolute, out var uri);
            if (parsed && uri is not null)
            {
                return [uri];
            }

            AnsiConsole.MarkupLineInterpolated($"[red]ProjectID: {file.ProjectID} FileID:{file.FileID}. {Markup.Escape(response.Data.DisplayName)} has no download url. Trying to generate one.[/]");

            return TryGeneratingDownloadUri(response.Data);
        }
        catch (Exception exception)
        {
            throw new Exception($"ProjectID: {file.ProjectID} FileID:{file.FileID}. There is nothing we can do. Bail out.", exception);
        }
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
    private Uri[] TryGeneratingDownloadUri(ModFile data)
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