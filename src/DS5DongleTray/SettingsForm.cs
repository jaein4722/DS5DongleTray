using System.Drawing;
using System.Windows.Forms;

namespace DS5DongleTray;

internal sealed class SettingsForm : Form
{
    private readonly DongleHidClient client;
    private readonly SliderInput hapticsGainInput;
    private readonly SliderInput speakerVolumeInput;
    private readonly SliderInput inactiveTimeInput;
    private readonly SliderInput audioBufferInput;
    private readonly CheckBox disableInactiveDisconnectInput;
    private readonly CheckBox disablePicoLedInput;
    private readonly ComboBox pollingRateInput;
    private readonly ComboBox controllerModeInput;
    private readonly Label statusLabel;
    private DongleConfig? currentConfig;
    private bool loading;

    public SettingsForm(DongleHidClient client)
    {
        this.client = client;

        Text = "DS5Dongle Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(640, 458);

        hapticsGainInput = new SliderInput("Haptics gain", 1.00m, 2.00m, 0.01m, 2);
        speakerVolumeInput = new SliderInput("Speaker volume (%)", 0m, 100m, 1m, 0);
        audioBufferInput = new SliderInput("Haptics buffer length", 16m, 128m, 1m, 0);
        inactiveTimeInput = new SliderInput("Inactive time (min)", 5m, 60m, 1m, 0);
        disableInactiveDisconnectInput = new CheckBox { Text = "Disable inactive disconnect", AutoSize = true };
        disablePicoLedInput = new CheckBox { Text = "Disable Pico LED", AutoSize = true };
        pollingRateInput = NewComboBox(["250 Hz", "500 Hz", "Real-time"]);
        controllerModeInput = NewComboBox(["DualSense", "DualSense Edge", "Auto"]);

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

        var refreshButton = new Button { Text = "Refresh", Location = new Point(16, 348), Size = new Size(76, 30) };
        var applyButton = new Button { Text = "Apply", Location = new Point(98, 348), Size = new Size(76, 30) };
        var saveButton = new Button { Text = "Save", Location = new Point(180, 348), Size = new Size(76, 30) };
        var reconnectButton = new Button { Text = "Reconnect USB", Location = new Point(262, 348), Size = new Size(112, 30) };
        var closeButton = new Button { Text = "Close", Location = new Point(548, 412), Size = new Size(76, 30) };

        statusLabel = new Label
        {
            AutoSize = false,
            Location = new Point(16, 410),
            Size = new Size(520, 30),
            TextAlign = ContentAlignment.MiddleLeft
        };

        refreshButton.Click += async (_, _) => await LoadConfigAsync();
        applyButton.Click += async (_, _) => await ApplyConfigAsync();
        saveButton.Click += async (_, _) => await SaveConfigAsync();
        reconnectButton.Click += async (_, _) => await ReconnectUsbAsync();
        closeButton.Click += (_, _) => Close();

        Controls.AddRange([
            feedbackGroup,
            powerGroup,
            performanceGroup,
            compatibilityGroup,
            refreshButton,
            applyButton,
            saveButton,
            reconnectButton,
            statusLabel,
            closeButton
        ]);

        Shown += async (_, _) => await LoadConfigAsync();
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
            statusLabel.Text = "Applied to RAM.";
        });
    }

    private async Task SaveConfigAsync()
    {
        await RunUiTaskAsync("Saving config...", async () =>
        {
            await client.SaveConfigAsync();
            statusLabel.Text = "Saved to flash.";
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
        var baseConfig = currentConfig ?? new DongleConfig(
            DongleConfig.SupportedConfigVersion,
            1.0f,
            -100.0f,
            30,
            false,
            false,
            0,
            64,
            2);

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
        comboBox.SelectedIndex = 0;
        return comboBox;
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
