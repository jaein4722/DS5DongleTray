# DS5DongleTray

DS5DongleTray is a small Windows notification-area app for DS5Dongle. It reads the connected DualSense battery level, firmware version, and Bluetooth RSSI. It also provides a settings window for DS5Dongle firmware config.

DS5DongleTray is intended to work with the [original DS5Dongle firmware](https://github.com/awalol/DS5Dongle) as long as the dongle exposes the existing host config reports used by the web config page.

## Which Build Should I Use?

Release builds are split into two flavors:

- `original`: for the upstream/original firmware. This is the recommended build for most users.
- `custom`: for my [custom firmware fork](https://github.com/jaein4722/DS5Dongle). This includes experimental tray-assisted firmware update support using the custom `0xF6 + 0x04` UF2 bootloader command.

Release executables are self-contained Windows x64 builds, so installing the .NET Desktop Runtime separately is not required.

## Download

Download one of these files from the latest GitHub Release:

- `DS5DongleTray-<tag>-original-win-x64.exe`
- `DS5DongleTray-<tag>-custom-win-x64.exe`

SHA256 checksum files are also attached:

- `DS5DongleTray-<tag>-original-win-x64.exe.sha256`
- `DS5DongleTray-<tag>-custom-win-x64.exe.sha256`

## Usage

1. Run the downloaded `.exe`.
2. If Windows SmartScreen appears, choose `More info`, then `Run anyway`.
3. DS5DongleTray starts in the Windows notification area / system tray.
4. If the icon is hidden, open the tray overflow arrow near the taskbar clock.
5. Right-click the DS5DongleTray icon to open the menu.

The tray menu shows battery, firmware version, and RSSI when the dongle is connected. It also provides:

- `Refresh now`
- `Settings...`
- `Open Config Page`
- `Open GitHub Repository`
- `Exit`

The `custom` build also adds:

- `Update Firmware...`

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

## Custom Firmware Flavor

The `custom` release build adds `Update Firmware...` to the tray menu. It checks releases from `jaein4722/DS5Dongle`, can install a selected local `.uf2`, and can ask supported firmware to reboot into UF2 bootloader mode.

This requires firmware with the custom `0xF6 + 0x04` bootloader command. It is not available in the `original` build.

## Firmware Protocol

The tray app talks to DS5Dongle through HID feature reports, using the same style as the web config page. Battery status is read from the normal `0x01` controller input report. See [docs/host-protocol.md](docs/host-protocol.md) for the report IDs and payloads.

## Development

Requirements for building from source:

- Windows 10 or newer
- .NET 8 SDK

Build the original firmware flavor:

```powershell
dotnet restore
dotnet build -c Release
```

Build the custom firmware flavor:

```powershell
dotnet build -c Release -p:DefineConstants=CUSTOM_FIRMWARE
```

Start the tray app from source:

```powershell
dotnet run --project .\src\DS5DongleTray\DS5DongleTray.csproj -c Release
```

Run one diagnostic poll and exit:

```powershell
dotnet run --project .\src\DS5DongleTray\DS5DongleTray.csproj -c Release -- --once
```

Tagged releases are built by GitHub Actions.

## Known Limitations

- DualSense reports battery in coarse 10% steps.
- Replacement batteries may still show normal-looking percentages because values are controller-reported estimates, not direct mAh measurements.
- Battery is shown as unknown when the controller is disconnected or no `0x01` input report is observed during polling.
- The custom firmware update flow only works with firmware that implements the custom bootloader command.
- This tray app is Windows only.
