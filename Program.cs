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
Console.WriteLine($"Mod loaders: {string.Join(", ", manifest.Minecraft.ModLoaders.Select(x => $"{(x.Primary ? "primary" : "")} {x.Id}"))}");

var curseForgeClient = new CurseForgeClient(apiToken);
var httpClient = new HttpClient();

foreach (var file in manifest.Files.Take(1))
{
    var uri = await curseForgeClient.GetDownloadUri(file);
    var fileName = Path.GetFileName(uri?.LocalPath);

    if (fileName is null)
    {
        Console.WriteLine("Failed to get download url.");
        return;
    }

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
