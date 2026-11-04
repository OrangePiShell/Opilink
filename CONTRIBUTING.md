# Contributing

Thanks for helping! Bug reports, board test results and pull requests are welcome.

## Reporting a bug
Open an issue with the **Bug report** template. Please include: Windows version, board model and OS (for example "Orange Pi 5, Armbian 25.8"), what you pressed, and the exact error text. Remove IP addresses you consider private and never paste passwords.

## Building
You only need Windows 10/11. Double-click `start.bat`: it compiles `app\*.cs` with the C# compiler that is part of Windows (`csc.exe`, .NET Framework 4).

## Translations
Texts are in `lang/<code>.txt` (`fa`, `zh`, `ru`, `hi`). Each line is `English text<TAB>translation`; the English text must be exactly what the code shows. A `*` in the English text matches any text and `{0}`, `{1}` in the translation put it back; `\n` is a new line. Keep translations short: the tool tiles are small. To add a language: create `lang/<code>.txt`, add the code and name to `L.Codes` / `L.Names` in `app/Lang.cs`, draw its flag in `app/Flags.cs`, and mention it in the README. A text that is missing simply stays English.

## Pull requests
1. Fork, create a branch (`feat/...` or `fix/...`).
2. Keep changes small and focused. Follow `.editorconfig`.
3. Anything that **changes the board** (reboot, kill, stop, update, delete) must ask for confirmation first. The app never deletes, renames or chmods files on the board.
4. Do not add third-party dependencies without discussing it in an issue first.
5. Update `CHANGELOG.md` under `[Unreleased]`.
6. Say in the PR what you tested and on which board/OS.

## Commit style
[Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `chore:`.

## Code of Conduct
By taking part you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
