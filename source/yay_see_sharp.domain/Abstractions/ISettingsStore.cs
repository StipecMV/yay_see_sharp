using yay_see_sharp.domain.Models;

namespace yay_see_sharp.domain.Abstractions;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
