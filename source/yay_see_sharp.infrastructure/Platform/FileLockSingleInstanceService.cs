using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.infrastructure.Platform;

public sealed class FileLockSingleInstanceService : ISingleInstanceService
{
    private readonly string _lockFilePath;
    private FileStream? _lockStream;

    public FileLockSingleInstanceService(string? lockFilePath = null)
    {
        _lockFilePath = lockFilePath ?? Path.Combine(
            Path.GetTempPath(),
            "yay_see_sharp.lock");
    }

    public bool TryAcquire()
    {
        if (_lockStream is not null)
        {
            return true;
        }

        try
        {
            _lockStream = new FileStream(
                _lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _lockStream?.Dispose();
        _lockStream = null;

        try
        {
            File.Delete(_lockFilePath);
        }
        catch (IOException)
        {
        }
    }
}
