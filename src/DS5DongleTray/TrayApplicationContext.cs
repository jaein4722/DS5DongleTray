using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ConfigPageUrl = "https://ds5.awalol.eu.org";
    private const string GitHubRepositoryUrl = "https://github.com/jaein4722/DS5Dongle";
    private readonly DongleHidClient client;
    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer timer;
    private readonly ToolStripMenuItem batteryItem;
    private readonly ToolStripMenuItem firmwareItem;
    private readonly ToolStripMenuItem rssiItem;
    private readonly ToolStripMenuItem refreshItem;
    private SettingsForm? settingsForm;
    private FirmwareUpdateForm? firmwareUpdateForm;
    private Icon? currentIcon;
    private bool polling;

    public TrayApplicationContext(DongleHidClient client)
    {
        this.client = client;

        batteryItem = new ToolStripMenuItem("Battery: unknown") { Enabled = false };
        firmwareItem = new ToolStripMenuItem("Firmware: unknown") { Enabled = false };
        rssiItem = new ToolStripMenuItem("RSSI: unknown") { Enabled = false };
        refreshItem = new ToolStripMenuItem("Refresh now", null, async (_, _) => await RefreshAsync());
        var settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings());
        var updateFirmwareItem = new ToolStripMenuItem("Update Firmware...", null, (_, _) => ShowFirmwareUpdate());
        var openConfigItem = new ToolStripMenuItem("Open Config Page", null, (_, _) => OpenUrl(ConfigPageUrl));
        var openRepoItem = new ToolStripMenuItem("Open GitHub Repository", null, (_, _) => OpenUrl(GitHubRepositoryUrl));
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            batteryItem,
            firmwareItem,
            rssiItem,
            new ToolStripSeparator(),
            refreshItem,
            settingsItem,
            updateFirmwareItem,
            new ToolStripSeparator(),
            openConfigItem,
            openRepoItem,
            new ToolStripSeparator(),
            exitItem
        ]);

        currentIcon = BatteryIconFactory.Create(new DongleSnapshot { DeviceFound = false });
        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = currentIcon,
            Text = "DS5Dongle: starting",
            Visible = true
        };

        timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (polling)
        {
            return;
        }

        polling = true;
        refreshItem.Enabled = false;

        try
        {
            var snapshot = await client.ReadSnapshotAsync();
            ApplySnapshot(snapshot);
        }
        finally
        {
            refreshItem.Enabled = true;
            polling = false;
        }
    }

    private void ApplySnapshot(DongleSnapshot snapshot)
    {
        batteryItem.Text = snapshot.BatteryMenuText;
        firmwareItem.Text = $"Firmware: {snapshot.FirmwareVersion ?? "unknown"}";
        rssiItem.Text = $"RSSI: {(snapshot.Rssi.HasValue ? snapshot.Rssi.Value.ToString() : "unknown")}";
        notifyIcon.Text = TrimTooltip(snapshot.TooltipText);

        var oldIcon = currentIcon;
        currentIcon = BatteryIconFactory.Create(snapshot);
        notifyIcon.Icon = currentIcon;
        oldIcon?.Dispose();
    }

    private void ShowSettings()
    {
        if (settingsForm is { IsDisposed: false })
        {
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(client);
        settingsForm.FormClosed += async (_, _) =>
        {
            settingsForm = null;
            await RefreshAsync();
        };
        settingsForm.Show();
    }

    private void ShowFirmwareUpdate()
    {
        if (firmwareUpdateForm is { IsDisposed: false })
        {
            firmwareUpdateForm.Activate();
            return;
        }

        firmwareUpdateForm = new FirmwareUpdateForm(new FirmwareUpdater(client));
        firmwareUpdateForm.FormClosed += async (_, _) =>
        {
            firmwareUpdateForm = null;
            await RefreshAsync();
        };
        firmwareUpdateForm.Show();
    }

    private static string TrimTooltip(string text)
    {
        return text.Length <= 63 ? text : text[..63];
    }

    private static void OpenUrl(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    protected override void ExitThreadCore()
    {
        settingsForm?.Close();
        firmwareUpdateForm?.Close();
        timer.Stop();
        timer.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        currentIcon?.Dispose();
        base.ExitThreadCore();
    }
}
