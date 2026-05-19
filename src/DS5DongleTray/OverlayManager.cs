namespace DS5DongleTray;

internal sealed class OverlayManager : IDisposable
{
    private readonly AppSettings settings;
    private readonly OverlayWindow window = new();
    private BatteryStatus? previousBattery;
    private bool wasLowBattery;
    private byte? lastLowBatteryPercentShown;
    private DateTimeOffset lastPsButtonOverlay = DateTimeOffset.MinValue;

    public OverlayManager(AppSettings settings)
    {
        this.settings = settings;
    }

    public void HandleSnapshot(DongleSnapshot snapshot)
    {
        var battery = snapshot.Battery;
        if (battery is null || !battery.HasKnownLevel)
        {
            previousBattery = battery;
            wasLowBattery = false;
            lastLowBatteryPercentShown = null;
            return;
        }

        if (ShouldShowChargingChange(battery))
        {
            Show(snapshot, "Charging state changed");
        }

        if (ShouldShowLowBattery(battery))
        {
            Show(snapshot, "Low battery");
        }

        previousBattery = battery;
    }

    public void HandleInputBattery(BatteryStatus? battery, DongleSnapshot? baseSnapshot)
    {
        if (battery?.IsPsButtonPressed != true || !settings.Overlay.ShowOnPsButton)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (now - lastPsButtonOverlay < TimeSpan.FromSeconds(2))
        {
            return;
        }

        lastPsButtonOverlay = now;
        var snapshot = (baseSnapshot ?? new DongleSnapshot { DeviceFound = true }) with { Battery = battery };
        Show(snapshot, "PS button");
    }

    public void ShowTrayClick(DongleSnapshot? snapshot)
    {
        if (!settings.Overlay.ShowOnTrayLeftClick)
        {
            return;
        }

        Show(snapshot ?? new DongleSnapshot { DeviceFound = false }, "DS5Dongle");
    }

    public void Dispose()
    {
        window.Dispose();
    }

    private bool ShouldShowChargingChange(BatteryStatus battery)
    {
        if (!settings.Overlay.ShowOnChargingStateChanged || previousBattery is null || !previousBattery.HasKnownLevel)
        {
            return false;
        }

        return battery.PowerState != previousBattery.PowerState || battery.IsUsbPowered != previousBattery.IsUsbPowered;
    }

    private bool ShouldShowLowBattery(BatteryStatus battery)
    {
        if (!settings.Overlay.ShowOnLowBattery || battery.IsCharging || battery.PowerState == 0x02)
        {
            wasLowBattery = false;
            lastLowBatteryPercentShown = null;
            return false;
        }

        var isLow = battery.Percent <= settings.Overlay.ClampedLowBatteryThreshold;
        if (!isLow)
        {
            wasLowBattery = false;
            lastLowBatteryPercentShown = null;
            return false;
        }

        var shouldShow = !wasLowBattery
            || !lastLowBatteryPercentShown.HasValue
            || battery.Percent < lastLowBatteryPercentShown.Value;

        wasLowBattery = true;
        if (shouldShow)
        {
            lastLowBatteryPercentShown = battery.Percent;
        }

        return shouldShow;
    }

    private void Show(DongleSnapshot snapshot, string reason)
    {
        window.ShowSnapshot(snapshot, reason, settings.Overlay.ClampedDisplaySeconds);
    }
}
