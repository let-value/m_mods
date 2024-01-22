namespace mmods.Models;

public record ModpackFile(
    string Name
) : IDisposable
{
    public void Dispose()
    {
    }
}