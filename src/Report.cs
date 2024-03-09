using System.Collections.Concurrent;
using mmods.Models;

namespace mmods;

public static class Report
{
    public static string GetReport(
        Modpack modpack,
        ConcurrentDictionary<FileType, ConcurrentDictionary<string, bool>> downloadSummary,
        Dictionary<FileType, HashSet<string>> overridesSummary
        )
    {
        var summary = downloadSummary.ToDictionary(x => x.Key, x => new HashSet<string>(x.Value.Keys));

        foreach (var (fileType, files) in overridesSummary)
        {
            if (!summary.ContainsKey(fileType))
            {
                summary[fileType] = new HashSet<string>();
            }

            summary[fileType].UnionWith(files);
        }

        var modList = summary.Select(x =>
        {
            var (fileType, files) = x;

            var list = files.Select(file => $"- {file}").ToList();
            list.Sort();

            return $@"
### {fileType} ({files.Count})

<details>
<summary>Show list</summary>

{string.Join(Environment.NewLine, list)}

</details>

";
        });

        var description = $@"
# Modpack

- Format: {modpack.Format},
- Name: {modpack.Name},
- Description: {modpack.Description}
- Author: {modpack.Author}
- Version: {modpack.Version}
{(modpack.Dependencies is not null or [] ? $"- {string.Join("\n - ", modpack.Dependencies)}" : "")}


## Files

{string.Join(Environment.NewLine, summary.Select(x => $"- {x.Key}: {x.Value.Count}"))}

{string.Join(Environment.NewLine, modList)}

";

        return description;
    }
}