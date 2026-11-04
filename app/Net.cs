using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace OpiLink
{
    class NetRange
    {
        public uint Net, Mask;
        public int Rank; // higher = scanned first (real Wi-Fi / Ethernet with a gateway)
    }

    class Found
    {
        public uint Addr;
        public string Ip, Host = "", Mac = "", Banner = "";
        public bool Ssh, Router, Alive;
    }

    // Finds the Orange Pi on the local network (the same list a modem shows under "connected devices"):
    // pings every address of the PC's own private network(s), looks for an open SSH port (22), reads the ARP table
    // for MAC addresses and asks DNS for host names. Only private networks are scanned, and only from this PC.
    static class Finder
    {
        static readonly Regex NameHint = new Regex("orange|opi|armbian", RegexOptions.IgnoreCase);
        static readonly Regex SafeHint = new Regex("^[a-z0-9-]{1,40}$", RegexOptions.IgnoreCase);
        static readonly Regex VirtualNic = new Regex("virtual|vmware|vbox|hyper-v|vethernet|bluetooth|loopback|wsl|docker|tap-|tailscale|zerotier|vpn|wintun|wireguard|openvpn|proton|nord|anyconnect|forti|zscaler|npcap|ppp", RegexOptions.IgnoreCase);
        static readonly Regex ArpLine = new Regex(@"^\s*(\d{1,3}(?:\.\d{1,3}){3})\s+([0-9a-fA-F]{2}(?:[-:][0-9a-fA-F]{2}){5})\s", RegexOptions.Multiline);
        static readonly Regex UserOk = new Regex("^[a-z_][a-z0-9_-]{0,31}$");
        const int MaxHosts = 1024;

        static uint ToUint(IPAddress a)
        {
            byte[] b = a.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        static string ToIp(uint v)
        {
            return ((v >> 24) & 255) + "." + ((v >> 16) & 255) + "." + ((v >> 8) & 255) + "." + (v & 255);
        }

        // 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16
        static bool IsPrivate(uint ip)
        {
            return (ip >> 24) == 10 || (ip >> 20) == 0xAC1 || (ip >> 16) == 0xC0A8;
        }

        // Runs work(item) for every item on a few threads; stops early when cancelled.
        static void ForEachParallel(List<uint> items, int threads, Func<bool> cancelled, Action<uint> work, Action<int> tick)
        {
            int next = -1, done = 0;
            var pool = new List<Thread>();
            for (int t = 0; t < threads; t++)
            {
                var th = new Thread(delegate ()
                {
                    while (!cancelled())
                    {
                        int i = Interlocked.Increment(ref next);
                        if (i >= items.Count) return;
                        try { work(items[i]); } catch (Exception) { }
                        tick(Interlocked.Increment(ref done));
                    }
                });
                th.IsBackground = true;
                th.Start();
                pool.Add(th);
            }
            foreach (Thread th in pool) th.Join();
        }

        // Port 22 open? Also reads the "SSH-2.0-..." banner when the server sends one.
        static bool TrySsh(string ip, int ms, out string banner)
        {
            banner = "";
            var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                IAsyncResult ar = s.BeginConnect(new IPEndPoint(IPAddress.Parse(ip), 22), null, null);
                if (!ar.AsyncWaitHandle.WaitOne(ms, false)) return false;
                s.EndConnect(ar);
                s.ReceiveTimeout = 1200;
                var buf = new byte[128];
                int n = 0;
                try { n = s.Receive(buf); } catch (Exception) { }
                if (n > 0)
                {
                    string line = Encoding.ASCII.GetString(buf, 0, n).Split('\n')[0];
                    banner = Regex.Replace(line, @"[^\x20-\x7E]", "").Trim();
                }
                return true;
            }
            catch (Exception) { return false; }
            finally { try { s.Close(); } catch (Exception) { } }
        }

        // DNS lookup with a time limit (a blocked lookup must not freeze the scan).
        static IPHostEntry Lookup(string nameOrIp, int ms)
        {
            IPHostEntry e = null;
            var t = new Thread(delegate () { try { e = Dns.GetHostEntry(nameOrIp); } catch (Exception) { } });
            t.IsBackground = true;
            t.Start();
            return t.Join(ms) ? e : null;
        }

        static Dictionary<uint, string> ReadArp()
        {
            var map = new Dictionary<uint, string>();
            try
            {
                var psi = new ProcessStartInfo("arp", "-a") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                using (Process p = Process.Start(psi))
                {
                    string text = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(5000);
                    foreach (Match m in ArpLine.Matches(text))
                    {
                        IPAddress ip;
                        if (!IPAddress.TryParse(m.Groups[1].Value, out ip)) continue;
                        string mac = m.Groups[2].Value.ToLowerInvariant().Replace('-', ':');
                        if (mac == "ff:ff:ff:ff:ff:ff" || mac.StartsWith("01:00:5e")) continue;
                        map[ToUint(ip)] = mac;
                    }
                }
            }
            catch (Exception) { }
            return map;
        }

        // hint = board id from the catalog (e.g. "orangepi5-plus"), used to try the name "<hint>.local".
        // Returns a list of { ip, host, mac, ssh, banner, likely, router }.
        public static object Scan(string hint, Func<bool> cancelled, Action<int> onProgress)
        {
            onProgress(0);
            var own = new List<uint>();
            var gateways = new List<uint>();
            var nets = new List<NetRange>();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (VirtualNic.IsMatch(ni.Description ?? "") || VirtualNic.IsMatch(ni.Name ?? "")) continue;
                IPInterfaceProperties props = ni.GetIPProperties();
                int rank = 0;
                foreach (GatewayIPAddressInformation g in props.GatewayAddresses)
                    if (g.Address.AddressFamily == AddressFamily.InterNetwork) { gateways.Add(ToUint(g.Address)); rank = 2; }
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet || ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) rank += 1;
                foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                {
                    if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    uint ip = ToUint(u.Address);
                    if (!IsPrivate(ip)) continue;
                    int prefix = u.PrefixLength;
                    if (prefix < 24 || prefix > 30) prefix = 24; // bigger or odd networks: only the /24 around this PC
                    uint mask = uint.MaxValue << (32 - prefix);
                    own.Add(ip);
                    bool known = false;
                    foreach (NetRange n in nets) if (n.Net == (ip & mask) && n.Mask == mask) known = true;
                    if (!known) nets.Add(new NetRange { Net = ip & mask, Mask = mask, Rank = rank });
                }
            }
            if (nets.Count == 0)
                throw new InvalidOperationException("This PC is not connected to a private network (Wi-Fi or Ethernet). Connect it to the same modem as the Orange Pi.");

            // The PC's real network (Wi-Fi / Ethernet with a gateway) is scanned first, so a VPN or virtual adapter can never use up the host limit.
            nets.Sort(delegate (NetRange x, NetRange y) { return y.Rank.CompareTo(x.Rank); });
            var hosts = new List<uint>();
            foreach (NetRange n in nets)
            {
                uint first = n.Net + 1, last = (n.Net | ~n.Mask) - 1;
                for (uint h = first; h <= last && hosts.Count < MaxHosts; h++)
                    if (!own.Contains(h) && !hosts.Contains(h)) hosts.Add(h);
            }

            // Try "<board>.local" (mDNS) in the background while the sweep runs.
            var named = new List<KeyValuePair<string, uint>>();
            Thread mdns = null;
            if (SafeHint.IsMatch(hint ?? ""))
            {
                string h1 = hint.ToLowerInvariant();
                string[] names = h1.IndexOf('-') >= 0 ? new[] { h1, h1.Replace("-", "") } : new[] { h1 };
                mdns = new Thread(delegate ()
                {
                    foreach (string nm in names)
                    {
                        IPHostEntry e = Lookup(nm + ".local", 2500);
                        if (e == null) continue;
                        foreach (IPAddress a in e.AddressList)
                            if (a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(ToUint(a)))
                                lock (named) named.Add(new KeyValuePair<string, uint>(nm, ToUint(a)));
                    }
                });
                mdns.IsBackground = true;
                mdns.Start();
            }

            // Phase 1 (0-70%): ping + SSH port on every address.
            var found = new Dictionary<uint, Found>();
            int total = Math.Max(1, hosts.Count);
            ForEachParallel(hosts, 48, cancelled, delegate (uint addr)
            {
                string ip = ToIp(addr);
                var f = new Found { Addr = addr, Ip = ip };
                try { using (var p = new Ping()) { PingReply r = p.Send(ip, 700); f.Alive = r != null && r.Status == IPStatus.Success; } } catch (Exception) { }
                string banner;
                if (TrySsh(ip, 700, out banner)) { f.Ssh = true; f.Alive = true; f.Banner = banner; }
                if (f.Alive) lock (found) found[addr] = f;
            }, delegate (int done) { onProgress(done * 70 / total); });
            if (cancelled()) throw new OperationCanceledException("Cancelled by user");

            // Hosts that ignore ping but answered ARP (the sweep made Windows ask for them).
            onProgress(72);
            Dictionary<uint, string> arp = ReadArp();
            foreach (KeyValuePair<uint, string> kv in arp)
            {
                if (!hosts.Contains(kv.Key)) continue;
                uint last = kv.Key & 255;
                if (last == 0 || last == 255) continue;
                Found f;
                lock (found)
                {
                    if (!found.TryGetValue(kv.Key, out f)) { f = new Found { Addr = kv.Key, Ip = ToIp(kv.Key), Alive = true }; found[kv.Key] = f; }
                }
                f.Mac = kv.Value;
            }
            if (mdns != null) mdns.Join(6000);
            lock (named)
                foreach (KeyValuePair<string, uint> kv in named)
                {
                    Found f;
                    lock (found)
                    {
                        if (!found.TryGetValue(kv.Value, out f)) { f = new Found { Addr = kv.Value, Ip = ToIp(kv.Value), Alive = true }; found[kv.Value] = f; }
                    }
                    f.Host = kv.Key + ".local";
                }

            // Phase 2 (75-100%): host names.
            var addrs = new List<uint>(found.Keys);
            ForEachParallel(addrs, 16, cancelled, delegate (uint addr)
            {
                Found f = found[addr];
                if (f.Host.Length > 0) return;
                IPHostEntry e = Lookup(f.Ip, 2500);
                if (e != null && !string.IsNullOrEmpty(e.HostName) && e.HostName != f.Ip) f.Host = e.HostName;
            }, delegate (int done) { onProgress(75 + done * 25 / Math.Max(1, addrs.Count)); });
            if (cancelled()) throw new OperationCanceledException("Cancelled by user");

            // Keep devices with SSH, or whose name looks like an Orange Pi. The router is shown but never ranked first.
            var list = new List<Found>();
            foreach (Found f in found.Values)
            {
                f.Router = gateways.Contains(f.Addr);
                if (f.Ssh || NameHint.IsMatch(f.Host)) list.Add(f);
            }
            list.Sort(delegate (Found x, Found y)
            {
                int sx = Score(x), sy = Score(y);
                return sx != sy ? sy.CompareTo(sx) : x.Addr.CompareTo(y.Addr);
            });
            var result = new List<object>();
            foreach (Found f in list)
            {
                if (result.Count >= 50) break;
                result.Add(new Dictionary<string, object> {
                    { "ip", f.Ip }, { "host", f.Host }, { "mac", f.Mac }, { "ssh", f.Ssh }, { "banner", f.Banner },
                    { "router", f.Router }, { "likely", f.Ssh && NameHint.IsMatch(f.Host) && !f.Router }
                });
            }
            onProgress(100);
            return result;
        }

        static int Score(Found f)
        {
            int s = 0;
            if (f.Ssh) s += 2;
            if (NameHint.IsMatch(f.Host)) s += 4;
            if (f.Router) s -= 3;
            return s;
        }

        // Opens a console window with "ssh user@ip". Both values are checked, so nothing else can reach the command line.
        public static void OpenSsh(string ip, string user, bool forgetKey)
        {
            IPAddress parsed;
            if (!IPAddress.TryParse(ip, out parsed) || parsed.AddressFamily != AddressFamily.InterNetwork || !IsPrivate(ToUint(parsed)))
                throw new ArgumentException("Only an address on your own network can be opened");
            if (user == null || !UserOk.IsMatch(user)) throw new ArgumentException("The user name can only have lowercase letters, digits, _ and -");
            string addr = parsed.ToString();
            if (forgetKey)
            {
                // After a new burn the board has a new host key: remove the old one saved for this IP (ssh-keygen keeps a known_hosts.old backup).
                try
                {
                    var kg = new ProcessStartInfo("ssh-keygen", "-R " + addr) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (Process p = Process.Start(kg)) { p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(5000); }
                }
                catch (Exception) { }
            }
            var psi = new ProcessStartInfo("cmd.exe", "/k ssh " + user + "@" + addr) { UseShellExecute = true };
            Process.Start(psi);
        }
    }
}
