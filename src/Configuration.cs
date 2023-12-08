using Microsoft.Extensions.Configuration;

namespace mmods;

public static class Configuration
{
    public static IConfigurationRoot Config => new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", true)
        .AddEnvironmentVariables()
        .AddUserSecrets<Program>()
        .Build();

    public static string? GetCurseForgeToken() => Config.GetSection("CurseForge").Value;
}