using System.IO.Compression;
using System.Text.Json;
using CurseForge;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;


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

var output = new PhysicalFileProvider(outputPath);

var config = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", true)
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>()
    .Build();

using var archive = ZipFile.OpenRead(modpackPath);

var manifestEntry = archive.Entries.First(x => x.FullName == "manifest.json");
var rest = archive.Entries.Where(x => x.FullName != "manifest.json").ToList();

using var manifestStream = manifestEntry.Open();
var manifest = JsonSerializer.Deserialize<Manifest>(manifestStream);

if (manifest is null || manifest.ManifestType != "minecraftModpack")
{
    Console.WriteLine("Not a minecraft modpack.");
    return;
}