using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace OpiLink
{
    // Reboot / Shut down. Always asks first. After the command it watches the board's ssh port, so you see when it is gone / back.
    class PowerForm : ToolBase
    {
        RoundButton btnReboot, btnOff;
        BarProgress bar;
        StatusLabel lblStatus;
        bool busy;

        public PowerForm(string ip, string user, Icon icon)
            : base(ip, user, icon, "Reboot / Shutdown", new Size(480, 240))
        {
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Controls.Add(new Label { Left = 16, Top = 14, Width = 448, Height = 40, Text = "Both buttons ask before they do anything. Everything running on the board stops.", ForeColor = Color.FromArgb(90, 90, 90) });
            btnReboot = new RoundButton { Left = 16, Top = 62, Width = 215, Height = 56, Text = "Reboot", Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            btnReboot.Click += delegate { Power(true); };
            btnOff = new RoundButton { Left = 249, Top = 62, Width = 215, Height = 56, Text = "Shut down", Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            btnOff.Click += delegate { Power(false); };
            Controls.AddRange(new Control[] { btnReboot, btnOff });
            bar = new BarProgress { Left = 16, Top = 140, Width = 448, Height = 10 };
            Controls.Add(bar);
            lblStatus = new StatusLabel { Left = 16, Top = 160, Width = 448, Height = 60 };
            Controls.Add(lblStatus);
        }

        void SetBusy(bool on)
        {
            busy = on;
            bar.Marquee = on;
            btnReboot.Enabled = !on; btnOff.Enabled = !on;
        }

        void Power(bool reboot)
        {
            if (busy) return;
            string q = reboot
                ? "Reboot the board now?\n\nIt is back in about a minute."
                : "Shut the board down now?\n\nTo use it again you must switch its power on yourself.";
            if (Msg.Show(this, q, "Orange Pi Link", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            SetBusy(true);
            lblStatus.Text = "Sending the command...";
            // The command is started in the background on the board after 2 seconds, so ssh can finish normally before the board goes down.
            string verb = reboot ? "reboot" : "poweroff";
            string script =
                "[ -z \"$S\" ] || $S true 2>/dev/null || { echo 'This user cannot use sudo without a password. Log in as root.' >&2; exit 3; }; " +
                "(nohup sh -c \"sleep 2; $S " + verb + " || $S systemctl " + verb + "\" >/dev/null 2>&1 &); echo scheduled";
            var th = new Thread(delegate ()
            {
                string err = null;
                try { Ssh.Run(ip, user, Remote.Sh(script, true), 20000); }
                catch (Exception e) { err = e.Message; }
                if (err != null) { Ui(delegate { SetBusy(false); lblStatus.Text = "Not done: " + Hint(err); }); return; }
                Ui(delegate { lblStatus.Text = "Waiting for the board to go offline..."; });
                bool gone = false;
                for (int i = 0; i < 60 && !closing; i++)
                {
                    if (!Remote.PortOpen(ip, 22, 1000)) { gone = true; break; }
                    Thread.Sleep(1000);
                }
                if (closing) return;
                if (!gone) { Ui(delegate { SetBusy(false); lblStatus.Text = "The board is still answering. The command may not have worked."; }); return; }
                if (!reboot) { Ui(delegate { SetBusy(false); lblStatus.Text = "The board is off (" + DateTime.Now.ToString("HH:mm:ss") + ")."; }); return; }
                Ui(delegate { lblStatus.Text = "The board is restarting. Waiting for it to come back..."; });
                bool back = false;
                for (int i = 0; i < 240 && !closing; i++)
                {
                    if (Remote.PortOpen(ip, 22, 1000)) { back = true; break; }
                    Thread.Sleep(1000);
                }
                if (closing) return;
                Ui(delegate
                {
                    SetBusy(false);
                    lblStatus.Text = back ? "The board is back online (" + DateTime.Now.ToString("HH:mm:ss") + ")." : "The board did not come back within 4 minutes. Check it.";
                });
            });
            th.IsBackground = true; th.Start();
        }
    }
}
