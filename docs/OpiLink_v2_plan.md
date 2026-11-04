# Orange Pi Link — v2 plan

Status: **all 16 tiles are written. A running v2 window is shown in `mock/screenshot.png`; each tile still needs a full test on a real board.** Shell scripts were syntax-checked on Linux.
Mock-up: `docs/mockup_v1.1.png`

## Done in v1.0 (already in the project)
- Board IP + User, "Find board on my network", remembered values
- **View board like WSL**: browse, download (folder = one .tar), upload (asks before replace), terminal in the current folder. Never deletes/renames.
- **Board status**: name, system, kernel, IP, uptime, load, memory, CPU temperature, disk; refresh every 5 s
- Login with a key made only for this app (`opilink_ed25519`), no password stored

## Changes already made after v1.0
- New wordmark `assets/wordmark.png`: "orange pi link" (burner font style). The orange "Link" label was removed from `LinkForm.cs`.

## UI plan (v2)
- Window: top card (IP, User, state, detected system line) + a 2-column grid of equal tiles.
- The two old big buttons become normal tiles. "View board like WSL" stays orange as the main action.
- Tiles whose action changes the board (Reboot/Shutdown, Update packages, Services, Processes/Kill) always ask for confirmation.
- If the grid gets too tall: split into tabs (Files, System, Network, Advanced).

## Tiles (16)
| # | Tile | What it does | Commands (typical) |
|---|------|--------------|--------------------|
| 1 | View board like WSL | file view, download/upload | existing |
| 2 | Board status | live status | existing |
| 3 | Open terminal | ssh window on the board | `ssh` (existing `OpenTerminal`) |
| 4 | Reboot / Shutdown | power, with confirm | `reboot`, `poweroff` |
| 5 | Network info | IP, Wi-Fi, gateway, DNS, internet test | `ip a`, `nmcli`, `ping` |
| 6 | Update packages | update/upgrade with live log, confirm | `apt update && apt upgrade` (or dnf/pacman) |
| 7 | Services | list + start/stop/restart, confirm | `systemctl` |
| 8 | Run command | one command, see output | `Ssh.Run` |
| 9 | Processes | CPU/RAM list, Kill with confirm | `ps`, `kill` |
| 10 | Docker | containers: start, stop, logs | `docker ps/start/stop/logs` |
| 11 | Logs | log viewer | `journalctl`, `dmesg` |
| 12 | Port forwarding | board port -> Windows localhost | `ssh -L` |
| 13 | System config | opens the distro config tool in a terminal | `armbian-config` / `dietpi-config` |
| 14 | Hardware info | model, kernel/BSP, CPU/GPU/NPU temp and freq | `/proc`, `/sys` |
| 15 | Storage / NVMe | disks, usage, SMART health | `lsblk`, `df`, `smartctl` |
| 16 | Backup | tar of a chosen folder saved on Windows | `tar` over ssh |

## Not Armbian-only: system detection
The board may run Armbian, DietPi, Ubuntu, Debian, Orange Pi OS, etc. After connecting, the app runs one short read-only check and remembers the result:
- `/etc/os-release` (name + version, shown under "Connected to ...", e.g. `Armbian 25.8 (Debian 12) · aarch64`)
- is `systemctl` / `journalctl` there? (Services, Logs)
- which package manager? `apt` / `dnf` / `pacman` (Update packages)
- is `docker` installed? (Docker)
- `nmcli` / `iw` / `wpa_supplicant` (Network info)
- `smartctl` (Storage; if missing, show `lsblk`/`df` only)

Rules:
- A tile that does not work on this system is **greyed out** with a line like "Not available on this system" (never an error popup).
- "System config" is named by system: `armbian-config`, `dietpi-config`; hidden/greyed on others.
- Hardware tiles read `/proc` and `/sys`, so they work on every distro.
- The check never installs or changes anything on the board.

## Later / optional (not in the mock-up)
- 15 Multi-board: saved list of boards with names
- 16 Set Wi-Fi / Static IP from inside the app
- 17 Mount as a real Explorer drive (needs WinFsp + SSHFS-Win installed separately)

## Suggested build order
1. Open terminal, Reboot/Shutdown, Network info, Run command (small, reuse `Ssh.Run`)
2. System detection + greyed tiles
3. Services, Logs, Processes, Update packages (need confirm dialogs)
4. Hardware info, Storage/NVMe, Docker
5. Port forwarding, Backup
6. Multi-board, Wi-Fi/static IP, SSHFS drive

## Test note
Every tile must be tested on a real board. If the build fails, open an issue with the error text.
