namespace mmods.Models;

public record Modpack(
    string Name,
    string? Author,
    string? Version,
    string? Description,
    string[]? Dependencies
) : IDisposable
{
    public void Dispose()
    {

    }
}