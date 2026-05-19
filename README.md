# DS5DongleTray

DS5DongleTray is a small Windows notification-area app for DS5Dongle. It polls the Pico firmware for the connected DualSense battery level, firmware version, and Bluetooth RSSI. It also provides a settings window for DS5Dongle firmware config.

## Requirements

- Windows 10 or newer
- .NET 8 Desktop Runtime to run
- .NET 8 SDK to build
- DS5Dongle firmware with the `0xFA` battery status feature report

## Build

From this directory:

```powershell
dotnet restore
dotnet build -c Release
```

The app binary will be under:

```text
src\DS5DongleTray\bin\Release\net8.0-windows10.0.17763.0\
```

## Release Build

Tagged releases are built by GitHub Actions. Pushing a tag like `v0.1.0` creates a GitHub Release and attaches:

- `DS5DongleTray-<tag>-win-x64.exe`
- `DS5DongleTray-<tag>-win-x64.exe.sha256`

The release executable is framework-dependent and requires the .NET 8 Desktop Runtime.

## Run

Start the tray app:

```powershell
dotnet run --project .\src\DS5DongleTray\DS5DongleTray.csproj -c Release
```

Run one diagnostic poll and exit:

```powershell
dotnet run --project .\src\DS5DongleTray\DS5DongleTray.csproj -c Release -- --once
```

## Firmware Protocol

The tray app talks to DS5Dongle through HID feature reports, using the same style as the web config page. See [docs/host-protocol.md](docs/host-protocol.md) for the report IDs and payloads.

## Settings Window

The tray menu opens a settings window that can apply all current DS5Dongle firmware config fields:

- Haptics gain
- Speaker volume, shown as 0-100% in the app and converted to the firmware's dB value
- Inactive disconnect time
- Disable inactive disconnect
- Disable Pico LED
- Polling rate mode
- Audio buffer length
- Controller mode

Range settings use a slider plus numeric input and clamp out-of-range values automatically. Settings are applied to RAM immediately with `0xF6 + 0x01`. Use `Save to Flash` to persist them across reboots. Some USB descriptor-related settings may require `Reconnect USB` before Windows observes the change.

## Firmware Update

The tray menu also includes `Update Firmware...`. It checks the latest GitHub Release from `jaein4722/DS5Dongle`, downloads the standard `.uf2` firmware asset, asks the dongle to enter UF2 bootloader mode, waits for the removable UF2 drive, and copies the firmware file to it.

For testing, the same window can install a manually selected local `.uf2` file. It can also enter UF2 bootloader mode without copying firmware, which is useful for verifying the firmware-side bootloader command. Bootloader mode stops normal DS5Dongle operation; copy a UF2 firmware file or unplug and reconnect the Pico to return to firmware mode.

Automatic update requires firmware that supports the `0xF6 + 0x04` bootloader command. Older firmware must be updated manually once before this menu item can complete the whole update flow.

## Known Limitations

- DualSense reports battery in coarse 10% steps.
- Replacement batteries may still show normal-looking percentages because values are controller-reported estimates, not direct mAh measurements.
- Battery is shown as unknown when the controller is disconnected or the cached report is stale.
- Firmware update depends on GitHub Releases containing a compatible `.uf2` asset.
- This tray app is Windows only.
