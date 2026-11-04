namespace OpiLink
{
    // The shell scripts the tiles run on the board. All of them only read, except where the tile asks first (update, services, kill, power).
    // They run with "sh -c" and know $S (empty when root, "sudo -n" otherwise).
    static class Cmds
    {
        public const string Network =
            "echo '== Addresses =='; ip -br addr 2>/dev/null || ip addr 2>/dev/null || ifconfig 2>/dev/null; " +
            "echo; echo '== Gateway =='; ip route 2>/dev/null | grep '^default' || route -n 2>/dev/null; " +
            "echo; echo '== DNS =='; grep '^nameserver' /etc/resolv.conf 2>/dev/null; " +
            "echo; echo '== Wi-Fi =='; w=0; " +
            "for d in /sys/class/net/*/wireless; do [ -e \"$d\" ] || continue; w=1; n=$(basename $(dirname $d)); echo \"Adapter: $n\"; iw dev $n link 2>/dev/null || echo '(iw is not installed)'; done; " +
            "if command -v nmcli >/dev/null 2>&1; then nmcli -t -f ACTIVE,SSID,SIGNAL dev wifi 2>/dev/null | grep '^yes' | sed 's/^yes:/Connected (name:signal): /'; fi; " +
            "[ $w = 1 ] || echo 'No Wi-Fi adapter'; " +
            "echo; echo '== Internet =='; " +
            "if command -v ping >/dev/null 2>&1; then " +
            "if ping -c 2 -W 2 1.1.1.1 >/dev/null 2>&1; then echo 'Internet (by IP): OK'; else echo 'Internet (by IP): FAILED'; fi; " +
            "if ping -c 1 -W 2 example.com >/dev/null 2>&1; then echo 'Name lookup (DNS): OK'; else echo 'Name lookup (DNS): FAILED'; fi; " +
            "else echo 'ping is not installed'; fi";

        public const string Hardware =
            "rd() { cat \"$1\" 2>/dev/null || echo 0; }; " +
            "m=$(cat /proc/device-tree/model 2>/dev/null | tr -d '\\0'); echo \"Model:   ${m:-unknown}\"; " +
            "echo \"Kernel:  $(uname -srm)\"; echo; echo '== CPU =='; " +
            "lscpu 2>/dev/null | grep -E '^(Model name|CPU\\(s\\)|Architecture):'; " +
            "for p in /sys/devices/system/cpu/cpufreq/policy*; do [ -d \"$p\" ] || continue; echo \"$(basename $p): $(( $(rd $p/scaling_cur_freq) / 1000 )) MHz (max $(( $(rd $p/cpuinfo_max_freq) / 1000 )) MHz)\"; done; " +
            "echo; echo '== Temperatures =='; " +
            "for z in /sys/class/thermal/thermal_zone*; do [ -d \"$z\" ] || continue; echo \"$(cat $z/type 2>/dev/null): $(( $(rd $z/temp) / 1000 )) C\"; done; " +
            "echo; echo '== GPU / NPU / other clocks =='; " +
            "for d in /sys/class/devfreq/*; do [ -d \"$d\" ] || continue; echo \"$(basename $d): $(( $(rd $d/cur_freq) / 1000000 )) MHz\"; done; " +
            "echo; echo '== Memory =='; free -h 2>/dev/null";

        // One short line per drive kind for the status line: "SD/eMMC 29 GB, NVMe 476 GB". Reads /sys/block, so it works on every distro.
        public const string DriveSummary =
            "dv=; for d in /sys/block/*; do b=$(basename $d); case $b in loop*|ram*|zram*|dm-*|md*|sr*|nbd*|mmcblk*boot*|mmcblk*rpmb) continue;; esac; " +
            "gb=$(( $(cat $d/size 2>/dev/null || echo 0) * 512 / 1000000000 )); [ $gb -gt 0 ] || continue; rot=$(cat $d/queue/rotational 2>/dev/null || echo 0); " +
            "case $b in nvme*) k=NVMe;; mmcblk*) k='SD/eMMC';; *) if [ \"$rot\" = 1 ]; then k=HDD; else k=SSD; fi;; esac; " +
            "dv=\"$dv${dv:+, }$k $gb GB\"; done; echo drives=${dv:-none}; ";

        public const string Storage =
            "echo '== Detected drives =='; n=0; for d in /sys/block/*; do b=$(basename $d); " +
            "case $b in loop*|ram*|zram*|dm-*|md*|sr*|nbd*|mmcblk*boot*|mmcblk*rpmb) continue;; esac; " +
            "gb=$(( $(cat $d/size 2>/dev/null || echo 0) * 512 / 1000000000 )); [ $gb -gt 0 ] || continue; " +
            "rot=$(cat $d/queue/rotational 2>/dev/null || echo 0); " +
            "mod=$(cat $d/device/model 2>/dev/null | sed 's/ *$//'); [ -n \"$mod\" ] || mod=$(cat $d/device/name 2>/dev/null); " +
            "path=$(readlink -f $d 2>/dev/null); bus=; case \"$path\" in *usb*) bus=USB;; *nvme*) bus=NVMe;; *mmc*) bus=MMC;; *ata*) bus=SATA;; esac; " +
            "case $b in nvme*) kind='NVMe SSD';; mmcblk*) kind='SD card / eMMC';; *) if [ \"$rot\" = 1 ]; then kind='Hard disk (HDD)'; else kind='SSD / flash drive'; fi;; esac; " +
            "n=$((n+1)); echo \"/dev/$b  $kind  $gb GB  ${bus:+[$bus] }$mod\"; done; [ $n -gt 0 ] || echo 'No drives found'; " +
            "echo; echo '== Partitions =='; lsblk -o NAME,SIZE,TYPE,FSTYPE,MOUNTPOINT 2>/dev/null || cat /proc/partitions; " +
            "echo; echo '== Usage =='; df -h -x tmpfs -x devtmpfs -x squashfs 2>/dev/null || df -h; " +
            "echo; echo '== Health (SMART) =='; " +
            "if command -v smartctl >/dev/null 2>&1; then " +
            "found=0; for d in /dev/nvme?n1 /dev/sd?; do [ -e \"$d\" ] || continue; found=1; echo \"-- $d\"; " +
            "dt=; $S smartctl -i $d 2>&1 | grep -qiE 'unknown usb bridge|specify device type' && dt='-d sat'; " +
            "$S smartctl $dt -H $d 2>&1 | grep -iE 'result|status|overall|permission|denied|sudo'; " +
            "$S smartctl $dt -A $d 2>&1 | grep -iE 'temperature|percentage used|power.on.hours|reallocated'; done; " +
            "[ $found = 1 ] || echo 'No NVMe or SATA/USB disk found (SD cards have no SMART)'; " +
            "else echo 'smartctl is not installed. On Debian/Ubuntu/Armbian: sudo apt install smartmontools'; fi";

        public const string ServicesList =
            "{ systemctl list-unit-files --type=service --no-legend --plain --no-pager | awk '{print \"F\\t\"$1\"\\t\"$2}'; " +
            "systemctl list-units --type=service --all --no-legend --plain --no-pager | awk '{print \"U\\t\"$1\"\\t\"$3\"/\"$4}'; } | " +
            "awk -F'\\t' '$1==\"F\"{b[$2]=$3} $1==\"U\"{r[$2]=$3} END{for(k in b){print k\"\\t\"((k in r)?r[k]:\"inactive/dead\")\"\\t\"b[k]}}' | sort";

        public const string ProcessList =
            "ps -eo pid=,user=,pcpu=,pmem=,etime=,args= --sort=-pcpu | head -n 80 | " +
            "awk '{p=$1;u=$2;c=$3;m=$4;e=$5; $1=$2=$3=$4=$5=\"\"; sub(/^ +/,\"\"); print p\"\\t\"u\"\\t\"c\"\\t\"m\"\\t\"e\"\\t\"$0}'";

        // $D = docker, or "sudo -n docker" when this user may not talk to the docker daemon.
        public const string DockerPre = "if docker info >/dev/null 2>&1; then D=docker; else D=\"$S docker\"; fi; ";
        public const string DockerList = DockerPre + "$D ps -a --format '{{.Names}}\\t{{.Image}}\\t{{.Status}}\\t{{.Ports}}'";

        // $J = journalctl (with sudo when this user is not root and sudo works without a password), $K = dmesg the same way.
        public const string LogPre = "J=\"$S journalctl\"; K=\"$S dmesg\"; $S true >/dev/null 2>&1 || { J=journalctl; K=dmesg; }; ";

        public static string InstallDocker(string pm)
        {
            string tail = "; echo; if command -v docker >/dev/null 2>&1; then docker --version; echo 'Docker is installed. Close this window and press the Docker tile again.'; " +
                          "else echo 'Docker was NOT installed (see the messages above).'; exit 1; fi";
            if (pm == "apt")
                return "$S env DEBIAN_FRONTEND=noninteractive apt-get update && $S env DEBIAN_FRONTEND=noninteractive apt-get install -y docker.io && $S systemctl enable --now docker" + tail;
            if (pm == "dnf")
                return "$S dnf install -y moby-engine && $S systemctl enable --now docker" + tail;
            return "$S pacman -S --noconfirm docker && $S systemctl enable --now docker" + tail;
        }

        public static string Update(string pm, bool install)
        {
            if (pm == "apt")
                return install
                    ? "$S env DEBIAN_FRONTEND=noninteractive apt-get update && $S env DEBIAN_FRONTEND=noninteractive apt-get -y -o Dpkg::Options::=--force-confold upgrade && echo && echo 'Update finished.'"
                    : "$S apt-get update && echo && echo '== Packages that can be upgraded ==' && apt list --upgradable 2>/dev/null";
            if (pm == "dnf")
                return install ? "$S dnf -y upgrade && echo && echo 'Update finished.'" : "$S dnf check-update; true";
            return install ? "$S pacman -Syu --noconfirm && echo && echo 'Update finished.'" : "$S pacman -Qu || echo 'Nothing known to update (run the update to refresh the lists)'";
        }
    }
}
