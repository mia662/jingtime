using System.Text.Json;

namespace BeijingClock.Core;

public sealed record WindowPosition(int X, int Y);

public sealed class PositionStore
{
    public const int CoordinateLimit = 100_000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string path;

    public PositionStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        this.path = Path.GetFullPath(path);
    }

    public WindowPosition? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            WindowPosition? position = JsonSerializer.Deserialize<WindowPosition>(json, SerializerOptions);
            return position is not null && IsValid(position) ? position : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    public void Save(WindowPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (!IsValid(position))
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                position,
                $"Window coordinates must be between {-CoordinateLimit} and {CoordinateLimit}.");
        }

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("The settings path must have a parent directory.");
        }

        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            string json = JsonSerializer.Serialize(position, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static bool IsValid(WindowPosition position) =>
        position.X is >= -CoordinateLimit and <= CoordinateLimit &&
        position.Y is >= -CoordinateLimit and <= CoordinateLimit;

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch
        {
            // Preserve the original save exception. A same-directory orphaned temp file is harmless.
        }
    }
}
