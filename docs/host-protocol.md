# DS5Dongle Host Protocol

DS5DongleTray communicates with the DS5Dongle Pico firmware through HID feature reports. The firmware appears as a DualSense-compatible HID device, so the app first probes candidate devices by reading the DS5Dongle-specific firmware version report.

## Reports

| Report ID | Direction | Description |
| --- | --- | --- |
| `0xF6` | Host to device | Command report. Subcommands include apply config, save config, reconnect USB, enter UF2 bootloader. |
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

To enter UF2 bootloader mode for firmware update, send `0xF6` with payload byte `0x04`. The device disconnects from normal HID mode and should reappear as a removable UF2 drive.
