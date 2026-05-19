# DS5Dongle Host Protocol

DS5DongleTray communicates with the DS5Dongle Pico firmware through HID feature reports. The firmware appears as a DualSense-compatible HID device, so the app first probes candidate devices by reading the DS5Dongle-specific firmware version report.

## Reports

| Report ID | Direction | Description |
| --- | --- | --- |
| `0xF6` | Host to device | Command report. Subcommands include apply config, save config, reconnect USB. |
| `0xF7` | Device to host | Read packed firmware config body. |
| `0xF8` | Device to host | Read firmware version string. |
| `0xF9` | Device to host | Read cached Bluetooth RSSI as signed int8. |
| `0xFA` | Device to host | Read cached DualSense battery status. |

## Battery Status (`0xFA`)

Payload bytes after the report ID:

```c
uint8_t level_raw;          // 0..10, or 0xFF when unknown
uint8_t percent;            // 0..100, or 0xFF when unknown
uint8_t power_state;        // raw upper nibble from the DualSense battery byte
uint8_t is_charging;        // 1 when power_state == 0x01
uint8_t is_connected;       // whether the controller is currently known connected
uint8_t is_fresh;           // whether the latest battery report is recent
uint32_t last_report_age_ms;
```

Power state names:

- `0x00`: Discharging
- `0x01`: Charging
- `0x02`: Full
- `0x0A`: Abnormal Voltage
- `0x0B`: Abnormal Temperature
- `0x0F`: Charging Error

## Compatibility

Firmware without `0xFA` is treated as battery-unsupported. Firmware without `0xF8` is not treated as DS5Dongle by the tray app.
