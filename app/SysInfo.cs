using System;
using System.Collections.Generic;

namespace OpiLink
{
    // What the board is running and which tools it has. Found with one short read-only command; nothing is changed or installed.
    class SysInfo
    {
        public string Name = "Linux", Id = "", Arch = "", ArmbianVersion = "", Drives = "";
        public bool Root;
        public bool Unknown; // detection failed: do not grey out anything
        public readonly Dictionary<string, bool> Has = new Dictionary<string, bool>();

        const string Cmd =
            "[ -r /etc/os-release ] && . /etc/os-release; echo name=$PRETTY_NAME; echo id=$ID; echo arch=$(uname -m); echo root=$(id -u); " +
            "( . /etc/armbian-release 2>/dev/null && echo armbian=$VERSION ); " + Cmds.DriveSummary +
            "for c in systemctl journalctl docker nmcli iw smartctl armbian-config dietpi-config apt-get dnf pacman ps ping; do " +
            "command -v $c >/dev/null 2>&1 && echo has_$c=1; done; true";

        public bool Tool(string n)
        {
            if (Unknown) return true;
            bool b; return Has.TryGetValue(n, out b) && b;
        }

        public string Package
        {
            get
            {
                if (Tool("apt-get")) return "apt";
                if (Tool("dnf")) return "dnf";
                if (Tool("pacman")) return "pacman";
                return null;
            }
        }

        public string Line
        {
            get { return Name + (Arch.Length > 0 ? "  \u00B7  " + Arch : "") + (Drives.Length > 0 && Drives != "none" ? "  \u00B7  " + Drives : ""); }
        }

        public static SysInfo Detect(string ip, string user)
        {
            string text = Ssh.Run(ip, user, Remote.Sh(Cmd, false), 20000);
            var s = new SysInfo();
            string pretty = "";
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                string k = line.Substring(0, i), v = line.Substring(i + 1).Trim();
                if (k == "name") pretty = v;
                else if (k == "id") s.Id = v;
                else if (k == "arch") s.Arch = v;
                else if (k == "root") s.Root = v == "0";
                else if (k == "armbian") s.ArmbianVersion = v;
                else if (k == "drives") s.Drives = v;
                else if (k.StartsWith("has_")) s.Has[k.Substring(4)] = v == "1";
            }
            if (pretty.Length > 0) s.Name = pretty;
            if (s.ArmbianVersion.Length > 0) s.Name = "Armbian " + s.ArmbianVersion + (pretty.Length > 0 ? " (" + pretty + ")" : "");
            return s;
        }
    }
}
