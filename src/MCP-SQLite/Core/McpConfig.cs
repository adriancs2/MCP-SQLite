using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MCP_SQLite.Core
{
    /// <summary>
    /// Configuration loader. Reads mcp-config.json next to the executable and
    /// writes a default sample on first run. Every key is optional — a missing
    /// or malformed value falls back to its default.
    /// </summary>
    public static class McpConfig
    {
        /// <summary>Rows returned per result set when the caller does not pass max_rows.</summary>
        public static int DefaultMaxRows { get; private set; } = 50;

        /// <summary>How long a statement waits on a locked database before failing.</summary>
        public static int BusyTimeoutMs { get; private set; } = 5000;

        /// <summary>Per-statement command timeout.</summary>
        public static int CommandTimeoutSeconds { get; private set; } = 120;

        public static bool DebugLogging { get; private set; }

        public static string ConfigFilePath { get; private set; }

        private const string DefaultConfigJson = @"{
  ""default_max_rows"": 50,
  ""busy_timeout_ms"": 5000,
  ""command_timeout_seconds"": 120,
  ""debug_logging"": false
}";

        public static void Load()
        {
            ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-config.json");

            JObject config = null;
            try
            {
                if (!File.Exists(ConfigFilePath))
                    File.WriteAllText(ConfigFilePath, DefaultConfigJson, new System.Text.UTF8Encoding(false));
                config = JObject.Parse(File.ReadAllText(ConfigFilePath));
            }
            catch
            {
                // Read-only install folder or malformed JSON: run on defaults.
            }

            if (config != null)
            {
                DefaultMaxRows = Clamp(Int(config, "default_max_rows", DefaultMaxRows), 1, 100000);
                BusyTimeoutMs = Clamp(Int(config, "busy_timeout_ms", BusyTimeoutMs), 0, 600000);
                CommandTimeoutSeconds = Clamp(Int(config, "command_timeout_seconds", CommandTimeoutSeconds), 0, 86400);
                DebugLogging = Bool(config, "debug_logging", false);
            }
        }

        // Each value is read on its own so one bad entry cannot discard the rest.

        private static int Int(JObject o, string key, int fallback)
        {
            try
            {
                var t = o[key];
                return t == null || t.Type == JTokenType.Null ? fallback : t.Value<int>();
            }
            catch { return fallback; }
        }

        private static bool Bool(JObject o, string key, bool fallback)
        {
            try
            {
                var t = o[key];
                return t == null || t.Type == JTokenType.Null ? fallback : t.Value<bool>();
            }
            catch { return fallback; }
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
