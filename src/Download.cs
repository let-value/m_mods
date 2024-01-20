using CurseForge;
using Spectre.Console;
using static mmods.Utils;

namespace mmods;

public enum FileType
{
    Mod,
    ResourcePack,
    ShaderPack,
}

public static class Download
{
    public static HttpClient HttpClient = new(Limiter);
    public static CurseForgeClient CurseForgeClient = new();

    public static async Task DownloadFile(Uri uri, string filePath, string fileName, ProgressTask task, CancellationToken cancellationToken)
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

    public static async Task<(FileType, string)> DownloadMod(
        (int, ModFileDescription) job,
        string outputPath,
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
            var fileType = await CurseForgeClient.GetFileType(file);

            var outputDirectory = fileType switch
            {
                FileType.Mod => "mods",
                FileType.ResourcePack => "resourcepacks",
                FileType.ShaderPack => "shaderpacks",
                _ => throw new Exception($"Unknown category {fileType}")
            };

            var versions = await CurseForgeClient.GetDownloadUris(file);

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
                throw new Exception();
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

        throw new Exception();
    }
}