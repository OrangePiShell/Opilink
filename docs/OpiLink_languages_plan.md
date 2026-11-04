# Orange Pi Link - languages

Status: **implemented (needs a build + review on Windows).** English, فارسی (fa), 中文 (zh), Русский (ru), हिन्दी (hi).

## How it works
- `app/Lang.cs`: class `L` (translate, load, hook windows) and `Msg` (a `MessageBox` wrapper that translates).
- The English text in the code is the key. `lang/<code>.txt` holds `English<TAB>translation`. `*` in a key matches any text, `{0}`/`{1}` put it back, so built sentences such as "Connected to root@IP" are translated too. Texts with a blank line are translated paragraph by paragraph.
- Labels, buttons, check boxes, window titles and list headers are translated automatically when they get their text (`L.Attach`, called from `Theme.Attach`). Tile texts are translated when drawn. Message boxes go through `Msg.Show`. The coloured status line keeps choosing its colour from the English text (`StatusLabel`).
- A missing text stays English. Output from the board (logs, command results, file names) is never translated.
- The language picker is in the main window (top right). The choice is saved in `%APPDATA%\OpiLink\lang.txt`; the app restarts to apply it. First start: the Windows display language if supported, else English.

## Known limits
- Persian: text reads right to left (labels, check boxes, message boxes) but the window layout is not mirrored; tiles keep left alignment.
- Longer languages (Russian, Hindi) may be cut off in the small tiles or buttons; shorten the text in the language file if so.
- Chinese and Hindi rely on Windows font fallback from Segoe UI.
- Translations were written by Claude and must be reviewed by native speakers, especially warnings and confirm questions.
- Some rarely seen error texts from the code are still English (they are not in the files yet).
