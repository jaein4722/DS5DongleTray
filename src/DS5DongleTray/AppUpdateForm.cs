using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class AppUpdateForm : Form
{
    private readonly AppUpdater updater;
    private readonly Label currentVersionLabel;
    private readonly Label latestVersionLabel;
    private readonly Label assetLabel;
    private readonly Label statusLabel;
    private readonly Button updateButton;
    private readonly Button downloadButton;
    private readonly Button openReleaseButton;
    private AppRelease? latestRelease;
    private FirmwareVersionStatus latestStatus = FirmwareVersionStatus.Unknown;

    public AppUpdateForm(AppUpdater updater)
    {
        this.updater = updater;

        Text = "DS5DongleTray App Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 250);

        currentVersionLabel = NewLabel($"Current app: {AppUpdater.CurrentVersion}", new Point(16, 18), new Size(488, 24));
        latestVersionLabel = NewLabel("Latest release: checking...", new Point(16, 48), new Size(488, 24));
        assetLabel = NewLabel("Asset: checking...", new Point(16, 78), new Size(488, 24));
        statusLabel = NewLabel("", new Point(16, 118), new Size(488, 50));

        updateButton = new Button { Text = "Update App", Location = new Point(16, 178), Size = new Size(104, 30), Enabled = false };
        downloadButton = new Button { Text = "Download", Location = new Point(128, 178), Size = new Size(92, 30), Enabled = false };
        openReleaseButton = new Button { Text = "Open Release", Location = new Point(228, 178), Size = new Size(108, 30), Enabled = false };
        var closeButton = new Button { Text = "Close", Location = new Point(424, 204), Size = new Size(80, 30) };

        updateButton.Click += async (_, _) => await UpdateAppAsync();
        downloadButton.Click += async (_, _) => await DownloadAppAsync();
        openReleaseButton.Click += (_, _) =>
        {
            if (latestRelease is not null)
            {
                AppUpdater.OpenReleasePage(latestRelease);
            }
        };
        closeButton.Click += (_, _) => Close();

        Controls.AddRange([
            currentVersionLabel,
            latestVersionLabel,
            assetLabel,
            statusLabel,
            updateButton,
            downloadButton,
            openReleaseButton,
            closeButton
        ]);

        Shown += async (_, _) => await CheckLatestAsync();
    }

    private async Task CheckLatestAsync()
    {
        SetBusy(true);
        try
        {
            var result = await updater.CheckForUpdateAsync();
            latestRelease = result.LatestRelease;
            latestStatus = result.Status;

            latestVersionLabel.Text = $"Latest release: {latestRelease.Tag}";
            assetLabel.Text = $"Asset: {latestRelease.Asset.Name}";
            statusLabel.Text = latestStatus == FirmwareVersionStatus.UpdateAvailable
                ? AppUpdater.CanSelfUpdate(out var reason)
                    ? "Update available. The app will restart after updating."
                    : $"Update available. Automatic replacement is unavailable: {reason}"
                : "Current app is already up to date.";

            openReleaseButton.Enabled = true;
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Could not check app releases: {ex.Message}";
            latestStatus = FirmwareVersionStatus.Unknown;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task UpdateAppAsync()
    {
        if (latestRelease is null)
        {
            return;
        }

        if (!AppUpdater.CanSelfUpdate(out var reason))
        {
            MessageBox.Show(this, reason, "App Update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            "DS5DongleTray will download the latest exe, close itself, replace the current exe, and restart. Continue?",
            "Update App",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        SetBusy(true);
        var progress = new Progress<string>(message => statusLabel.Text = message);

        try
        {
            var download = await updater.DownloadLatestAsync(latestRelease, useTempDirectory: true, progress);
            statusLabel.Text = "Starting updater...";
            AppUpdater.StartSelfUpdate(download.DownloadPath);
            Application.Exit();
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"App update failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "App Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            SetBusy(false);
        }
    }

    private async Task DownloadAppAsync()
    {
        if (latestRelease is null)
        {
            return;
        }

        SetBusy(true);
        var progress = new Progress<string>(message => statusLabel.Text = message);

        try
        {
            var download = await updater.DownloadLatestAsync(latestRelease, useTempDirectory: false, progress);
            statusLabel.Text = $"Downloaded to {download.DownloadPath}.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Download failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "App Download Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        var updateAvailable = latestRelease is not null && latestStatus == FirmwareVersionStatus.UpdateAvailable;
        updateButton.Enabled = !busy && updateAvailable && AppUpdater.CanSelfUpdate(out _);
        downloadButton.Enabled = !busy && updateAvailable;
        openReleaseButton.Enabled = !busy && latestRelease is not null;
    }

    private static Label NewLabel(string text, Point location, Size size)
    {
        return new Label
        {
            Text = text,
            Location = location,
            Size = size,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }
}
