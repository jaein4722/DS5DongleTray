# DS5DongleTray

DS5DongleTray is a small Windows notification-area app for DS5Dongle. It polls the Pico firmware for the connected DualSense battery level, firmware version, and Bluetooth RSSI. It also provides a settings window for DS5Dongle firmware config.

DS5DongleTray also works with the [original DS5Dongle firmware](https://github.com/awalol/DS5Dongle), but some features will be unavailable.
For the best experience, consider using my [custom fork](https://github.com/jaein4722/DS5Dongle), which adds the following host-tool extensions:

- Battery status report
- Reboot to UF2 bootloader / BOOTSEL mode

## Requirements

- Windows 10 or newer
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) to run
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

Tagged releases are built by GitHub Actions.

- `DS5DongleTray-<tag>-win-x64.exe`
- `DS5DongleTray-<tag>-win-x64.exe.sha256`

The release executable is framework-dependent and requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).

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
- Speaker volume
- Inactive disconnect time
- Disable inactive disconnect
- Disable Pico LED
- Polling rate mode
- Audio buffer length
- Controller mode

Settings are applied to RAM immediately with `0xF6 + 0x01`. Use `Save` to persist them across reboots. Polling rate mode and controller mode affect USB descriptors, so the app prompts for `Reconnect USB` after saving those changes.

## Firmware Update

The tray menu also includes `Update Firmware...`. It checks the latest GitHub Release from `jaein4722/DS5Dongle`, downloads the standard `.uf2` firmware asset, asks the dongle to enter UF2 bootloader mode, waits for the removable UF2 drive, and copies the firmware file to it.

For testing, you can install a manually selected local `.uf2` file. It can also enter UF2 bootloader mode without copying firmware, which is useful for verifying the firmware-side bootloader command. Bootloader mode stops normal DS5Dongle operation; copy a UF2 firmware file or unplug and reconnect the Pico to return to firmware mode.

Automatic update requires firmware that supports the `0xF6 + 0x04` bootloader command. Older firmware must be updated manually once before this menu item can complete the whole update flow.

## Known Limitations

- DualSense reports battery in coarse 10% steps.
- Replacement batteries may still show normal-looking percentages because values are controller-reported estimates, not direct mAh measurements.
- Battery is shown as unknown when the controller is disconnected or the cached report is stale.
- Firmware update depends on GitHub Releases containing a compatible `.uf2` asset.
- This tray app is Windows only.
