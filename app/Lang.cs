using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace OpiLink
{
    // Translations. The English text in the code is the key; nothing in the code changes when a language is added.
    // Texts live in lang\<code>.txt next to OpiLink.exe: "English text<TAB>translation", one per line.
    // In a key, * matches any text and {0}, {1} in the translation put that text back. A text that is not found stays English.
    static class L
    {
        public static readonly string[] Codes = { "en", "fa", "zh", "ru", "hi" };
        public static readonly string[] Names = { "English", "فارسی", "中文", "Русский", "हिन्दी" };
        public static string Code = "en";

        static string baseDir = "";
        static readonly Dictionary<string, string> exact = new Dictionary<string, string>();
        static readonly List<KeyValuePair<Regex, string>> patterns = new List<KeyValuePair<Regex, string>>();
        static readonly Dictionary<Control, bool> hooked = new Dictionary<Control, bool>();
        static bool busy;

        public static bool Rtl { get { return Code == "fa"; } }

        public static int Index
        {
            get { int i = Array.IndexOf(Codes, Code); return i < 0 ? 0 : i; }
        }

        static string LangFile()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpiLink"), "lang.txt");
        }

        // Called once at start: saved choice, else the Windows display language, else English.
        public static void Init(string baseDirectory)
        {
            baseDir = baseDirectory;
            string c = null;
            try { if (File.Exists(LangFile())) c = File.ReadAllText(LangFile()).Trim(); } catch (Exception) { }
            if (string.IsNullOrEmpty(c))
            {
                try { c = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName; } catch (Exception) { c = "en"; }
            }
            if (Array.IndexOf(Codes, c) < 0) c = "en";
            Load(c);
        }

        static void Load(string code)
        {
            Code = "en";
            exact.Clear();
            patterns.Clear();
            if (code == "en") return;
            string path = Path.Combine(Path.Combine(baseDir, "lang"), code + ".txt");
            if (!File.Exists(path)) return;
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    if (raw.Length == 0 || raw[0] == '#') continue;
                    int tab = raw.IndexOf('\t');
                    if (tab <= 0) continue;
                    string k = raw.Substring(0, tab).Replace("\\n", "\n");
                    string v = raw.Substring(tab + 1).Replace("\\n", "\n");
                    if (k.IndexOf('*') >= 0)
                    {
                        string rx = "^" + Regex.Escape(k).Replace("\\*", "(.*?)") + "$";
                        patterns.Add(new KeyValuePair<Regex, string>(new Regex(rx, RegexOptions.Singleline), v));
                    }
                    else exact[k] = v;
                }
                Code = code;
            }
            catch (Exception) { exact.Clear(); patterns.Clear(); Code = "en"; }
        }

        // The picked language is saved and the app starts again (every window is built with the new texts).
        public static void Choose(int index)
        {
            if (index < 0 || index >= Codes.Length || Codes[index] == Code) return;
            try
            {
                string f = LangFile();
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f, Codes[index], new UTF8Encoding(false));
            }
            catch (Exception) { return; }
            Application.Restart();
        }

        public static string T(string s)
        {
            if (string.IsNullOrEmpty(s) || Code == "en") return s;
            string v;
            if (exact.TryGetValue(s, out v)) return v;
            foreach (KeyValuePair<Regex, string> p in patterns)
            {
                Match m = p.Key.Match(s);
                if (!m.Success) continue;
                Match mm = m;
                return Regex.Replace(p.Value, @"\{(\d)\}", delegate (Match g)
                {
                    int i = int.Parse(g.Groups[1].Value) + 1;
                    return i < mm.Groups.Count ? T(mm.Groups[i].Value) : g.Value;
                });
            }
            if (s.IndexOf("\n\n", StringComparison.Ordinal) >= 0)
            {
                string[] parts = s.Split(new string[] { "\n\n" }, StringSplitOptions.None);
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    string t = T(parts[i]);
                    if (t != parts[i]) any = true;
                    parts[i] = t;
                }
                if (any) return string.Join("\n\n", parts);
            }
            return s;
        }

        // ---------- windows ----------
        // Translates the text of labels, buttons, check boxes, window titles and list headers now and whenever they change.
        public static void Attach(Control root)
        {
            if (Code == "en") return;
            Hook(root);
        }

        static bool Skip(Control c)
        {
            return c is TextBoxBase || c is ComboBox || c is ListBox || c is ListView || c is UpDownBase || c is StatusLabel || c is ThemeToggle;
        }

        static void Hook(Control c)
        {
            if (hooked.ContainsKey(c)) return;
            hooked[c] = true;
            c.Disposed += delegate { hooked.Remove(c); };
            if (!Skip(c))
            {
                Fix(c);
                c.TextChanged += OnText;
            }
            ListView lv = c as ListView;
            if (lv != null) foreach (ColumnHeader h in lv.Columns) h.Text = T(h.Text);
            c.ControlAdded += delegate (object s, ControlEventArgs e) { Hook(e.Control); };
            foreach (Control k in c.Controls) Hook(k);
        }

        static void OnText(object sender, EventArgs e)
        {
            if (busy) return;
            busy = true;
            try { Fix((Control)sender); }
            finally { busy = false; }
        }

        static void Fix(Control c)
        {
            string t = c.Text;
            bool translated = false;
            if (!string.IsNullOrEmpty(t))
            {
                string v = T(t);
                if (v != t) { c.Text = v; translated = true; }
            }
            // Right to left only for translated texts: numbers and values such as "512 / 2048 MB" must keep their order.
            if (Rtl && translated && (c is Label || c is CheckBox) && c.RightToLeft != RightToLeft.Yes) c.RightToLeft = RightToLeft.Yes;
        }
    }

    // Same as MessageBox.Show, but the text is translated and, for Persian, read right to left.
    static class Msg
    {
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(owner, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }

        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxDefaultButton defaultButton)
        {
            MessageBoxOptions opt = L.Rtl ? (MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) : (MessageBoxOptions)0;
            return MessageBox.Show(owner, L.T(text), L.T(caption), buttons, icon, defaultButton, opt);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return Show(null, text, caption, buttons, icon, MessageBoxDefaultButton.Button1);
        }
    }
}
