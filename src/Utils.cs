using ComposableAsync;
using RateLimiter;

namespace mmods;

public static class Utils
{
    public static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    public static DelegatingHandler Limiter = TimeLimiter
        .GetFromMaxCountByInterval(100, TimeSpan.FromSeconds(10))
        .AsDelegatingHandler();
}