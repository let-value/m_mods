using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace CurseForge;

public record ModFile(
   [property: JsonPropertyName("projectID")] int ProjectID,
   [property: JsonPropertyName("fileID")] int FileID,
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

public record Manifest(
    [property: JsonPropertyName("minecraft")] Minecraft Minecraft,
    [property: JsonPropertyName("manifestType")] string ManifestType,
    [property: JsonPropertyName("manifestVersion")] int ManifestVersion,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("author")] string Author,
    [property: JsonPropertyName("files")] IReadOnlyList<ModFile> Files,
    [property: JsonPropertyName("overrides")] string Overrides
);

public record StringResponse(
    [property: JsonPropertyName("data")] string Data
);

public class CurseForgeClient
{
    const string BaseUrl = "https://api.curseforge.com";
    HttpClient HttpClient;

    public CurseForgeClient(string? apiToken, DelegatingHandler handler)
    {
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

    public async Task<Uri> GetDownloadUri(ModFile file)
    {
        var response = await HttpClient.GetFromJsonAsync<StringResponse>($"/v1/mods/{file.ProjectID}/files/{file.FileID}/download-url")
            ?? throw new Exception("Failed to get download url");

        var parsed = Uri.TryCreate(response.Data, UriKind.Absolute, out var uri);
        if (!parsed || uri is null)
        {
            throw new Exception("Failed to parse download url");
        }
        return uri;
    }
}