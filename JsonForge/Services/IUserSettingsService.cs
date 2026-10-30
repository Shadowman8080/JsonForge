using JsonForge.Models;

namespace JsonForge.Services;

public interface IUserSettingsService
{
    UserSettings Current { get; }

    event Action<UserSettings>? Changed;

    void Update(UserSettings next);
}
