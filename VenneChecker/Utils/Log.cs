using BepInEx.Logging;

namespace VenneChecker
{
    /// <summary>
    /// Static logging helper that uses BepInEx's Logger (visible in LogOutput.log).
    /// Unity's Debug.Log does NOT appear in BepInEx logs on Unity 6000.
    /// </summary>
    public static class Log
    {
        private static ManualLogSource _log;

        public static void Init(ManualLogSource logger)
        {
            _log = logger;
        }

        public static void Info(string msg) => _log?.LogInfo(msg);
        public static void Warn(string msg) => _log?.LogWarning(msg);
        public static void Error(string msg) => _log?.LogError(msg);
    }
}
