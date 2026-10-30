using System.Text.Json;
using JsonForge.Models;
using Windows.Storage;

namespace JsonForge.Services;

public sealed class UserSettingsService : IUserSettingsService
{
    private const string Key = "JsonForge.UserSettings.v1";
    private readonly object _gate = new();
    private readonly ApplicationDataContainer? _local;
    private UserSettings _current;

    public UserSettingsService()
    {
        try
        {
            _local = ApplicationData.Current.LocalSettings;
        }
        catch
        {
            // ApplicationData is unavailable on some targets; run with in-memory settings.
            _local = null;
        }

        _current = Load();
    }

    public UserSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public event Action<UserSettings>? Changed;

    public void Update(UserSettings next)
    {
        lock (_gate)
        {
            _current = next;
            Save(next);
        }

        Changed?.Invoke(next);
    }

    private UserSettings Load()
    {
        try
        {
            if (_local is not null && _local.Values.TryGetValue(Key, out var value) && value is string json && !string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<UserSettings>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // ignore; fall back to defaults
        }

        return new UserSettings();
    }

    private void Save(UserSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
        {
            WriteIndented = false,
        });

        if (_local is not null)
        {
            _local.Values[Key] = json;
        }
    }
}
