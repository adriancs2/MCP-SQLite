using MCP_SQLite.Core;
using System;
using System.IO;
using System.Text;

namespace MCP_SQLite
{
    /// <summary>
    /// MCP SQLite — a console MCP server (JSON-RPC over stdin/stdout) that lets an
    /// AI agent create, inspect, query and modify SQLite database files.
    /// </summary>
    internal static class Program
    {
        private static void Main()
        {
            // stdin/stdout ARE the MCP transport. Force UTF-8 (no BOM) on the pipes
            // regardless of the host's console code page.
            var utf8 = new UTF8Encoding(false);
            try
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true });
                Console.SetIn(new StreamReader(Console.OpenStandardInput(), utf8));
            }
            catch { /* handles may not be redirectable when launched standalone */ }

            McpConfig.Load();

            var server = new McpServer();
            server.Run();               // blocks until the client closes stdin

            Environment.Exit(0);
        }
    }
}
