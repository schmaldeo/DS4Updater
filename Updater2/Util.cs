using System.Diagnostics;

namespace Updater2
{
    class Util
    {
        public static void StartProcessInExplorer(string path)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "explorer.exe";
            startInfo.Arguments = path;
            startInfo.UseShellExecute = true;
            try
            {
                using (Process temp = Process.Start(startInfo)) { }
            }
            catch { }
        }
    }
}
