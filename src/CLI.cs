using CurseForge;
using Spectre.Console;

namespace mmods;

public static class CLI
{
    public static (string modpackPath, string outputPath) ParseArgs(string[] args)
    {
        var result = args switch
        {
        [var modpackPath, var outputPath] => (modpackPath, outputPath),
            _ => throw new ArgumentException("Expected 2 arguments: modpackPath outputPath")
        };

        if (!File.Exists(result.modpackPath))
        {
            throw new ArgumentException("Modpack archive file not found.");
        }

        return result;
    }

    public static string PrintModpackInfo(Manifest manifest, int requiredFilesCount, int overrideEntriesCount)
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(["Name", manifest.Name]);
        grid.AddRow(["Version", manifest.Version]);
        grid.AddRow(["Minecraft", manifest.Minecraft.Version]);

        var modLoaders = string.Join(", ", manifest.Minecraft.ModLoaders.Select(x => $"{(x.Primary ? ":check_mark:" : "")} {x.Id}"));

        grid.AddRow(["Mod loaders", modLoaders]);
        grid.AddRow(["Mods", requiredFilesCount.ToString()]);
        grid.AddRow(["Overrides", overrideEntriesCount.ToString()]);

        var panel = new Panel(grid)
        {
            Header = new PanelHeader("Modpack"),
        };

        AnsiConsole.Write(panel);

        return modLoaders;
    }
}