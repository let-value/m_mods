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