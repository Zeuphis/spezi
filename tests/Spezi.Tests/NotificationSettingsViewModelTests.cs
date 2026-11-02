using System.Globalization;
using Spezi.Core;
using Spezi.Core.ViewModels;
using Xunit;

namespace Spezi.Tests;

public class NotificationSettingsViewModelTests
{
    static (NotificationSettingsViewModel vm, AppSettings s, Func<int> saves) Make(AppSettings? settings = null)
    {
        var s = settings ?? new AppSettings();
        var count = 0;
        return (new NotificationSettingsViewModel(() => s, () => count++), s, () => count);
    }

    [Fact]
    public void LoadsWithoutSaving()
    {
        var (vm, _, saves) = Make(new AppSettings { StatusToasts = false, ToastDurationSeconds = 6, CheckForUpdatesOnStartup = false });
        Assert.False(vm.StatusToasts);
        Assert.Equal(6, vm.ToastDurationSeconds);
        Assert.False(vm.CheckForUpdatesOnStartup);
        Assert.Equal(0, saves());
    }

    [Fact]
    public void ChangesWriteThroughAndSave()
    {
        var (vm, s, saves) = Make();
        vm.StatusToasts = false;
        vm.CheckForUpdatesOnStartup = false;
        vm.ToastDurationSeconds = 7.5;
        Assert.False(s.StatusToasts);
        Assert.False(s.CheckForUpdatesOnStartup);
        Assert.Equal(7.5, s.ToastDurationSeconds);
        Assert.Equal(3, saves());
    }

    [Fact]
    public void DurationIsClampedAndTextUpdates()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var (vm, s, _) = Make();
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.ToastDurationSeconds = 99;
            Assert.Equal(15, vm.ToastDurationSeconds);
            Assert.Equal(15, s.ToastDurationSeconds);
            Assert.Equal("15 seconds", vm.ToastDurationText);
            Assert.Contains(nameof(vm.ToastDurationText), changed);
            vm.ToastDurationSeconds = 2.5;
            Assert.Equal("2.5 seconds", vm.ToastDurationText);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void DefaultsCheckForUpdates() => Assert.True(new AppSettings().CheckForUpdatesOnStartup);
}
