using System.Collections.Concurrent;
using mmods.Models;
using mmods.Services;
using Spectre.Console;
using static mmods.Utils;

namespace mmods;

public class Downloader(ModpackFile[] files, IService service, string outputPath)
{
    private HttpClient HttpClient = new(Limiter);
    ConcurrentQueue<QueueJob> Queue = new(files.Select(x => new QueueJob(x, 3)));
    ConcurrentDictionary<FileType, ConcurrentDictionary<string, bool>> Summary = new();

    async ValueTask ProcessJob(QueueJob job, ProgressContext context, CancellationToken cancellationToken)
    {
        try
        {
            var (fileType, fileName) = await DownloadMod(job, outputPath, context, cancellationToken);

            Summary.AddOrUpdate(fileType, new ConcurrentDictionary<string, bool> { [fileName] = true }, (_, bag) =>
            {
                bag.AddOrUpdate(fileName, true, (_, _) => true);
                return bag;
            });
        }
        catch
        {
            var (file, retries) = job;
            if (retries > 0)
                Queue.Enqueue(new(file, retries - 1));
        }
    }

    public async Task<ConcurrentDictionary<FileType, ConcurrentDictionary<string, bool>>> DownloadMods(ProgressContext context)
    {
        await Parallel.ForEachAsync(
            Queue,
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            (job, cancellationToken) => ProcessJob(job, context, cancellationToken)
        );

        return Summary;
    }

    public async Task<(FileType, string)> DownloadMod(
        QueueJob job,
        string outputPath,
        ProgressContext context,
        CancellationToken cancellationToken
    )
    {
        var (file, tries) = job;

        var task = context.AddTask(
            file.Name,
            new ProgressTaskSettings
            {
                AutoStart = false
            }
        );

        try
        {
            var fileType = await service.GetFileType(file);
            var outputDirectory = GetOutputDirectory(fileType);
            var uris = await service.GetDownloadUris(file);

            for (var i = 0; i < uris.Length; i++)
                try
                {
                    var uri = uris[i];

                    var fileName = Path.GetFileName(uri?.LocalPath);

                    if (fileName is null)
                    {
                        AnsiConsole.MarkupLine("Failed to get download url.");
                        continue;
                    }

                    task.Description = $"File: {Markup.Escape(fileName)}";

                    var filePath = Path.Combine(outputPath!, outputDirectory, fileName);
                    if (File.Exists(filePath))
                    {
                        AnsiConsole.MarkupLine($"File {Markup.Escape(fileName)} already exists.");
                        return (fileType, fileName);
                    }

                    var directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath is null)
                    {
                        throw new Exception($"Failed to get directory path for {Markup.Escape(fileName)}");
                    }

                    EnsureDirectoryExists(directoryPath);

                    await DownloadFile(uri!, filePath, fileName, task, cancellationToken);
                    return (fileType, fileName);
                }
                catch (Exception exception)
                {
                    AnsiConsole.MarkupLine($"Failed to download url {i}/{uris.Length}, retrying...");
                    if (i == uris.Length - 1)
                        throw new Exception($"Failed to download any urls.", exception);
                }
        }
        catch (Exception exception)
        {
            AnsiConsole.WriteException(exception);
            if (tries > 0)
            {
                AnsiConsole.MarkupLine($"{file.Name}, retrying...");
                throw new Exception();
            }
            else
            {
                AnsiConsole.MarkupLine($"Failed to download {file.Name}.");
            }
        }
        finally
        {
            task.StopTask();
        }

        throw new Exception();
    }

    public async Task DownloadFile(Uri uri, string filePath, string fileName, ProgressTask task, CancellationToken cancellationToken)
    {
        using var response = await HttpClient!.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

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

    private string GetOutputDirectory(FileType fileType) => fileType switch
    {
        FileType.Mod => "mods",
        FileType.ResourcePack => "resourcepacks",
        FileType.ShaderPack => "shaderpacks",
        _ => throw new Exception($"Unknown category {fileType}")
    };
}