using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class SettingsForm : Form
{
    private const string BuiltInDefaultProfileName = "Default";
    private static readonly IReadOnlyDictionary<string, DongleConfig> BuiltInProfiles =
        new Dictionary<string, DongleConfig>(StringComparer.OrdinalIgnoreCase)
        {
            [BuiltInDefaultProfileName] = DongleConfig.WebConfigDefault
        };

    private readonly DongleHidClient client;
    private readonly AppSettings appSettings;
    private readonly SliderInput hapticsGainInput;
    private readonly SliderInput speakerVolumeInput;
    private readonly SliderInput inactiveTimeInput;
    private readonly SliderInput audioBufferInput;
    private readonly CheckBox overlayLowBatteryInput;
    private readonly CheckBox overlayPsButtonInput;
    private readonly CheckBox overlayChargingChangedInput;
    private readonly CheckBox overlayTrayLeftClickInput;
    private readonly NumericUpDown overlayLowBatteryThresholdInput;
    private readonly NumericUpDown overlayDisplaySecondsInput;
    private readonly CheckBox startWithWindowsInput;
    private readonly CheckBox automaticUpdateCheckInput;
    private readonly CheckBox automaticAppUpdateCheckInput;
    private readonly Label startupStatusLabel;
    private readonly ComboBox profileInput;
    private readonly CheckBox disableInactiveDisconnectInput;
    private readonly CheckBox disablePicoLedInput;
    private readonly ComboBox pollingRateInput;
    private readonly ComboBox controllerModeInput;
    private readonly Label statusLabel;
    private DongleConfig? currentConfig;
    private DongleConfig? lastSavedConfig;
    private bool loading;
    private bool loadingOverlaySettings;

    public SettingsForm(DongleHidClient client, AppSettings appSettings)
    {
        this.client = client;
        this.appSettings = appSettings;

        Text = "DS5Dongle Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(664, 568);

        hapticsGainInput = new SliderInput("Haptics gain", 1.00m, 2.00m, 0.01m, 2);
        speakerVolumeInput = new SliderInput("Speaker volume (%)", 0m, 100m, 1m, 0);
        audioBufferInput = new SliderInput("Haptics buffer length", 16m, 128m, 1m, 0);
        inactiveTimeInput = new SliderInput("Inactive time (min)", 5m, 60m, 1m, 0);
        disableInactiveDisconnectInput = new CheckBox { Text = "Disable inactive disconnect", AutoSize = true };
        disablePicoLedInput = new CheckBox { Text = "Disable Pico LED", AutoSize = true };
        pollingRateInput = NewComboBox(["250 Hz", "500 Hz", "Real-time"]);
        controllerModeInput = NewComboBox(["DualSense", "DualSense Edge", "Auto"]);

        overlayLowBatteryInput = new CheckBox { Text = "Show overlay when battery is low", AutoSize = true };
        overlayPsButtonInput = new CheckBox { Text = "Show overlay when PS button is pressed", AutoSize = true };
        overlayChargingChangedInput = new CheckBox { Text = "Show overlay when charging state changes", AutoSize = true };
        overlayTrayLeftClickInput = new CheckBox { Text = "Show overlay on tray icon left-click", AutoSize = true };
        overlayLowBatteryThresholdInput = NewNumericInput(10, 50, 10);
        overlayDisplaySecondsInput = NewNumericInput(1, 10, 1);
        startWithWindowsInput = new CheckBox { Text = "Start DS5DongleTray when I sign in to Windows", AutoSize = true };
        automaticUpdateCheckInput = new CheckBox { Text = "Check for firmware updates automatically", AutoSize = true };
        automaticAppUpdateCheckInput = new CheckBox { Text = "Check for app updates automatically", AutoSize = true };
        startupStatusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(20, 118),
            Size = new Size(570, 64),
            TextAlign = ContentAlignment.MiddleLeft
        };
        profileInput = NewComboBox([]);
        profileInput.DropDownStyle = ComboBoxStyle.DropDown;

        var tabs = new TabControl { Location = new Point(12, 12), Size = new Size(640, 504) };
        var generalPage = new TabPage("General");
        var firmwarePage = new TabPage("Firmware");
        var overlayPage = new TabPage("Overlay");
        tabs.TabPages.AddRange([generalPage, firmwarePage, overlayPage]);

        var feedbackGroup = NewGroup("Feedback output", new Point(16, 16), new Size(296, 190));
        hapticsGainInput.Location = new Point(14, 28);
        speakerVolumeInput.Location = new Point(14, 82);
        audioBufferInput.Location = new Point(14, 136);
        feedbackGroup.Controls.AddRange([hapticsGainInput, speakerVolumeInput, audioBufferInput]);

        var powerGroup = NewGroup("Power && indicators", new Point(328, 16), new Size(296, 190));
        inactiveTimeInput.Location = new Point(14, 28);
        disableInactiveDisconnectInput.Location = new Point(14, 98);
        disablePicoLedInput.Location = new Point(14, 136);
        powerGroup.Controls.AddRange([inactiveTimeInput, disableInactiveDisconnectInput, disablePicoLedInput]);

        var performanceGroup = NewGroup("Performance", new Point(16, 222), new Size(296, 94));
        var pollingLabel = NewLabel("Polling rate mode", new Point(14, 26), new Size(120, 22));
        pollingRateInput.Location = new Point(140, 24);
        performanceGroup.Controls.AddRange([pollingLabel, pollingRateInput]);

        var compatibilityGroup = NewGroup("Compatibility", new Point(328, 222), new Size(296, 94));
        var controllerLabel = NewLabel("Controller mode", new Point(14, 26), new Size(120, 22));
        controllerModeInput.Location = new Point(140, 24);
        compatibilityGroup.Controls.AddRange([controllerLabel, controllerModeInput]);

        var profilesGroup = NewGroup("Profiles", new Point(16, 332), new Size(608, 72));
        var profileLabel = NewLabel("Preset", new Point(14, 30), new Size(54, 22));
        profileInput.Location = new Point(72, 28);
        profileInput.Width = 178;
        var loadProfileButton = new Button { Text = "Load", Location = new Point(260, 26), Size = new Size(70, 28) };
        var saveProfileButton = new Button { Text = "Save current", Location = new Point(338, 26), Size = new Size(96, 28) };
        var deleteProfileButton = new Button { Text = "Delete", Location = new Point(442, 26), Size = new Size(70, 28) };
        profilesGroup.Controls.AddRange([profileLabel, profileInput, loadProfileButton, saveProfileButton, deleteProfileButton]);

        var refreshButton = new Button { Text = "Refresh", Location = new Point(16, 418), Size = new Size(76, 30) };
        var applyButton = new Button { Text = "Apply", Location = new Point(98, 418), Size = new Size(76, 30) };
        var saveButton = new Button { Text = "Save", Location = new Point(180, 418), Size = new Size(76, 30) };
        var reconnectButton = new Button { Text = "Reconnect USB", Location = new Point(262, 418), Size = new Size(112, 30) };
        var closeButton = new Button { Text = "Close", Location = new Point(572, 526), Size = new Size(76, 30) };

        statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(388, 418),
            Size = new Size(520, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };

        BuildGeneralTab(generalPage);
        BuildOverlayTab(overlayPage);

        refreshButton.Click += async (_, _) => await LoadConfigAsync();
        applyButton.Click += async (_, _) => await ApplyConfigAsync();
        saveButton.Click += async (_, _) => await SaveConfigAsync();
        reconnectButton.Click += async (_, _) => await ReconnectUsbAsync();
        loadProfileButton.Click += (_, _) => LoadSelectedProfile();
        saveProfileButton.Click += (_, _) => SaveCurrentProfile();
        deleteProfileButton.Click += (_, _) => DeleteSelectedProfile();
        closeButton.Click += (_, _) => Close();

        firmwarePage.Controls.AddRange([
            feedbackGroup,
            powerGroup,
            performanceGroup,
            compatibilityGroup,
            profilesGroup,
            refreshButton,
            applyButton,
            saveButton,
            reconnectButton,
            statusLabel
        ]);
        Controls.AddRange([tabs, closeButton]);

        LoadGeneralSettingsToControls();
        LoadOverlaySettingsToControls();
        RefreshProfileList();

        Shown += async (_, _) => await LoadConfigAsync();
    }

    private void BuildGeneralTab(Control generalPage)
    {
        var startupGroup = NewGroup("Windows startup", new Point(16, 16), new Size(592, 166));
        startWithWindowsInput.Location = new Point(18, 32);
        automaticUpdateCheckInput.Location = new Point(18, 64);
        automaticAppUpdateCheckInput.Location = new Point(18, 94);
        startupGroup.Controls.AddRange([startWithWindowsInput, automaticUpdateCheckInput, automaticAppUpdateCheckInput, startupStatusLabel]);

        var todoGroup = NewGroup("Planned", new Point(16, 198), new Size(592, 110));
        var todoLabel = new Label
        {
            Text = "TODO: Audio Guard / Audio Check for detecting DS5Dongle speaker and microphone default-device issues.",
            Location = new Point(18, 34),
            Size = new Size(552, 44),
            TextAlign = ContentAlignment.MiddleLeft
        };
        todoGroup.Controls.Add(todoLabel);

        startWithWindowsInput.CheckedChanged += (_, _) => SaveGeneralSettingsFromControls();
        automaticUpdateCheckInput.CheckedChanged += (_, _) => SaveGeneralSettingsFromControls();
        automaticAppUpdateCheckInput.CheckedChanged += (_, _) => SaveGeneralSettingsFromControls();
        generalPage.Controls.AddRange([startupGroup, todoGroup]);
    }

    private void LoadGeneralSettingsToControls()
    {
        startWithWindowsInput.Checked = StartupManager.IsEnabled();
        automaticUpdateCheckInput.Checked = appSettings.UpdateCheck.Enabled;
        automaticAppUpdateCheckInput.Checked = appSettings.AppUpdateCheck.Enabled;
        UpdateStartupStatus();
    }

    private void SaveGeneralSettingsFromControls()
    {
        try
        {
            StartupManager.SetEnabled(startWithWindowsInput.Checked);
            appSettings.UpdateCheck.Enabled = automaticUpdateCheckInput.Checked;
            appSettings.AppUpdateCheck.Enabled = automaticAppUpdateCheckInput.Checked;
            appSettings.Save();
            UpdateStartupStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Windows Startup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            startWithWindowsInput.Checked = StartupManager.IsEnabled();
            UpdateStartupStatus();
        }
    }

    private void UpdateStartupStatus()
    {
        startupStatusLabel.Text = startWithWindowsInput.Checked
            ? $"Enabled for current user.\nCommand: {StartupManager.CurrentCommand()}"
            : "Disabled. This setting uses the current user's Windows Run registry key and does not require administrator rights.";
    }

    private void BuildOverlayTab(Control overlayPage)
    {
        var triggersGroup = NewGroup("Overlay triggers", new Point(16, 16), new Size(592, 166));
        overlayLowBatteryInput.Location = new Point(18, 30);
        overlayPsButtonInput.Location = new Point(18, 62);
        overlayChargingChangedInput.Location = new Point(18, 94);
        overlayTrayLeftClickInput.Location = new Point(18, 126);
        triggersGroup.Controls.AddRange([
            overlayLowBatteryInput,
            overlayPsButtonInput,
            overlayChargingChangedInput,
            overlayTrayLeftClickInput
        ]);

        var behaviorGroup = NewGroup("Overlay behavior", new Point(16, 198), new Size(592, 116));
        var thresholdLabel = NewLabel("Low battery threshold (%)", new Point(18, 32), new Size(180, 22));
        var durationLabel = NewLabel("Display duration (sec)", new Point(18, 72), new Size(180, 22));
        overlayLowBatteryThresholdInput.Location = new Point(212, 30);
        overlayDisplaySecondsInput.Location = new Point(212, 70);
        behaviorGroup.Controls.AddRange([
            thresholdLabel,
            overlayLowBatteryThresholdInput,
            durationLabel,
            overlayDisplaySecondsInput
        ]);

        var hintLabel = new Label
        {
            Text = "Overlay settings are saved immediately. PS button overlay uses lightweight input-report polling and can be disabled here.",
            Location = new Point(20, 334),
            Size = new Size(580, 48),
            TextAlign = ContentAlignment.MiddleLeft
        };

        overlayLowBatteryInput.CheckedChanged += (_, _) => SaveOverlaySettingsFromControls();
        overlayPsButtonInput.CheckedChanged += (_, _) => SaveOverlaySettingsFromControls();
        overlayChargingChangedInput.CheckedChanged += (_, _) => SaveOverlaySettingsFromControls();
        overlayTrayLeftClickInput.CheckedChanged += (_, _) => SaveOverlaySettingsFromControls();
        overlayLowBatteryThresholdInput.ValueChanged += (_, _) => SaveOverlaySettingsFromControls();
        overlayDisplaySecondsInput.ValueChanged += (_, _) => SaveOverlaySettingsFromControls();

        overlayPage.Controls.AddRange([triggersGroup, behaviorGroup, hintLabel]);
    }

    private void LoadOverlaySettingsToControls()
    {
        loadingOverlaySettings = true;
        try
        {
            var overlay = appSettings.Overlay;
            overlayLowBatteryInput.Checked = overlay.ShowOnLowBattery;
            overlayPsButtonInput.Checked = overlay.ShowOnPsButton;
            overlayChargingChangedInput.Checked = overlay.ShowOnChargingStateChanged;
            overlayTrayLeftClickInput.Checked = overlay.ShowOnTrayLeftClick;
            overlayLowBatteryThresholdInput.Value = overlay.ClampedLowBatteryThreshold;
            overlayDisplaySecondsInput.Value = overlay.ClampedDisplaySeconds;
        }
        finally
        {
            loadingOverlaySettings = false;
        }
    }

    private void SaveOverlaySettingsFromControls()
    {
        if (loadingOverlaySettings)
        {
            return;
        }

        appSettings.Overlay.ShowOnLowBattery = overlayLowBatteryInput.Checked;
        appSettings.Overlay.ShowOnPsButton = overlayPsButtonInput.Checked;
        appSettings.Overlay.ShowOnChargingStateChanged = overlayChargingChangedInput.Checked;
        appSettings.Overlay.ShowOnTrayLeftClick = overlayTrayLeftClickInput.Checked;
        appSettings.Overlay.LowBatteryThresholdPercent = (int)overlayLowBatteryThresholdInput.Value;
        appSettings.Overlay.DisplaySeconds = (int)overlayDisplaySecondsInput.Value;
        appSettings.Save();
    }

    private void RefreshProfileList(string? selectedName = null)
    {
        var names = BuiltInProfiles.Keys
            .Concat(appSettings.Profiles.Keys.Where(name => !BuiltInProfiles.ContainsKey(name)))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        profileInput.Items.Clear();
        profileInput.Items.AddRange(names);

        if (names.Length == 0)
        {
            profileInput.SelectedIndex = -1;
            profileInput.Text = "";
            return;
        }

        var index = !string.IsNullOrWhiteSpace(selectedName)
            ? Array.FindIndex(names, name => string.Equals(name, selectedName, StringComparison.OrdinalIgnoreCase))
            : 0;
        profileInput.SelectedIndex = index >= 0 ? index : 0;
    }

    private void LoadSelectedProfile()
    {
        var name = SelectedProfileName();
        if (string.IsNullOrWhiteSpace(name) || !TryGetProfile(name, out var config))
        {
            MessageBox.Show(this, "Select a saved profile first.", "Profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        currentConfig = config;
        ApplyConfigToControls(config);
        statusLabel.Text = $"Profile loaded: {name}";
    }

    private void SaveCurrentProfile()
    {
        using var dialog = new ProfileNameForm(SelectedProfileName());
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ClampInputs();
        var name = dialog.ProfileName;
        if (BuiltInProfiles.ContainsKey(name))
        {
            MessageBox.Show(this, "Built-in profiles cannot be overwritten. Choose a different name.", "Profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        appSettings.Profiles[name] = ReadConfigFromControls();
        appSettings.Save();
        RefreshProfileList(name);
        statusLabel.Text = $"Profile saved: {name}";
    }

    private void DeleteSelectedProfile()
    {
        var name = SelectedProfileName();
        if (BuiltInProfiles.ContainsKey(name))
        {
            MessageBox.Show(this, "Built-in profiles cannot be deleted.", "Profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (string.IsNullOrWhiteSpace(name) || !appSettings.Profiles.ContainsKey(name))
        {
            MessageBox.Show(this, "Select a saved profile first.", "Profiles", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Delete profile '{name}'?",
            "Delete Profile",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (result != DialogResult.Yes)
        {
            return;
        }

        appSettings.Profiles.Remove(name);
        appSettings.Save();
        RefreshProfileList();
        statusLabel.Text = $"Profile deleted: {name}";
    }

    private string SelectedProfileName()
    {
        return profileInput.SelectedItem as string ?? profileInput.Text.Trim();
    }

    private bool TryGetProfile(string name, out DongleConfig config)
    {
        return BuiltInProfiles.TryGetValue(name, out config!)
            || appSettings.Profiles.TryGetValue(name, out config!);
    }

    private async Task LoadConfigAsync()
    {
        await RunUiTaskAsync("Loading config...", async () =>
        {
            var config = await client.ReadConfigAsync();
            if (config is null)
            {
                throw new InvalidOperationException("Config is unavailable or unsupported.");
            }

            currentConfig = config;
            lastSavedConfig = config;
            ApplyConfigToControls(config);
            statusLabel.Text = "Config loaded.";
        });
    }

    private async Task ApplyConfigAsync()
    {
        await RunUiTaskAsync("Applying config...", async () =>
        {
            ClampInputs();
            var config = ReadConfigFromControls();
            await client.ApplyConfigAsync(config);
            currentConfig = config;
            statusLabel.Text = RequiresUsbReconnect(config, lastSavedConfig)
                ? "Applied to RAM. Reconnect USB is required for polling/controller changes."
                : "Applied to RAM.";
        });
    }

    private async Task SaveConfigAsync()
    {
        await RunUiTaskAsync("Saving config...", async () =>
        {
            ClampInputs();
            var config = ReadConfigFromControls();
            var reconnectRequired = RequiresUsbReconnect(config, lastSavedConfig);

            await client.ApplyConfigAsync(config);
            await client.SaveConfigAsync();
            currentConfig = config;
            lastSavedConfig = config;
            statusLabel.Text = reconnectRequired
                ? "Saved to flash. Reconnect USB is required for polling/controller changes."
                : "Saved to flash.";

            if (reconnectRequired)
            {
                BeginInvoke(new Action(() => PromptReconnectUsbAfterSave()));
            }
        });
    }

    private async Task ReconnectUsbAsync()
    {
        await RunUiTaskAsync("Reconnecting USB...", async () =>
        {
            await client.ReconnectUsbAsync();
            statusLabel.Text = "USB reconnect requested.";
        });
    }

    private async Task RunUiTaskAsync(string status, Func<Task> task)
    {
        if (loading)
        {
            return;
        }

        loading = true;
        Enabled = false;
        statusLabel.Text = status;

        try
        {
            await task();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "DS5DongleTray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            statusLabel.Text = "Failed.";
        }
        finally
        {
            Enabled = true;
            loading = false;
        }
    }

    private void ApplyConfigToControls(DongleConfig config)
    {
        hapticsGainInput.Value = (decimal)config.HapticsGain;
        speakerVolumeInput.Value = DbToPercent(config.SpeakerVolume);
        inactiveTimeInput.Value = config.InactiveTimeMinutes;
        disableInactiveDisconnectInput.Checked = config.DisableInactiveDisconnect;
        disablePicoLedInput.Checked = config.DisablePicoLed;
        pollingRateInput.SelectedIndex = config.PollingRateMode <= 2 ? config.PollingRateMode : 0;
        audioBufferInput.Value = config.AudioBufferLength;
        controllerModeInput.SelectedIndex = config.ControllerMode <= 2 ? config.ControllerMode : 2;
        ClampInputs();
    }

    private DongleConfig ReadConfigFromControls()
    {
        var baseConfig = currentConfig ?? DongleConfig.WebConfigDefault;

        return baseConfig with
        {
            HapticsGain = (float)hapticsGainInput.Value,
            SpeakerVolume = PercentToDb(speakerVolumeInput.Value),
            InactiveTimeMinutes = (byte)inactiveTimeInput.Value,
            DisableInactiveDisconnect = disableInactiveDisconnectInput.Checked,
            DisablePicoLed = disablePicoLedInput.Checked,
            PollingRateMode = (byte)pollingRateInput.SelectedIndex,
            AudioBufferLength = (byte)audioBufferInput.Value,
            ControllerMode = (byte)controllerModeInput.SelectedIndex
        };
    }

    private void ClampInputs()
    {
        hapticsGainInput.Clamp();
        speakerVolumeInput.Clamp();
        inactiveTimeInput.Clamp();
        audioBufferInput.Clamp();
    }

    private async void PromptReconnectUsbAfterSave()
    {
        var result = MessageBox.Show(
            this,
            "Polling rate mode or controller mode changed. Windows will not observe this change until USB is reconnected.\n\nReconnect USB now?",
            "Reconnect USB Required",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            await ReconnectUsbAsync();
        }
    }

    private static bool RequiresUsbReconnect(DongleConfig config, DongleConfig? previousConfig)
    {
        return previousConfig is not null
            && (config.PollingRateMode != previousConfig.PollingRateMode
                || config.ControllerMode != previousConfig.ControllerMode);
    }

    private static decimal DbToPercent(float db)
    {
        if (db <= -100.0f)
        {
            return 0m;
        }

        var percent = Math.Pow(10.0, db / 20.0) * 100.0;
        return ClampDecimal((decimal)percent, 0m, 100m);
    }

    private static float PercentToDb(decimal percent)
    {
        var clamped = ClampDecimal(percent, 0m, 100m);
        if (clamped <= 0m)
        {
            return -100.0f;
        }

        return (float)(20.0 * Math.Log10((double)clamped / 100.0));
    }

    private static GroupBox NewGroup(string text, Point location, Size size)
    {
        return new GroupBox
        {
            Text = text,
            Location = location,
            Size = size
        };
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

    private static ComboBox NewComboBox(string[] items)
    {
        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 134
        };
        comboBox.Items.AddRange(items);
        if (items.Length > 0)
        {
            comboBox.SelectedIndex = 0;
        }

        return comboBox;
    }

    private static NumericUpDown NewNumericInput(int min, int max, int increment)
    {
        return new NumericUpDown
        {
            Minimum = min,
            Maximum = max,
            Increment = increment,
            Width = 86
        };
    }

    private static decimal ClampDecimal(decimal value, decimal min, decimal max)
    {
        return Math.Min(Math.Max(value, min), max);
    }
}

internal sealed class SliderInput : UserControl
{
    private readonly decimal min;
    private readonly decimal max;
    private readonly decimal scale;
    private readonly TrackBar trackBar;
    private readonly NumericUpDown numericInput;
    private bool updating;

    public SliderInput(string labelText, decimal min, decimal max, decimal increment, int decimalPlaces)
    {
        this.min = min;
        this.max = max;
        scale = decimalPlaces == 0 ? 1m : (decimal)Math.Pow(10, decimalPlaces);

        Size = new Size(268, 48);

        var label = new Label
        {
            Text = labelText,
            Location = new Point(0, 0),
            Size = new Size(150, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        trackBar = new TrackBar
        {
            Location = new Point(0, 22),
            Size = new Size(178, 28),
            Minimum = 0,
            Maximum = DecimalToTrack(max),
            TickStyle = TickStyle.None,
            SmallChange = Math.Max(1, IncrementToTrack(increment)),
            LargeChange = Math.Max(1, IncrementToTrack(increment) * 5)
        };

        numericInput = new NumericUpDown
        {
            Location = new Point(184, 20),
            Size = new Size(76, 23),
            Minimum = min,
            Maximum = max,
            Increment = increment,
            DecimalPlaces = decimalPlaces
        };

        trackBar.Scroll += (_, _) => SyncFromTrackBar();
        numericInput.ValueChanged += (_, _) => SyncFromNumeric();
        numericInput.Leave += (_, _) => Clamp();

        Controls.AddRange([label, trackBar, numericInput]);
        Value = min;
    }

    public decimal Value
    {
        get => numericInput.Value;
        set => SetValue(value);
    }

    public void Clamp()
    {
        SetValue(numericInput.Value);
    }

    private void SetValue(decimal value)
    {
        var clamped = Math.Min(Math.Max(value, min), max);
        if (updating)
        {
            return;
        }

        updating = true;
        numericInput.Value = clamped;
        trackBar.Value = DecimalToTrack(clamped);
        updating = false;
    }

    private void SyncFromTrackBar()
    {
        if (updating)
        {
            return;
        }

        updating = true;
        numericInput.Value = TrackToDecimal(trackBar.Value);
        updating = false;
    }

    private void SyncFromNumeric()
    {
        if (updating)
        {
            return;
        }

        SetValue(numericInput.Value);
    }

    private int DecimalToTrack(decimal value)
    {
        return (int)Math.Round((value - min) * scale, MidpointRounding.AwayFromZero);
    }

    private int IncrementToTrack(decimal increment)
    {
        return (int)Math.Round(increment * scale, MidpointRounding.AwayFromZero);
    }

    private decimal TrackToDecimal(int value)
    {
        return Math.Min(Math.Max(min + value / scale, min), max);
    }
}

internal sealed class ProfileNameForm : Form
{
    private readonly TextBox nameInput;

    public ProfileNameForm(string? currentName)
    {
        Text = "Save Profile";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(360, 132);

        var label = new Label
        {
            Text = "Profile name",
            Location = new Point(16, 18),
            Size = new Size(328, 22),
            TextAlign = ContentAlignment.MiddleLeft
        };

        nameInput = new TextBox
        {
            Location = new Point(16, 44),
            Size = new Size(328, 24),
            Text = currentName ?? ""
        };

        var okButton = new Button { Text = "Save", Location = new Point(176, 88), Size = new Size(80, 30), DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", Location = new Point(264, 88), Size = new Size(80, 30), DialogResult = DialogResult.Cancel };

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange([label, nameInput, okButton, cancelButton]);
    }

    public string ProfileName => nameInput.Text.Trim();

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(ProfileName))
        {
            MessageBox.Show(this, "Enter a profile name.", "Save Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }
}
