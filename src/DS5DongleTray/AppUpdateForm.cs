using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class AppUpdateForm : Form
{
    private readonly AppUpdater updater;
    private readonly Label currentLabel;
    private readonly Label releaseLabel;
    private readonly Label assetLabel;
    private readonly Label statusLabel;
    private readonly Button updateButton;
    private readonly Button downloadButton;
    private readonly Button openReleaseButton;
    private AppRelease? latestRelease;
    private bool updateAvailable;

    public AppUpdateForm(AppUpdater updater)
    {
        this.updater = updater;

        Text = "DS5DongleTray App Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 238);

        currentLabel = NewLabel($"Current app: {AppUpdater.CurrentVersion}", new Point(16, 18), new Size(488, 24));
        releaseLabel = NewLabel("Latest release: checking...", new Point(16, 48), new Size(488, 24));
        assetLabel = NewLabel("App asset: checking...", new Point(16, 78), new Size(488, 24));
        statusLabel = NewLabel("", new Point(16, 116), new Size(488, 42));

        updateButton = new Button { Text = "Update App", Location = new Point(16, 174), Size = new Size(112, 30), Enabled = false };
        downloadButton = new Button { Text = "Download", Location = new Point(136, 174), Size = new Size(112, 30), Enabled = false };
        openReleaseButton = new Button { Text = "Open Release", Location = new Point(256, 174), Size = new Size(112, 30), Enabled = false };
        var closeButton = new Button { Text = "Close", Location = new Point(424, 174), Size = new Size(80, 30) };

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
            currentLabel,
            releaseLabel,
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
            updateAvailable = result.IsUpdateAvailable;
            currentLabel.Text = $"Current app: {result.CurrentVersion}";
            releaseLabel.Text = $"Latest release: {latestRelease.Tag}";
            assetLabel.Text = $"App asset: {latestRelease.Asset.Name}";

            if (updateAvailable)
            {
                statusLabel.Text = "App update available.";
            }
            else
            {
                statusLabel.Text = "DS5DongleTray is already up to date.";
            }
        }
        catch (Exception ex)
        {
            latestRelease = null;
            updateAvailable = false;
            statusLabel.Text = $"Could not check app releases: {ex.Message}";
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
            var result = await updater.DownloadLatestAsync(latestRelease, useTempDirectory: true, progress);
            statusLabel.Text = "Restarting to complete update...";
            AppUpdater.StartSelfUpdate(result.DownloadPath);
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
            var result = await updater.DownloadLatestAsync(latestRelease, useTempDirectory: false, progress);
            statusLabel.Text = $"Downloaded to {result.DownloadPath}.";
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
        updateButton.Enabled = !busy && latestRelease is not null && updateAvailable;
        downloadButton.Enabled = !busy && latestRelease is not null;
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
