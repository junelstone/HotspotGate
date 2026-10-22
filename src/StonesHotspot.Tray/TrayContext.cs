using System.ComponentModel;
using System.Diagnostics;
using System.ServiceProcess;
using StonesHotspot.Core;
using StonesHotspot.Core.Localization;

namespace StonesHotspot.Tray;

/// <summary>Tray icon showing the service state, with Start/Stop and "Open dashboard".</summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string ServiceName = ProductInfo.ServiceName;
    private static readonly string DashboardUrl = $"http://localhost:{ProductInfo.DefaultAdminPort}/admin";

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayContext()
    {
        _statusItem = new ToolStripMenuItem(Strings.Status_Unknown) { Enabled = false };
        _startItem = new ToolStripMenuItem(Strings.Tray_StartService, null, (_, _) => ControlService(start: true));
        _stopItem = new ToolStripMenuItem(Strings.Tray_StopService, null, (_, _) => ControlService(start: false));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.Tray_OpenDashboard, null, (_, _) => OpenDashboard()));
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Strings.Tray_Exit, null, (_, _) => ExitThread()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = Strings.AppName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenDashboard();

        _timer = new System.Windows.Forms.Timer { Interval = 5000 };
        _timer.Tick += (_, _) => RefreshStatus();
        _timer.Start();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var status = ReadServiceStatus();
        var text = status switch
        {
            ServiceControllerStatus.Running => Strings.Status_Running,
            ServiceControllerStatus.Stopped => Strings.Status_Stopped,
            null => Strings.Status_NotInstalled,
            _ => Strings.Status_Unknown,
        };

        _statusItem.Text = text;
        _icon.Text = $"{Strings.AppName} — {text}";
        _startItem.Enabled = status == ServiceControllerStatus.Stopped;
        _stopItem.Enabled = status == ServiceControllerStatus.Running;
    }

    private static ServiceControllerStatus? ReadServiceStatus()
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            return service.Status;
        }
        catch (InvalidOperationException)
        {
            return null; // service not installed
        }
    }

    private void ControlService(bool start)
    {
        try
        {
            using var service = new ServiceController(ServiceName);
            if (start)
                service.Start();
            else
                service.Stop();
        }
        catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception { NativeErrorCode: 5 })
        {
            MessageBox.Show(Strings.Error_AdminRequired, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            MessageBox.Show(string.Format(Strings.Error_ServiceControlFailed, ex.Message), Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        RefreshStatus();
    }

    private static void OpenDashboard() =>
        Process.Start(new ProcessStartInfo(DashboardUrl) { UseShellExecute = true });

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _icon.Visible = false;
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
