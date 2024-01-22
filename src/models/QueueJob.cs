namespace mmods.Models;

public record QueueJob(ModpackFile File, int retries = 3);