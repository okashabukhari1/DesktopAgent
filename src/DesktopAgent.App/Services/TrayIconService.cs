using System.Drawing;
using DesktopAgent.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Forms = System.Windows.Forms;

namespace DesktopAgent.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly IWindowService _windows;
    private readonly ISettingsService _settings;
    private readonly ILogger<TrayIconService> _logger;
    private Forms.NotifyIcon? _icon;

    public TrayIconService(IWindowService windows, ISettingsService settings, ILogger<TrayIconService> logger)
    {
        _windows = windows;
        _settings = settings;
        _logger = logger;
    }

    public void Start()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Desktop App", null, (_, _) => _windows.ShowDesktopApp());
        menu.Items.Add("Assistant", null, (_, _) => _windows.ShowAssistant());
        menu.Items.Add("Workspaces", null, (_, _) => _windows.ShowWorkspaces());
        menu.Items.Add("Applications", null, (_, _) => _windows.ShowApplications());
        menu.Items.Add("Permissions", null, (_, _) => _windows.ShowPermissions());
        menu.Items.Add("Activity", null, (_, _) => _windows.ShowActivity());
        menu.Items.Add("Show Character", null, (_, _) => _windows.ShowCharacter());
        menu.Items.Add("Hide Character", null, (_, _) => _windows.HideCharacter());
        menu.Items.Add("Settings", null, (_, _) => _windows.ShowSettings());
        var alwaysOnTop = new Forms.ToolStripMenuItem("Always on Top") { CheckOnClick = true, Checked = _settings.Current.AlwaysOnTop };
        alwaysOnTop.CheckedChanged += async (_, _) =>
        {
            _settings.Current.AlwaysOnTop = alwaysOnTop.Checked;
            _windows.ShowCharacter();
            try { await _settings.SaveAsync(_settings.Current); }
            catch (Exception exception) { _logger.LogWarning(exception, "Always-on-top preference could not be saved"); }
        };
        menu.Items.Add(alwaysOnTop);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _windows.Shutdown());

        _icon = new Forms.NotifyIcon
        {
            Text = "Desktop Agent",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => _windows.ShowDesktopApp();
    }

    public void Dispose()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _icon = null;
    }
}
