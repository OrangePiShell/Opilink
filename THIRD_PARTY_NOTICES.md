# Third-party notices

Orange Pi Link contains **no third-party source code and no bundled libraries**.

It relies on components that are *not* shipped with it:

| Component | Used for | License / owner |
|-----------|----------|-----------------|
| .NET Framework 4.x (built into Windows 10/11): WinForms, `csc.exe` | UI and compiling | Microsoft, part of Windows |
| Windows "OpenSSH Client" (`ssh.exe`, `ssh-keygen.exe`) | All communication with the board | OpenSSH is BSD-style licensed; installed by Windows |
| Standard Linux tools on the board (`ps`, `systemctl`, `journalctl`, `tar`, `lsblk`, `smartctl`, ...) | Run over SSH | Their own licenses; nothing is installed on the board by this app |

No license conflicts were found: the project is MIT and nothing GPL-licensed is linked or bundled.

## Trademarks and artwork
"Orange Pi" is a name and logo of its owner (Shenzhen Xunlong Software Co., Ltd.). This project is not affiliated with or endorsed by them. `assets/wordmark.png` uses an orange-fruit logo in the style of the Orange Pi brand. The MIT license of this repository covers the code; it does **not** grant any rights to third-party trademarks or logos. See the README disclaimer.
