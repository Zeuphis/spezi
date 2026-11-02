using System.Windows;
using System.Windows.Controls;

namespace Spezi.App;

static class Prompt
{
    public static string? Ask(Window? owner, string title, string initial)
    {
        var window = new Window
        {
            Title = title,
            Width = 440,
            Height = 180,
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["Bg"]
        };
        var box = new System.Windows.Controls.TextBox { Text = initial, Margin = new Thickness(16, 16, 16, 8) };
        var ok = new System.Windows.Controls.Button { Content = "Save", Style = (Style)System.Windows.Application.Current.Resources["PrimaryButton"], IsDefault = true };
        var cancel = new System.Windows.Controls.Button { Content = "Cancel", Style = (Style)System.Windows.Application.Current.Resources["GhostButton"], IsCancel = true };
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; window.DialogResult = true; };
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(16) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(box);
        window.Content = root;
        window.ShowDialog();
        return result;
    }
}
