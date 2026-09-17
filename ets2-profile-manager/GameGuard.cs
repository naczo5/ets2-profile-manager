using System.Diagnostics;

namespace Ets2ProfileManager
{
    /// <summary>Single safe implementation of the game-running check.</summary>
    internal static class GameGuard
    {
        public static bool IsRunning()
        {
            Process[] all;
            try
            {
                all = Process.GetProcesses();
            }
            catch
            {
                return false;
            }
            foreach (Process p in all)
            {
                try
                {
                    string name = p.ProcessName.ToLower();
                    if (name.Contains("eurotrucks2") || name.Contains("amtrucks"))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Protected or just-exited process: ignore and keep scanning.
                }
                finally
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return false;
        }
    }
}
