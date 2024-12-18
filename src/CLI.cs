using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using mmods.Models;
using Spectre.Console;

namespace mmods;

public static class CLI
{
    public static (string modpackFormat, string modpackGlob, string outputPath) ParseArgs(string[] args)
    {
        var (modpackFormat, modpackGlob, outputPath) = args switch
        {
        [var format, var modpack, var output] => (format, modpack, output),
            _ => throw new ArgumentException("Expected 3 arguments: modpackFormat modpackGlob outputPath")
        };

        return (modpackFormat, modpackGlob, outputPath);
    }

    public static string[] MatchFiles(string modpackGlob)
    {
        var cwd = Directory.GetCurrentDirectory();
        Console.WriteLine($"Searching for modpack archive file: {modpackGlob}, in {cwd}");
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var matcher = new Matcher();
        matcher.AddInclude(modpackGlob);

        var matches = matcher.Execute(new DirectoryInfoWrapper(directory));
        if (!matches.HasMatches)
        {
            throw new ArgumentException("Modpack archive file not found.");
        }

        var files = matches.Files.Select(x => x.Path).ToList();
        files.Sort();

        Console.WriteLine(String.Join("\n", files));

        return files.ToArray();
    }

    public static CombinationStream.CombinationStream GetStream(string[] files)
    {
        var resolvedFiles = files.Select(x => Path.Combine(Directory.GetCurrentDirectory(), x));
        var streams = resolvedFiles.Select(x => new FileStream(x, FileMode.Open) as Stream).ToList();

        return new CombinationStream.CombinationStream(streams);
    }

    public static void PrintModpackInfo(Modpack manifest, int filesCount, int overridesCount)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        var dependencies = (manifest.Dependencies ?? Array.Empty<string>())
            .ToArray()
            .Select<string, string[]>(x => ["", Markup.Escape(x)])
            .ToArray();

        string[][] rows = [
            ["Format", Markup.Escape(manifest.Format)],
            ["Name", Markup.Escape(manifest.Name)],
            manifest.Author is not null or "" ? ["Author", Markup.Escape(manifest.Author)] : [],
            manifest.Description is not null or "" ? ["Description", Markup.Escape(manifest.Description)] : [],
            manifest.Version is not null or "" ? ["Version", Markup.Escape(manifest.Version)] : [],
            ..dependencies,
            ["Files", Markup.Escape(filesCount.ToString())],
            ["Overrides", Markup.Escape(overridesCount.ToString())],
        ];

        foreach (var row in rows)
        {
            grid.AddRow(row);
        }

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("Modpack"),
        };

        AnsiConsole.Write(panel);
    }
}