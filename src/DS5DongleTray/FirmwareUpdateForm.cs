using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class FirmwareUpdateForm : Form
{
    private readonly FirmwareUpdater updater;
    private readonly Label currentFirmwareLabel;
    private readonly Label releaseLabel;
    private readonly Label assetLabel;
    private readonly Label statusLabel;
    private readonly TextBox firmwarePathTextBox;
    private readonly Button updateButton;
    private readonly Button browseFirmwareButton;
    private readonly Button installLocalFirmwareButton;
    private readonly Button enterBootloaderButton;
    private readonly Button openReleaseButton;
    private FirmwareRelease? latestRelease;
    private FirmwareVersionStatus latestStatus = FirmwareVersionStatus.Unknown;
    private bool firmwareUpdateSupported;
    private FirmwareReleaseChannel releaseChannel = FirmwareReleaseChannel.Official;

    public FirmwareUpdateForm(FirmwareUpdater updater)
    {
        this.updater = updater;

        Text = "DS5Dongle Firmware Update";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(520, 322);

        releaseLabel = NewLabel("Latest release: checking...", new Point(16, 18), new Size(488, 24));
        currentFirmwareLabel = NewLabel("Current firmware: checking...", new Point(16, 48), new Size(488, 24));
        assetLabel = NewLabel("Firmware asset: checking...", new Point(16, 78), new Size(488, 24));
        firmwarePathTextBox = new TextBox { Location = new Point(16, 112), Size = new Size(366, 24) };
        browseFirmwareButton = new Button { Text = "Browse...", Location = new Point(390, 110), Size = new Size(114, 30), Enabled = false };
        statusLabel = NewLabel("", new Point(16, 154), new Size(488, 58));

        updateButton = new Button { Text = "Update Latest", Location = new Point(16, 226), Size = new Size(112, 30), Enabled = false };
        installLocalFirmwareButton = new Button { Text = "Install Local UF2", Location = new Point(136, 226), Size = new Size(122, 30), Enabled = false };
        enterBootloaderButton = new Button { Text = "Enter Bootloader", Location = new Point(266, 226), Size = new Size(122, 30), Enabled = false };
        openReleaseButton = new Button { Text = "Open Release", Location = new Point(396, 226), Size = new Size(108, 30), Enabled = false };
        var closeButton = new Button { Text = "Close", Location = new Point(424, 278), Size = new Size(80, 30) };

        updateButton.Click += async (_, _) => await UpdateFirmwareAsync();
        browseFirmwareButton.Click += (_, _) => BrowseFirmwareFile();
        installLocalFirmwareButton.Click += async (_, _) => await InstallLocalFirmwareAsync();
        enterBootloaderButton.Click += async (_, _) => await EnterBootloaderAsync();
        openReleaseButton.Click += (_, _) =>
        {
            if (latestRelease is not null)
            {
                FirmwareUpdater.OpenReleasePage(latestRelease);
            }
        };
        closeButton.Click += (_, _) => Close();

        Controls.AddRange([
            releaseLabel,
            currentFirmwareLabel,
            assetLabel,
            firmwarePathTextBox,
            browseFirmwareButton,
            statusLabel,
            closeButton,
            updateButton,
            installLocalFirmwareButton,
            enterBootloaderButton,
            openReleaseButton
        ]);
        Shown += async (_, _) => await CheckLatestAsync();
    }

    private async Task CheckLatestAsync()
    {
        try
        {
            var currentFirmware = await updater.GetCurrentFirmwareVersionAsync();
            firmwareUpdateSupported = currentFirmware?.Contains("-custom", StringComparison.OrdinalIgnoreCase) == true;
            releaseChannel = FirmwareUpdater.GetChannelForFirmware(currentFirmware);
            latestRelease = await updater.GetLatestReleaseAsync(releaseChannel);
            latestStatus = FirmwareUpdater.GetVersionStatus(currentFirmware, latestRelease);

            releaseLabel.Text = $"Latest release: {latestRelease.Tag} ({latestRelease.Repository})";
            currentFirmwareLabel.Text = $"Current firmware: {currentFirmware ?? "not detected"}";
            assetLabel.Text = $"Firmware asset: {latestRelease.Asset.Name}";
            updateButton.Text = firmwareUpdateSupported ? "Update Latest" : "Download Latest";

            if (!firmwareUpdateSupported)
            {
                statusLabel.Text = latestStatus == FirmwareVersionStatus.UpdateAvailable
                    ? "Official update available. Download only; flash manually with BOOTSEL."
                    : latestStatus == FirmwareVersionStatus.UpToDate
                        ? "Current official firmware is already up to date."
                        : "Current firmware could not be checked. Official release download is available only after detection.";
            }
            else if (latestStatus == FirmwareVersionStatus.UpToDate)
            {
                statusLabel.Text = "Current firmware is already up to date.";
            }
            else
            {
                statusLabel.Text = latestStatus == FirmwareVersionStatus.Unknown
                    ? "Current firmware could not be checked. Connect DS5Dongle before updating."
                    : "Update available. The dongle will reboot into UF2 mode.";
            }

            openReleaseButton.Enabled = true;
            SetBusy(false);
        }
        catch (Exception ex)
        {
            latestStatus = FirmwareVersionStatus.Unknown;
            firmwareUpdateSupported = false;
            currentFirmwareLabel.Text = "Current firmware: unknown";
            statusLabel.Text = $"Could not check releases: {ex.Message}";
            browseFirmwareButton.Enabled = false;
            installLocalFirmwareButton.Enabled = false;
            enterBootloaderButton.Enabled = false;
        }
    }

    private async Task UpdateFirmwareAsync()
    {
        var confirmMessage = firmwareUpdateSupported
            ? "The dongle will reboot into UF2 bootloader mode, then the app will copy the latest firmware to it. Continue?"
            : "The latest official firmware will be downloaded to your Downloads folder. Flashing is not automated for original firmware. Continue?";
        var confirm = MessageBox.Show(this, confirmMessage, "Firmware Update", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);

        if (confirm != DialogResult.OK)
        {
            return;
        }

        SetBusy(true);
        var progress = new Progress<string>(message => statusLabel.Text = message);

        try
        {
            if (firmwareUpdateSupported)
            {
                var result = await updater.DownloadAndInstallLatestAsync(releaseChannel, progress);
                statusLabel.Text = $"Updated with {result.Release.Asset.Name}.";
            }
            else
            {
                var result = await updater.DownloadLatestAsync(releaseChannel, progress);
                firmwarePathTextBox.Text = result.DownloadPath;
                statusLabel.Text = $"Downloaded {result.Release.Asset.Name}. Flash manually with BOOTSEL.";
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Update failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Firmware Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseFirmwareFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select DS5Dongle UF2 Firmware",
            Filter = "UF2 firmware (*.uf2)|*.uf2|All files (*.*)|*.*",
            DefaultExt = "uf2",
            AddExtension = true,
            AutoUpgradeEnabled = false,
            CheckFileExists = true,
            DereferenceLinks = false,
            InitialDirectory = GetInitialFirmwareDirectory(),
            Multiselect = false,
            RestoreDirectory = true,
            ValidateNames = true
        };

        statusLabel.Text = "Opening file picker...";
        try
        {
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                firmwarePathTextBox.Text = dialog.FileName;
                statusLabel.Text = "Ready to install selected UF2.";
            }
            else
            {
                statusLabel.Text = "File selection canceled.";
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"File picker failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "File Picker Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task InstallLocalFirmwareAsync()
    {
        var firmwarePath = firmwarePathTextBox.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(firmwarePath))
        {
            MessageBox.Show(this, "Select a UF2 file or paste its full path first.", "Local Firmware", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"The dongle will reboot into UF2 bootloader mode, then the app will copy this firmware file to it:\n\n{firmwarePath}\n\nContinue?",
            "Install Selected Firmware",
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
            var destination = await updater.InstallLocalFirmwareAsync(firmwarePath, progress);
            statusLabel.Text = $"Installed {Path.GetFileName(firmwarePath)} to {destination}.";
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Manual update failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Manual Firmware Update Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task EnterBootloaderAsync()
    {
        var confirm = MessageBox.Show(
            this,
            "The dongle will reboot into UF2 bootloader mode without copying firmware.\n\nDS5Dongle will stop working until you copy a UF2 firmware file or unplug and reconnect the Pico. Continue?",
            "Enter Bootloader",
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
            await updater.EnterBootloaderOnlyAsync(progress);
        }
        catch (Exception ex)
        {
            statusLabel.Text = $"Bootloader entry failed: {ex.Message}";
            MessageBox.Show(this, ex.Message, "Bootloader Entry Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        updateButton.Enabled = !busy && latestRelease is not null && latestStatus == FirmwareVersionStatus.UpdateAvailable;
        browseFirmwareButton.Enabled = !busy && firmwareUpdateSupported;
        installLocalFirmwareButton.Enabled = !busy && firmwareUpdateSupported;
        enterBootloaderButton.Enabled = !busy && firmwareUpdateSupported;
        openReleaseButton.Enabled = !busy && latestRelease is not null;
    }

    private static string GetInitialFirmwareDirectory()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
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
