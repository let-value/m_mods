using CurseForge;
using DotNet.Globbing;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using Spectre.Console;

namespace mmods;

public static class CLI
{
    public static (string[] files, string outputPath) ParseArgs(string[] args)
    {
        var result = args switch
        {
        [var modpackPath, var outputPath] => (modpackPath, outputPath),
            _ => throw new ArgumentException("Expected 2 arguments: modpackPath outputPath")
        };

        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        var matcher = new Matcher();
        matcher.AddInclude(result.modpackPath);

        var matches = matcher.Execute(new DirectoryInfoWrapper(directory));


        if (!matches.HasMatches)
        {
            throw new ArgumentException("Modpack archive file not found.");
        }

        var files = matches.Files.Select(x => x.Path).ToArray();

        AnsiConsole.MarkupLineInterpolated($"Directory: {Directory.GetCurrentDirectory()}, Pattern: {result.modpackPath}, Files found: {Markup.Escape(string.Join(", ", files))}");

        return (files, result.outputPath);
    }

    public static string PrintModpackInfo(Manifest manifest, int requiredFilesCount, int overrideEntriesCount)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(["Name", Markup.Escape(manifest.Name)]);
        grid.AddRow(["Version", Markup.Escape(manifest.Version)]);
        grid.AddRow(["Minecraft", Markup.Escape(manifest.Minecraft.Version)]);

        var modLoaders = string.Join(", ", manifest.Minecraft.ModLoaders.Select(x => $"{(x.Primary ? ":check_mark:" : "")} {x.Id}"));

        grid.AddRow(["Mod loaders", Markup.Escape(modLoaders)]);
        grid.AddRow(["Mods", Markup.Escape(requiredFilesCount.ToString())]);
        grid.AddRow(["Overrides", Markup.Escape(overrideEntriesCount.ToString())]);

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("Modpack"),
        };

        AnsiConsole.Write(panel);

        return modLoaders;
    }

    public static CombinationStream.CombinationStream GetStream(string[] files)
    {
        var resolvedFiles = files.Select(x => Path.Combine(Directory.GetCurrentDirectory(), x));
        var streams = resolvedFiles.Select(x => new FileStream(x, FileMode.Open) as Stream).ToList();

        return new CombinationStream.CombinationStream(streams);
    }
}