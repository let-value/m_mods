using System.IO.Compression;
using System.Text.Json;
using CurseForge;
using Microsoft.Extensions.Configuration;

string modpackPath = args[0];
string outputPath = args[1];

if (!File.Exists(modpackPath))
{
    Console.WriteLine("Modpack zip file not found.");
    return;
}

if (!Directory.Exists(outputPath))
{
    Directory.CreateDirectory(outputPath);
}

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

var apiToken = config.GetSection("CurseForge").Value;

using var archive = ZipFile.OpenRead(modpackPath);

var manifestEntry = archive.Entries.First(x => x.FullName == "manifest.json");
using var manifestStream = manifestEntry.Open();
var manifest = JsonSerializer.Deserialize<Manifest>(manifestStream);

if (manifest is null || manifest.ManifestType != "minecraftModpack")
{
    Console.WriteLine("Not a curseforge modpack.");
    return;
}

Console.WriteLine($"Modpack: {manifest.Name}, Version:{manifest.Version}");
Console.WriteLine($"Minecraft version: {manifest.Minecraft.Version}");
var modLoaders = string.Join(", ", manifest.Minecraft.ModLoaders.Select(x => $"{(x.Primary ? "primary" : "")} {x.Id}"));
Console.WriteLine($"Mod loaders: {modLoaders}");

var curseForgeClient = new CurseForgeClient(apiToken);
var httpClient = new HttpClient();

var modList = new List<string>();

foreach (var file in manifest.Files.Take(1))
{
    var uri = await curseForgeClient.GetDownloadUri(file);
    var fileName = Path.GetFileName(uri?.LocalPath);

    if (fileName is null)
    {
        Console.WriteLine("Failed to get download url.");
        return;
    }

    modList.Add(fileName);

    var filePath = Path.Combine(outputPath, "mods", fileName);

    if (File.Exists(filePath))
    {
        Console.WriteLine($"File {fileName} already exists.");
        continue;
    }

    var directoryPath = Path.GetDirectoryName(filePath);

    if (directoryPath is null)
    {
        Console.WriteLine($"Failed to get directory path for {fileName}");
        continue;
    }

    if (!Directory.Exists(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }

    Console.WriteLine($"Downloading {fileName}...");

    using var response = await httpClient.GetAsync(uri);
    await using var stream = await response.Content.ReadAsStreamAsync();
    await using var fileStream = File.Create(filePath);
    await stream.CopyToAsync(fileStream);

    Console.WriteLine($"Downloaded {fileName}.");
}

var overrideEntries = archive.Entries.Where(x => x.FullName.StartsWith(manifest.Overrides)).ToList();
foreach (var entry in overrideEntries)
{
    var relativePath = entry.FullName.Replace($"{manifest.Overrides}/", "");
    var filePath = Path.Combine(outputPath, relativePath);

    if (relativePath.StartsWith("mods/"))
    {
        modList.Add(Path.GetFileName(filePath));
    }

    if (File.Exists(filePath))
    {
        Console.WriteLine($"File {relativePath} already exists.");
        continue;
    }

    var directoryPath = Path.GetDirectoryName(filePath);
    if (directoryPath is null)
    {
        Console.WriteLine($"Failed to get directory path for {relativePath}");
        continue;
    }

    if (!Directory.Exists(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }

    entry.ExtractToFile(filePath);
}

var description = $@"
# Modpack

- Name: {manifest.Name},
- Version: {manifest.Version}
- Minecraft version: {manifest.Minecraft.Version}
- Mod loaders: {modLoaders}

## Mods ({modList.Count})

{string.Join(Environment.NewLine, modList.Select(x => $"- {x}"))}
";

var readmePath = Path.Combine(outputPath, "README.md");
await File.WriteAllTextAsync(readmePath, description);
