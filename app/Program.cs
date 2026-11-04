using System;
using System.Windows.Forms;

namespace OpiLink
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                Theme.Load();
                L.Init(AppDomain.CurrentDomain.BaseDirectory);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new LinkForm(AppDomain.CurrentDomain.BaseDirectory));
                return 0;
            }
            catch (Exception e)
            {
                Msg.Show(e.Message, "Orange Pi Link", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
