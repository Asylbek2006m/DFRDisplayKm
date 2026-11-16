using System;
using System.Threading;
using System.Windows.Forms;

namespace TouchBarStudio
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Single instance. A freshly restarted (elevated) copy waits a few seconds for the old one to exit.
            using (var mutex = new Mutex(false, "TouchBarStudio.SingleInstance"))
            {
                bool owned;
                try
                {
                    owned = mutex.WaitOne(TimeSpan.FromSeconds(5), false);
                }
                catch (AbandonedMutexException)
                {
                    owned = true;
                }

                if (!owned)
                {
                    return;
                }

                Application.SetHighDpiMode(HighDpiMode.SystemAware);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Loc.Set("auto");
                bool hidden = Array.IndexOf(args, "--hidden") >= 0;
                Application.Run(new MainForm(hidden));

                mutex.ReleaseMutex();
            }
        }
    }
}
