# DS5Dongle Host Protocol

DS5DongleTray communicates with the DS5Dongle Pico firmware through HID feature reports for management/config commands, and reads battery status from the normal DualSense-compatible `0x01` controller input report. The firmware appears as a DualSense-compatible HID device, so the app first probes candidate devices by reading the DS5Dongle-specific firmware version report.

## Reports

| Report ID | Direction | Description |
| --- | --- | --- |
| `0xF6` | Host to device | Command report. Subcommands include apply config, save config, and reconnect USB. |
| `0xF7` | Device to host | Read packed firmware config body. |
| `0xF8` | Device to host | Read firmware version string. |
| `0xF9` | Device to host | Read cached Bluetooth RSSI as signed int8. |

## Battery Status (`0x01`)

Battery status is parsed from the normal controller input report:

```c
uint8_t battery = input_report[52];
uint8_t level_raw = battery & 0x0F;          // 0..10
uint8_t power_state = (battery >> 4) & 0x0F;
uint8_t percent = power_state == 0x02
    ? 100
    : min(level_raw, 10) * 10;               // 0..100, 10% steps
uint8_t flags = input_report[53];            // USB power flags are used to disambiguate 0x02
```

The app follows Sony's upstream Linux `hid-playstation` interpretation for the raw fields: the lower nibble is a 0..10 battery level and `power_state == 0x02` means full/complete regardless of the raw level. DS5DongleTray intentionally displays discharging/charging values in coarse 10% steps instead of Linux's midpoint presentation.

Overlay triggers also watch the normal input report at 100 ms intervals. The latest input report battery state is cached for faster tray updates, while firmware/config/RSSI feature reports stay on the slower management polling path.

Power state names:

- `0x00`: Discharging
- `0x01`: Charging
- `0x02`: Complete
- `0x0A`: Abnormal Voltage
- `0x0B`: Abnormal Temperature
- `0x0F`: Charging Error

## Compatibility

Firmware without `0xF8` is not treated as DS5Dongle by the tray app.

## Config Body (`0xF7`)

Current firmware config body layout:

```c
uint8_t config_version;
float haptics_gain;              // 1.0..2.0
float speaker_volume;            // -100..0 dB
uint8_t inactive_time;           // 5..60 min
uint8_t disable_inactive_disconnect;
uint8_t disable_pico_led;
uint8_t polling_rate_mode;       // 0: 250 Hz, 1: 500 Hz, 2: real-time
uint8_t audio_buffer_length;     // 16..128
uint8_t controller_mode;         // 0: DS5, 1: DSE, 2: Auto
```

To apply config immediately, send `0xF6` with payload byte `0x01` followed by the packed config body. To persist the current config to flash, send `0xF6` with payload byte `0x02`. To reconnect USB, send `0xF6` with payload byte `0x03`.

Firmware versions containing `-custom` are treated as supporting `0xF6 + 0x04` to enter UF2 bootloader mode. This is not part of the upstream/original firmware path.
