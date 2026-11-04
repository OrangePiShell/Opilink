using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace OpiLink
{
    // Adds a command "opi" to Windows (cmd, PowerShell, Windows Terminal): typing  opi  opens a shell on the board, like typing  wsl.
    // It is only a small file on this PC (opi.cmd) and one folder in the user PATH. Nothing is installed on the board, no admin needed,
    // and it works when Orange Pi Link is closed. The key is the same one the app uses.
    static class Cli
    {
        const string Name = "opi";

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

        static string BinDir
        {
            get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpiLink"), "bin"); }
        }

        static string File1 { get { return Path.Combine(BinDir, Name + ".cmd"); } }

        // ip and user were already checked by Ssh.CheckTarget (private IPv4 and a plain user name), so they are safe inside the .cmd file.
        static string Script(string ip, string user)
        {
            return "@echo off\r\n" +
                   "rem Made by Orange Pi Link. Opens a shell on the board; words after " + Name + " are run as one command there.\r\n" +
                   "ssh -t -i \"%USERPROFILE%\\.ssh\\opilink_ed25519\" -o IdentitiesOnly=yes -o ConnectTimeout=6 -o ServerAliveInterval=15 " +
                   "-o StrictHostKeyChecking=accept-new " + user + "@" + ip + " %*\r\n";
        }

        static bool SamePath(string a, string b)
        {
            return string.Equals((a ?? "").Trim().TrimEnd('\\'), (b ?? "").Trim().TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }

        static void Broadcast()
        {
            try
            {
                UIntPtr r;
                SendMessageTimeout((IntPtr)0xFFFF, 0x1A, UIntPtr.Zero, "Environment", 2, 5000, out r);
            }
            catch (Exception) { }
        }

        static void AddToPath(string dir)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey("Environment", true))
            {
                if (k == null) throw new InvalidOperationException("Could not open the user environment settings.");
                string cur = k.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                foreach (string part in cur.Split(';')) if (SamePath(part, dir)) return;
                string nv = cur.Length == 0 ? dir : cur.TrimEnd(';') + ";" + dir;
                k.SetValue("Path", nv, RegistryValueKind.ExpandString);
            }
        }

        static void RemoveFromPath(string dir)
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey("Environment", true))
            {
                if (k == null) return;
                string cur = k.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                var keep = new StringBuilder();
                foreach (string part in cur.Split(';'))
                {
                    if (part.Trim().Length == 0 || SamePath(part, dir)) continue;
                    if (keep.Length > 0) keep.Append(';');
                    keep.Append(part);
                }
                k.SetValue("Path", keep.ToString(), RegistryValueKind.ExpandString);
            }
        }

        public static void Run(IWin32Window owner, string ip, string user)
        {
            string dir = BinDir, file = File1;
            try
            {
                if (File.Exists(file))
                {
                    DialogResult r = Msg.Show(owner,
                        "The command \"" + Name + "\" is already set up on this PC.\n\n" +
                        "Yes = update it to " + user + "@" + ip + "\nNo = remove it\nCancel = do nothing",
                        "Orange Pi Link", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Cancel) return;
                    if (r == DialogResult.No)
                    {
                        File.Delete(file);
                        try { if (Directory.GetFileSystemEntries(dir).Length == 0) { Directory.Delete(dir); RemoveFromPath(dir); Broadcast(); } } catch (Exception) { }
                        Msg.Show(owner, "The command \"" + Name + "\" was removed.", "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                }
                else if (Msg.Show(owner,
                    "Add the command \"" + Name + "\" to Windows?\n\n" +
                    "After this, typing  " + Name + "  in any cmd or PowerShell window opens a shell on " + user + "@" + ip + " - like typing  wsl  - even when Orange Pi Link is closed.\n\n" +
                    "What is added: a small file " + Name + ".cmd in\n" + dir + "\nand that folder in your user PATH (no admin needed). Nothing is installed on the board.\n\n" +
                    "If the board's IP changes, press this button again.",
                    "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

                Directory.CreateDirectory(dir);
                File.WriteAllText(file, Script(ip, user), new UTF8Encoding(false));
                AddToPath(dir);
                Broadcast();
                Msg.Show(owner,
                    "Done. Open a NEW cmd or PowerShell window and type:\n\n    " + Name + "\n\nWords after it run one command on the board, for example:  " + Name + " ls /",
                    "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception e)
            {
                Msg.Show(owner, e.Message, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
