namespace mmods.Models;

public record Modpack(
    string Format,
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