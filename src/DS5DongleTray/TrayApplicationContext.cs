using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string ConfigPageUrl = "https://ds5.awalol.eu.org";
    private const string GitHubRepositoryUrl = "https://github.com/jaein4722/DS5Dongle";
    private readonly DongleHidClient client;
    private readonly AppSettings settings;
    private readonly OverlayManager overlayManager;
    private readonly NotifyIcon notifyIcon;
    private readonly System.Windows.Forms.Timer timer;
    private readonly System.Windows.Forms.Timer inputTimer;
    private readonly System.Windows.Forms.Timer updateCheckTimer;
    private readonly ToolStripMenuItem batteryItem;
    private readonly ToolStripMenuItem firmwareItem;
    private readonly ToolStripMenuItem rssiItem;
    private readonly ToolStripMenuItem updateStatusItem;
    private readonly ToolStripMenuItem appUpdateStatusItem;
    private readonly ToolStripMenuItem refreshItem;
    private SettingsForm? settingsForm;
    private FirmwareUpdateForm? firmwareUpdateForm;
    private AppUpdateForm? appUpdateForm;
    private Icon? currentIcon;
    private DongleSnapshot? lastSnapshot;
    private bool polling;
    private bool inputPolling;
    private bool updateChecking;
    private bool appUpdateChecking;

    public TrayApplicationContext(DongleHidClient client, AppSettings settings)
    {
        this.client = client;
        this.settings = settings;
        overlayManager = new OverlayManager(settings);

        batteryItem = new ToolStripMenuItem("Battery: unknown") { Enabled = false };
        firmwareItem = new ToolStripMenuItem("Firmware: unknown") { Enabled = false };
        rssiItem = new ToolStripMenuItem("RSSI: unknown") { Enabled = false };
        updateStatusItem = new ToolStripMenuItem("Firmware update: checking...") { Enabled = false, Visible = false };
        appUpdateStatusItem = new ToolStripMenuItem("App update: checking...") { Enabled = false, Visible = false };
        refreshItem = new ToolStripMenuItem("Refresh now", null, async (_, _) => await RefreshAsync());
        var settingsItem = new ToolStripMenuItem("Settings...", null, (_, _) => ShowSettings());
        var updateFirmwareItem = new ToolStripMenuItem("Update Firmware...", null, (_, _) => ShowFirmwareUpdate());
        var updateAppItem = new ToolStripMenuItem("Update App...", null, (_, _) => ShowAppUpdate());
        var openConfigItem = new ToolStripMenuItem("Open Config Page", null, (_, _) => OpenUrl(ConfigPageUrl));
        var openRepoItem = new ToolStripMenuItem("Open GitHub Repository", null, (_, _) => OpenUrl(GitHubRepositoryUrl));
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(
        [
            batteryItem,
            firmwareItem,
            rssiItem,
            updateStatusItem,
            appUpdateStatusItem,
            new ToolStripSeparator(),
            refreshItem,
            settingsItem,
            updateFirmwareItem,
            updateAppItem,
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
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                overlayManager.ShowTrayClick(lastSnapshot);
            }
        };

        timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += async (_, _) => await RefreshAsync();
        timer.Start();

        inputTimer = new System.Windows.Forms.Timer { Interval = 100 };
        inputTimer.Tick += async (_, _) => await PollInputOverlayAsync();
        inputTimer.Start();

        updateCheckTimer = new System.Windows.Forms.Timer { Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds };
        updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesIfDueAsync();
        updateCheckTimer.Start();

        ApplyCachedUpdateStatus();
        ApplyCachedAppUpdateStatus();
        _ = RefreshAsync();
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(15));
            await CheckForUpdatesIfDueAsync(force: true);
            await CheckForAppUpdatesIfDueAsync(force: true);
        });
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
            overlayManager.HandleSnapshot(snapshot);
        }
        finally
        {
            refreshItem.Enabled = true;
            polling = false;
        }
    }

    private void ApplySnapshot(DongleSnapshot snapshot)
    {
        lastSnapshot = snapshot;
        batteryItem.Text = snapshot.BatteryMenuText;
        firmwareItem.Text = $"Firmware: {snapshot.FirmwareVersion ?? "unknown"}";
        rssiItem.Text = $"RSSI: {(snapshot.Rssi.HasValue ? snapshot.Rssi.Value.ToString() : "unknown")}";
        notifyIcon.Text = TrimTooltip(snapshot.TooltipText);

        var oldIcon = currentIcon;
        currentIcon = BatteryIconFactory.Create(snapshot);
        notifyIcon.Icon = currentIcon;
        oldIcon?.Dispose();
    }

    private async Task PollInputOverlayAsync()
    {
        if (inputPolling || !ShouldPollInputReports())
        {
            return;
        }

        inputPolling = true;
        try
        {
            var battery = await client.ReadInputBatteryAsync();
            if (battery is null)
            {
                return;
            }

            var snapshot = UpdateSnapshotBatteryCache(battery);
            overlayManager.HandleInputBattery(battery, snapshot);
        }
        catch
        {
            // PS-button overlay polling is opportunistic; normal tray polling reports connection errors.
        }
        finally
        {
            inputPolling = false;
        }
    }

    private bool ShouldPollInputReports()
    {
        return settings.Overlay.ShowOnPsButton
            || settings.Overlay.ShowOnLowBattery
            || settings.Overlay.ShowOnChargingStateChanged;
    }

    private DongleSnapshot UpdateSnapshotBatteryCache(BatteryStatus battery)
    {
        var snapshot = (lastSnapshot ?? new DongleSnapshot { DeviceFound = true }) with
        {
            DeviceFound = true,
            Battery = battery,
            BatteryUnsupported = false
        };

        if (HasBatteryChanged(lastSnapshot?.Battery, battery))
        {
            ApplySnapshot(snapshot);
            overlayManager.HandleSnapshot(snapshot);
        }
        else
        {
            lastSnapshot = snapshot;
        }

        return snapshot;
    }

    private static bool HasBatteryChanged(BatteryStatus? previous, BatteryStatus current)
    {
        return previous is null
            || previous.Percent != current.Percent
            || previous.PowerState != current.PowerState
            || previous.IsUsbPowered != current.IsUsbPowered
            || previous.IsConnected != current.IsConnected
            || previous.IsFresh != current.IsFresh;
    }

    private async Task CheckForUpdatesIfDueAsync(bool force = false)
    {
        if (updateChecking || !settings.UpdateCheck.Enabled)
        {
            if (!settings.UpdateCheck.Enabled)
            {
                updateStatusItem.Text = "Firmware update: auto-check off";
                updateStatusItem.Visible = true;
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && settings.UpdateCheck.LastCheckedAt != default &&
            now - settings.UpdateCheck.LastCheckedAt < TimeSpan.FromDays(1))
        {
            return;
        }

        updateChecking = true;
        updateStatusItem.Text = "Firmware update: checking...";
        updateStatusItem.Visible = true;
        try
        {
            var result = await new FirmwareUpdater(client).CheckForUpdateAsync();
            settings.UpdateCheck.LastCheckedAt = now;
            settings.UpdateCheck.LastFirmwareVersion = result.CurrentVersion;
            settings.UpdateCheck.LastLatestVersion = result.LatestRelease?.Tag;
            settings.UpdateCheck.LastRepository = result.LatestRelease?.Repository;
            settings.UpdateCheck.LastUpdateAvailable = result.IsUpdateAvailable;
            settings.Save();
            ApplyUpdateStatus(result);
        }
        catch
        {
            settings.UpdateCheck.LastCheckedAt = now;
            settings.UpdateCheck.LastUpdateAvailable = false;
            settings.Save();
            updateStatusItem.Text = "Firmware update: check failed";
            updateStatusItem.Visible = true;
        }
        finally
        {
            updateChecking = false;
        }
    }

    private void ApplyCachedUpdateStatus()
    {
        if (!settings.UpdateCheck.Enabled)
        {
            updateStatusItem.Text = "Firmware update: auto-check off";
            updateStatusItem.Visible = true;
            return;
        }

        if (settings.UpdateCheck.LastUpdateAvailable && !string.IsNullOrWhiteSpace(settings.UpdateCheck.LastLatestVersion))
        {
            updateStatusItem.Text = $"Update available: {settings.UpdateCheck.LastLatestVersion}";
            updateStatusItem.Visible = true;
            return;
        }

        if (settings.UpdateCheck.LastCheckedAt != default)
        {
            updateStatusItem.Text = "Firmware update: up to date";
            updateStatusItem.Visible = false;
            return;
        }

        updateStatusItem.Text = "Firmware update: not checked yet";
        updateStatusItem.Visible = false;
    }

    private void ApplyUpdateStatus(UpdateCheckResult result)
    {
        updateStatusItem.Text = result.Status switch
        {
            FirmwareVersionStatus.UpdateAvailable when result.LatestRelease is not null => $"Update available: {result.LatestRelease.Tag}",
            FirmwareVersionStatus.UpToDate => "Firmware update: up to date",
            _ => "Firmware update: unknown"
        };
        updateStatusItem.Visible = result.Status != FirmwareVersionStatus.UpToDate;
    }

    private async Task CheckForAppUpdatesIfDueAsync(bool force = false)
    {
        if (appUpdateChecking || !settings.AppUpdateCheck.Enabled)
        {
            if (!settings.AppUpdateCheck.Enabled)
            {
                appUpdateStatusItem.Text = "App update: auto-check off";
                appUpdateStatusItem.Visible = true;
            }

            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!force && settings.AppUpdateCheck.LastCheckedAt != default &&
            now - settings.AppUpdateCheck.LastCheckedAt < TimeSpan.FromDays(1))
        {
            return;
        }

        appUpdateChecking = true;
        appUpdateStatusItem.Text = "App update: checking...";
        appUpdateStatusItem.Visible = true;
        try
        {
            var result = await new AppUpdater().CheckForUpdateAsync();
            settings.AppUpdateCheck.LastCheckedAt = now;
            settings.AppUpdateCheck.LastCurrentVersion = result.CurrentVersion;
            settings.AppUpdateCheck.LastLatestVersion = result.LatestRelease.Tag;
            settings.AppUpdateCheck.LastUpdateAvailable = result.IsUpdateAvailable;
            settings.Save();
            ApplyAppUpdateStatus(result);
        }
        catch
        {
            settings.AppUpdateCheck.LastCheckedAt = now;
            settings.AppUpdateCheck.LastUpdateAvailable = false;
            settings.Save();
            appUpdateStatusItem.Text = "App update: check failed";
            appUpdateStatusItem.Visible = true;
        }
        finally
        {
            appUpdateChecking = false;
        }
    }

    private void ApplyCachedAppUpdateStatus()
    {
        if (!settings.AppUpdateCheck.Enabled)
        {
            appUpdateStatusItem.Text = "App update: auto-check off";
            appUpdateStatusItem.Visible = true;
            return;
        }

        if (settings.AppUpdateCheck.LastUpdateAvailable && !string.IsNullOrWhiteSpace(settings.AppUpdateCheck.LastLatestVersion))
        {
            appUpdateStatusItem.Text = $"App update available: {settings.AppUpdateCheck.LastLatestVersion}";
            appUpdateStatusItem.Visible = true;
            return;
        }

        if (settings.AppUpdateCheck.LastCheckedAt != default)
        {
            appUpdateStatusItem.Text = "App update: up to date";
            appUpdateStatusItem.Visible = false;
            return;
        }

        appUpdateStatusItem.Text = "App update: not checked yet";
        appUpdateStatusItem.Visible = false;
    }

    private void ApplyAppUpdateStatus(AppUpdateCheckResult result)
    {
        appUpdateStatusItem.Text = result.Status switch
        {
            FirmwareVersionStatus.UpdateAvailable => $"App update available: {result.LatestRelease.Tag}",
            FirmwareVersionStatus.UpToDate => "App update: up to date",
            _ => "App update: unknown"
        };
        appUpdateStatusItem.Visible = result.Status != FirmwareVersionStatus.UpToDate;
    }

    private void ShowSettings()
    {
        try
        {
            if (settingsForm is { IsDisposed: false })
            {
                ActivateForm(settingsForm);
                return;
            }

            settingsForm = new SettingsForm(client, settings);
            settingsForm.FormClosed += async (_, _) =>
            {
                settingsForm = null;
                await RefreshAsync();
            };
            ActivateForm(settingsForm);
        }
        catch (Exception ex)
        {
            settingsForm = null;
            MessageBox.Show(
                ex.ToString(),
                "DS5Dongle Settings Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowFirmwareUpdate()
    {
        try
        {
            if (firmwareUpdateForm is { IsDisposed: false })
            {
                ActivateForm(firmwareUpdateForm);
                return;
            }

            firmwareUpdateForm = new FirmwareUpdateForm(new FirmwareUpdater(client));
            firmwareUpdateForm.FormClosed += async (_, _) =>
            {
                firmwareUpdateForm = null;
                await RefreshAsync();
            };
            ActivateForm(firmwareUpdateForm);
        }
        catch (Exception ex)
        {
            firmwareUpdateForm = null;
            MessageBox.Show(
                ex.ToString(),
                "DS5Dongle Firmware Update Failed",
                MessageBoxButtons.OK,
            MessageBoxIcon.Error);
        }
    }

    private void ShowAppUpdate()
    {
        try
        {
            if (appUpdateForm is { IsDisposed: false })
            {
                ActivateForm(appUpdateForm);
                return;
            }

            appUpdateForm = new AppUpdateForm(new AppUpdater());
            appUpdateForm.FormClosed += (_, _) => appUpdateForm = null;
            ActivateForm(appUpdateForm);
        }
        catch (Exception ex)
        {
            appUpdateForm = null;
            MessageBox.Show(
                ex.ToString(),
                "DS5DongleTray App Update Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void ActivateForm(Form form)
    {
        if (form.InvokeRequired)
        {
            form.BeginInvoke(new Action(() => ActivateForm(form)));
            return;
        }

        form.Show();
        form.WindowState = FormWindowState.Normal;
        form.TopMost = true;
        form.TopMost = false;
        form.Activate();
        form.BringToFront();
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
        appUpdateForm?.Close();
        timer.Stop();
        inputTimer.Stop();
        updateCheckTimer.Stop();
        timer.Dispose();
        inputTimer.Dispose();
        updateCheckTimer.Dispose();
        overlayManager.Dispose();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
        currentIcon?.Dispose();
        base.ExitThreadCore();
    }
}
