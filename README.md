# DS5DongleTray

DS5DongleTray is a small Windows notification-area app for DS5Dongle. It reads the connected DualSense battery level, firmware version, and Bluetooth RSSI. It also provides a settings window for DS5Dongle firmware config.

DS5DongleTray is intended to work with the [original DS5Dongle firmware](https://github.com/awalol/DS5Dongle) as long as the dongle exposes the existing host config reports used by the web config page. Original firmware gets official release checking and download-only firmware update assistance; custom firmware also enables tray-assisted flashing when the version contains `-custom`.

Release executables are self-contained Windows x64 builds, so installing the .NET Desktop Runtime separately is not required.

## Download

Download this file from the latest GitHub Release:

- `DS5DongleTray-<tag>-win-x64.exe`

The SHA256 checksum file is also attached:

- `DS5DongleTray-<tag>-win-x64.exe.sha256`

## Usage

1. Run the downloaded `.exe`.
2. If Windows SmartScreen appears, choose `More info`, then `Run anyway`.
3. DS5DongleTray starts in the Windows notification area / system tray.
4. If the icon is hidden, open the tray overflow arrow near the taskbar clock.
5. Right-click the DS5DongleTray icon to open the menu.

The tray menu shows battery, firmware version, and RSSI when the dongle is connected. It also provides:

- `Refresh now`
- `Settings...`
- `Update Firmware...`
- `Update App...`
- `Open Config Page`
- `Open GitHub Repository`
- `Exit`

## Firmware Update

`Update Firmware...` checks releases from the matching firmware channel. Original firmware checks [awalol/DS5Dongle](https://github.com/awalol/DS5Dongle) and can download the latest official `.uf2` for manual BOOTSEL flashing. Custom firmware checks [jaein4722/DS5Dongle](https://github.com/jaein4722/DS5Dongle), can install a selected local `.uf2`, and can ask supported firmware to reboot into UF2 bootloader mode.

Automated flashing requires firmware with the custom `0xF6 + 0x04` bootloader command. The app enables flash actions only when the connected firmware version contains `-custom`; original firmware uses download-only update assistance.

## App Update

`Update App...` checks [jaein4722/DS5DongleTray](https://github.com/jaein4722/DS5DongleTray) releases. It can download the latest self-contained Windows exe, verify the SHA256 checksum when the release provides one, and replace the current exe by launching a temporary `.cmd` helper after DS5DongleTray exits. If the current exe directory is not writable, the app falls back to download-only mode.

DS5DongleTray checks for app updates once shortly after startup, then once per day while running. Available app updates are shown quietly in the tray menu; up-to-date results are hidden.

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

## Firmware Protocol

The tray app talks to DS5Dongle through HID feature reports, using the same style as the web config page. Battery status is read from the normal `0x01` controller input report. See [docs/host-protocol.md](docs/host-protocol.md) for the report IDs and payloads.

## Development

Requirements for building from source:

- Windows 10 or newer
- .NET 8 SDK

Build:

```powershell
dotnet restore
dotnet build -c Release
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
- Automatic firmware flashing only works with firmware that implements the custom bootloader command.
- This tray app is Windows only.
