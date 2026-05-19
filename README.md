# DS5DongleTray

DS5DongleTray is a small Windows notification-area app for DS5Dongle. It polls the Pico firmware for the connected DualSense battery level, firmware version, and Bluetooth RSSI, then shows the current battery state in the tray tooltip and context menu.

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

## Known Limitations

- DualSense reports battery in coarse 10% steps.
- Replacement batteries may still show normal-looking percentages because values are controller-reported estimates, not direct mAh measurements.
- Battery is shown as unknown when the controller is disconnected or the cached report is stale.
- This tray app is Windows only.
