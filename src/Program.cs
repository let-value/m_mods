using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;
using ComposableAsync;
using CurseForge;
using RateLimiter;
using Spectre.Console;
using static mmods.CLI;
using static mmods.Utils;

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    AnsiConsole.WriteException((Exception)args.ExceptionObject);
    Environment.Exit(1);
};

var (modpackPath, outputPath) = ParseArgs(args);

EnsureDirectoryExists(outputPath);

using var archive = ZipFile.OpenRead(modpackPath);
var manifestEntry = archive.Entries.First(x => x.FullName == "manifest.json");
using var manifestStream = manifestEntry.Open();
var manifest = JsonSerializer.Deserialize<Manifest>(manifestStream);

if (manifest is null || manifest.ManifestType != "minecraftModpack")
{
    throw new Exception("Not a curseforge modpack.");
}

var requiredFiles = manifest.Files.Where(x => x.Required).ToList();
var overrideEntries = archive.Entries.Where(x => x.FullName.StartsWith(manifest.Overrides)).ToList();

var modLoaders = PrintModpackInfo(manifest, requiredFiles.Count, overrideEntries.Count);

var handler = TimeLimiter
    .GetFromMaxCountByInterval(100, TimeSpan.FromSeconds(10))
    .AsDelegatingHandler();
var curseForgeClient = new CurseForgeClient(handler);
var httpClient = new HttpClient(handler);

var filesQueue = new ConcurrentQueue<(int, ModFileDescription)>(requiredFiles.Select(file => (3, file)));

var mods = new ConcurrentBag<string>();

async Task DownloadFile(Uri uri, string filePath, string fileName, ProgressTask task, CancellationToken cancellationToken)
{
    using var response = await httpClient!.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

    try
    {
        response.EnsureSuccessStatusCode();
    }
    catch (Exception exception)
    {
        throw new Exception($"Failed to download {fileName} from {uri}", exception);
    }

    var contentLength = response.Content.Headers.ContentLength ?? 0;
    var bufferSize = 8192;

    task.MaxValue(contentLength);
    task.StartTask();

    AnsiConsole.MarkupLine($"Starting download of [u]{Markup.Escape(fileName)}[/] from [u]{Markup.Escape(uri!.ToString())}[/] ({contentLength} bytes)");

    await using var contentStream = await response.Content.ReadAsStreamAsync();
    using var fileStream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, true);
    var buffer = new byte[bufferSize];

    while (true)
    {
        var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
        if (read == 0)
        {
            AnsiConsole.MarkupLine($"Download of [u]{Markup.Escape(fileName)}[/] [green]completed![/]");
            break;
        }

        task.Increment(read);

        await fileStream.WriteAsync(buffer, 0, read);
    }
}

async ValueTask TryDownloadMod(
    (int, ModFileDescription) job,
    ProgressContext context,
    CancellationToken cancellationToken
)
{
    var (tries, file) = job;

    var taskName = $"ProjectID: {file.ProjectID}, FileID: {file.FileID}, Tries: {tries}";

    var task = context.AddTask(
        taskName,
        new ProgressTaskSettings
        {
            AutoStart = false
        }
    );

    try
    {
        var versions = await curseForgeClient.GetDownloadUris(file);

        for (var i = 0; i < versions.Length; i++)
            try
            {
                var uri = versions[i];

                var fileName = Path.GetFileName(uri?.LocalPath);

                if (fileName is null)
                {
                    AnsiConsole.MarkupLine("Failed to get download url.");
                    continue;
                }

                task.Description = $"{taskName}, File: {Markup.Escape(fileName)}";

                var filePath = Path.Combine(outputPath!, "mods", fileName);
                if (File.Exists(filePath))
                {
                    AnsiConsole.MarkupLine($"File {Markup.Escape(fileName)} already exists.");
                    mods!.Add(fileName);
                    return;
                }

                var directoryPath = Path.GetDirectoryName(filePath);
                if (directoryPath is null)
                {
                    throw new Exception($"Failed to get directory path for {Markup.Escape(fileName)}");
                }

                EnsureDirectoryExists(directoryPath);

                await DownloadFile(uri!, filePath, fileName, task, cancellationToken);
                mods!.Add(fileName);

                break;
            }
            catch (Exception exception)
            {
                AnsiConsole.MarkupLine($"Failed to download url {i}/{versions.Length}, retrying...");
                if (i == versions.Length - 1)
                    throw new Exception($"Failed to download any urls.", exception);
            }
    }
    catch (Exception exception)
    {
        AnsiConsole.WriteException(exception);
        if (tries > 0)
        {
            AnsiConsole.MarkupLine($"Failed to download {file.ProjectID} {file.FileID}, retrying...");
            filesQueue.Enqueue((tries - 1, file));
        }
        else
        {
            AnsiConsole.MarkupLine($"Failed to download {file.ProjectID} {file.FileID}.");
        }
    }
    finally
    {
        task.StopTask();
    }
}

Task DownloadMods(ProgressContext context) => Parallel.ForEachAsync(
    filesQueue,
    new ParallelOptions { MaxDegreeOfParallelism = 4 },
    (job, cancellationToken) => TryDownloadMod(job, context, cancellationToken)
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

    if (relativePath.StartsWith("mods/"))
    {
        mods.Add(Path.GetFileName(filePath));
    }

    if (File.Exists(filePath))
    {
        AnsiConsole.MarkupLineInterpolated($"File {relativePath} already exists.");
        continue;
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

    entry.ExtractToFile(filePath);
}

if (mods.Count < requiredFiles.Count)
{
    throw new Exception($"[red]Failed to download all required files.[/] Downloaded {mods.Count}/{requiredFiles.Count}");
}

var modList = mods.Select(x => $"- {x}").ToList();
modList.Sort();

var description = $@"
# Modpack

- Name: {manifest.Name},
- Version: {manifest.Version}
- Minecraft version: {manifest.Minecraft.Version}
- Mod loaders: {modLoaders}

## Mods ({mods.Count})

<details>
<summary>Show list</summary>

{string.Join(Environment.NewLine, modList)}

</details>

";

var readmePath = Path.Combine(outputPath, "README.md");
await File.WriteAllTextAsync(readmePath, description);
