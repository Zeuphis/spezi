using CommunityToolkit.Mvvm.ComponentModel;

namespace Spezi.Core.ViewModels;

// Settings > Notifications and updates. Writes through to AppSettings and saves on every change.
public sealed partial class NotificationSettingsViewModel : ObservableObject
{
    readonly Func<AppSettings> _settings;
    readonly Action _save;
    bool _loading;

    public NotificationSettingsViewModel(Func<AppSettings> settings, Action save)
    {
        _settings = settings;
        _save = save;
        Load();
    }

    [ObservableProperty]
    private bool _statusToasts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToastDurationText))]
    private double _toastDurationSeconds;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup;

    public string ToastDurationText => $"{ToastDurationSeconds:0.#} seconds";

    public void Load()
    {
        _loading = true;
        try
        {
            var settings = _settings();
            StatusToasts = settings.StatusToasts;
            ToastDurationSeconds = AppSettings.ValidToastDuration(settings.ToastDurationSeconds);
            CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        }
        finally { _loading = false; }
    }

    partial void OnStatusToastsChanged(bool value) => Apply(s => s.StatusToasts = value);
    partial void OnCheckForUpdatesOnStartupChanged(bool value) => Apply(s => s.CheckForUpdatesOnStartup = value);

    partial void OnToastDurationSecondsChanged(double value)
    {
        var valid = AppSettings.ValidToastDuration(value);
        if (valid != value) { ToastDurationSeconds = valid; return; }
        Apply(s => s.ToastDurationSeconds = valid);
    }

    void Apply(Action<AppSettings> change)
    {
        if (_loading) return;
        change(_settings());
        _save();
    }
}
