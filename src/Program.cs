using CurseForge;
using Spectre.Console;
using mmods;
using static mmods.CLI;
using static mmods.Utils;
using static mmods.Report;
using mmods.Services;

AppDomain.CurrentDomain.UnhandledException += (_, args) =>
{
    AnsiConsole.WriteException((Exception)args.ExceptionObject);
    Environment.Exit(1);
};

var (modpackGlob, outputPath) = ParseArgs(args);
var modpackFiles = MatchFiles(modpackGlob);

EnsureDirectoryExists(outputPath);

using var stream = GetStream(modpackFiles);

IService service = new CurseForgeService();

using var modpack = await service.GetModpack(stream);
var (files, overridesCount) = await service.GetFiles(modpack);

PrintModpackInfo(modpack, files.Length, overridesCount);

var downloader = new Downloader(files, service, outputPath);

var downloadSummary = await AnsiConsole.Progress()
    .AutoClear(true)
    .HideCompleted(true)
    .Columns([
        new TaskDescriptionColumn(),
        new ProgressBarColumn(),
        new PercentageColumn(),
        new RemainingTimeColumn(),
        new SpinnerColumn(),
    ])
    .StartAsync(downloader.DownloadMods);

var downloadedCount = downloadSummary.Sum(x => x.Value.Count);
if (downloadedCount < files.Length)
{
    throw new Exception($"[red]Failed to download all required files.[/] Downloaded {downloadedCount}/{files.Length}");
}

var overridesSummary = await service.ApplyOverrides(modpack, outputPath);

var report = GetReport(modpack, downloadSummary, overridesSummary);
await File.WriteAllTextAsync(Path.Combine(outputPath, "README.md"), report);

stream.Dispose();

using var stream2 = GetStream(modpackFiles);

using var file = File.Create("modpack.zip");
stream2.CopyTo(file);
file.Close();