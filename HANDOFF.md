# HANDOFF - Orange Pi Link (OpiLink)

State: GitHub-ready repo prepared (v1.0.0). Nothing pushed yet.

## Project
Windows WinForms app (C#, .NET Framework 4, built by `start.bat` / CI with Windows `csc.exe`) that manages a Linux SBC over the Windows OpenSSH client. 16 tiles (see README). Entry: `app/Program.cs` -> `LinkForm`.

## Done so far
- v1.0: IP/user, find board, WSL-like file view, board status, key login.
- v2: 16 tiles, system detection with greyed tiles, `opi` command, light/dark theme, confirm dialogs. A real screenshot of the running v2 window is in `mock/screenshot.png`.
- GITHUB_READY pass:
  - Security scan of all files: no tokens, keys, passwords, personal data or absolute paths found. Only the private example IPs 192.168.1.6 / 192.168.0.1 in text/code. No network code besides ssh (no telemetry).
  - Added: README.md, LICENSE (MIT, OrangePiShell), .gitignore, .gitattributes, .editorconfig, CHANGELOG.md, CONTRIBUTING.md, CODE_OF_CONDUCT.md, SECURITY.md, THIRD_PARTY_NOTICES.md, `.github/` (FUNDING, dependabot, CODEOWNERS, issue/PR templates, ci.yml, release.yml), `app/AssemblyInfo.cs` (version 1.0.0), `mock/`.
  - Changed: `README.txt` -> `README.md`; `OpiLink_v2_plan.md` -> `docs/`; `VERSION.txt` now `Orange Pi Link 1.0.0 (...)`; status lines in the plan doc updated.
- No tests exist (C# WinForms, no test project); CI = build + hygiene check.

- Languages added (en, fa, zh, ru, hi): `app/Lang.cs`, `lang/*.txt` (265 entries each), picker in the main window, release zip includes `lang/`. Never compiled: check the build, then check every window in every language (cut-off text, Persian RTL).

- Language box is owner-drawn with flags drawn in `app/Flags.cs` (Windows cannot render flag emoji). Verified by user: v1.0.0 builds and runs, language box opens. Iran flag has no emblem (simplified).

## Decisions
- Release builds use `csc.exe` on `windows-latest` (same command as `start.bat`), not `dotnet publish`: the project is not an SDK project. Zip name `OpiLink-v<ver>-win-x64.zip` with exe, `assets/`, README, LICENSE; `SHA256SUMS.txt`; tags with `-` are pre-releases.
- Repo: github.com/OrangePiShell/Opilink (public, created empty by the user). License MIT.

## Open items
- Owner/repo, LICENSE holder (OrangePiShell) and contact (GitHub, no email) are filled in. Donate section in README: TRX + USDT on TRON (TRC20), one address (user-provided; BTC/LTC labels were TRON-format addresses, so not listed).
- Workflows were never run. Exe from CI not tested.
- Orange Pi logo in `assets/wordmark.png`: trademark risk, consider replacing.
- Namespace renamed OpiBurner -> OpiLink (all app/*.cs). Version set to 1.0.0 (title bar "v1.0.0", AssemblyInfo, VERSION.txt, CHANGELOG, tag v1.0.0).
- Test all 16 tiles on real boards.
- Review translations with native speakers; find texts still English.
- README images `docs/screenshot-light.png` and `docs/screenshot-dark.png` are REAL v1.0.0 screenshots from the user (language box open, flags visible).
