using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Copixy")]
[assembly: AssemblyProduct("Copixy")]
[assembly: AssemblyDescription("Screen capture tool")]
[assembly: AssemblyCompany("Deformarium")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

namespace Copixy
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Deformarium.Copixy.Instance", out created))
            using (var wake = new EventWaitHandle(false, EventResetMode.AutoReset, "Deformarium.Copixy.Capture"))
            {
                if (!created)
                {
                    // Already running — ask that instance to take a screenshot.
                    wake.Set();
                    return;
                }

                bool fromAutostart = Array.Exists(args, a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

                Native.InitDpi();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Res.InitFonts();
                Application.Run(new TrayApp(wake, fromAutostart));
                GC.KeepAlive(mutex);
            }
        }
    }
}
