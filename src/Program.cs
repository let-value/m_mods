using System.Collections.Concurrent;
using System.IO.Compression;
using ComposableAsync;
using CurseForge;
using Spectre.Console;
using static mmods.CLI;
using static mmods.Utils;
using static mmods.Download;
using static CurseForge.CurseForgeModpack;
using mmods;

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    AnsiConsole.WriteException((Exception)args.ExceptionObject);
    Environment.Exit(1);
};

var (modpackPath, outputPath) = ParseArgs(args);

EnsureDirectoryExists(outputPath);

var (manifest, archive) = ReadManifest(modpackPath);
var (requiredFiles, overrideEntries) = GetManifestFiles(manifest, archive);
var modLoaders = PrintModpackInfo(manifest, requiredFiles.Count, overrideEntries.Count);

var filesQueue = new ConcurrentQueue<(int, ModFileDescription)>(requiredFiles.Select(file => (3, file)));

var files = new ConcurrentDictionary<FileType, ConcurrentBag<string>>();

async ValueTask ProcessMod((int, ModFileDescription) job, ProgressContext context, CancellationToken cancellationToken)
{
    try
    {
        var (fileType, fileName) = await DownloadMod(job, outputPath, context, cancellationToken);

        files.AddOrUpdate(fileType, new ConcurrentBag<string> { fileName }, (_, bag) =>
        {
            bag.Add(fileName);
            return bag;
        });
    }
    catch
    {
        var (tries, file) = job;
        if (tries > 0)
            filesQueue.Enqueue((tries - 1, file));
    }
}

Task DownloadMods(ProgressContext context) => Parallel.ForEachAsync(
    filesQueue,
    new ParallelOptions { MaxDegreeOfParallelism = 4 },
    (job, cancellationToken) => ProcessMod(job, context, cancellationToken)
);

await AnsiConsole.Progress()
    .AutoClear(true)
    .HideCompleted(true)
    .Columns([
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn(),
        new SpinnerColumn(),
    ])
    .StartAsync(DownloadMods);

foreach (var entry in overrideEntries)
{
    var relativePath = entry.FullName.Replace($"{manifest.Overrides}/", "");
    var filePath = Path.Combine(outputPath, relativePath);

    FileType? fileType = relativePath switch
    {
        var x when x.StartsWith("mods") => FileType.Mod,
        var x when x.StartsWith("resourcepacks") => FileType.ResourcePack,
        var x when x.StartsWith("shaderpacks") => FileType.ShaderPack,
        _ => null
    };

    if (fileType is not null)
    {
        files.AddOrUpdate(FileType.Mod, new ConcurrentBag<string> { Path.GetFileName(filePath) }, (_, bag) =>
                {
                    bag.Add(Path.GetFileName(filePath));
                    return bag;
                });
    }

    var directoryPath = Path.GetDirectoryName(filePath);
    if (directoryPath is null)
    {
        AnsiConsole.MarkupLineInterpolated($"Failed to get directory path for {Markup.Escape(relativePath)}");
        continue;
    }

    if (!Directory.Exists(directoryPath))
    {
        Directory.CreateDirectory(directoryPath);
    }

    entry.ExtractToFile(filePath, true);
}

archive.Dispose();

if (files.Sum(x => x.Value.Count) < requiredFiles.Count)
{
    throw new Exception($"[red]Failed to download all required files.[/] Downloaded {files.Count}/{requiredFiles.Count}");
}

var modList = files.Select(x =>
{
    var list = x.Value.Select(y => $"- {y}").ToList();
    list.Sort();
    return $@"
### {x.Key} ({list.Count})

<details>
<summary>Show list</summary>

{string.Join(Environment.NewLine, list)}

</details>

";
});

var description = $@"
# Modpack

- Name: {manifest.Name},
- Version: {manifest.Version}
- Minecraft version: {manifest.Minecraft.Version}
- Mod loaders: {modLoaders}

## Files

{string.Join(Environment.NewLine, modList)}

";

var readmePath = Path.Combine(outputPath, "README.md");
await File.WriteAllTextAsync(readmePath, description);
