using System.Drawing;
using System.Windows;
using System.Windows.Forms;

namespace Spezi.App;

sealed class TrayHost : IDisposable
{
    readonly NotifyIcon _icon;

    public event Action? OpenRequested;
    public event Action? ExitRequested;

    public TrayHost()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Spezi", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "Spezi",
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public void SetStatus(string text)
    {
        _icon.Text = text.Length <= 63 ? text : text[..63];
    }

    static Icon CreateIcon()
    {
        var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Spezi.ico"));
        if (info?.Stream == null)
            return System.Drawing.SystemIcons.Application;
        using var copy = new MemoryStream();
        info.Stream.CopyTo(copy);
        copy.Position = 0;
        return new Icon(copy, 32, 32);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
