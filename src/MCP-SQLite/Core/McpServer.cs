using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MCP_SQLite.Core
{
    /// <summary>
    /// MCP server handling JSON-RPC 2.0 over stdin/stdout (one JSON message per line).
    /// </summary>
    public class McpServer
    {
        public const string ServerName = "MCP SQLite";

        // Newest first. The tool surface is identical under all of them, so the
        // client's requested revision is simply echoed back when it is known.
        private static readonly string[] SupportedProtocolVersions =
            { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

        private readonly Dictionary<string, ITool> _tools;
        private readonly object _writeLock = new object();
        private StreamWriter _logWriter;
        private bool _initialized;

        public McpServer()
        {
            _tools = ToolDiscovery.DiscoverTools(type => (ITool)Activator.CreateInstance(type));

            if (McpConfig.DebugLogging)
            {
                try
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp_debug.log");
                    _logWriter = new StreamWriter(logPath, append: true, encoding: Encoding.UTF8) { AutoFlush = true };
                    LogDebug($"=== {ServerName} server started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                    LogDebug($"Discovered {_tools.Count} tools");
                }
                catch { }
            }
        }

        /// <summary>Reads requests until the client closes stdin, then returns.</summary>
        public void Run()
        {
            while (true)
            {
                string line;
                try
                {
                    line = Console.In.ReadLine();
                }
                catch (Exception ex)
                {
                    LogDebug($"stdin read error: {ex.Message}");
                    break;
                }

                if (line == null)
                {
                    LogDebug("EOF received, shutting down");
                    break;
                }
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    LogDebug($"Received: {line}");
                    var request = JsonConvert.DeserializeObject<JsonRpcRequest>(line);
                    HandleRequest(request);
                }
                catch (JsonException ex)
                {
                    LogDebug($"JSON parse error: {ex.Message}");
                    SendError(null, JsonRpcErrorCodes.ParseError, "Parse error", ex.Message);
                }
                catch (Exception ex)
                {
                    LogDebug($"Unexpected error: {ex.Message}");
                    SendError(null, JsonRpcErrorCodes.InternalError, "Internal error", ex.ToString());
                }
            }
        }

        private void HandleRequest(JsonRpcRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Method))
            {
                SendError(request?.Id, JsonRpcErrorCodes.InvalidRequest, "Invalid request", null);
                return;
            }

            // A message without an id is a notification: JSON-RPC forbids replying to it.
            bool isNotification = request.Id == null;

            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        HandleInitialize(request);
                        break;
                    case "tools/list":
                        HandleToolsList(request);
                        break;
                    case "tools/call":
                        HandleToolCall(request);
                        break;
                    case "ping":
                        if (!isNotification) SendResponse(request.Id, new { });
                        break;
                    default:
                        if (!isNotification)
                            SendError(request.Id, JsonRpcErrorCodes.MethodNotFound, $"Method not found: {request.Method}", null);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error handling {request.Method}: {ex.Message}");
                if (!isNotification)
                    SendError(request.Id, JsonRpcErrorCodes.InternalError, "Internal error", ex.Message);
            }
        }

        private void HandleInitialize(JsonRpcRequest request)
        {
            _initialized = true;

            string requested = request.Params?.Value<string>("protocolVersion");
            string version = SupportedProtocolVersions.Contains(requested)
                ? requested
                : SupportedProtocolVersions[0];

            string instructions = null;
            string instructionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system-prompts.txt");
            if (File.Exists(instructionsPath))
            {
                try
                {
                    instructions = File.ReadAllText(instructionsPath, Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(instructions)) instructions = null;
                }
                catch { }
            }

            var v = Assembly.GetExecutingAssembly().GetName().Version;

            SendResponse(request.Id, new InitializeResult
            {
                ProtocolVersion = version,
                Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
                ServerInfo = new ServerInfo { Name = ServerName, Version = $"{v.Major}.{v.Minor}.{v.Build}" },
                Instructions = instructions
            });
        }

        private void HandleToolsList(JsonRpcRequest request)
        {
            if (!_initialized)
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidRequest, "Server not initialized", null);
                return;
            }
            var definitions = ToolDiscovery.GenerateToolDefinitions(_tools);
            SendResponse(request.Id, new ToolsListResult { Tools = definitions.OrderBy(d => d.Name).ToList() });
        }

        private void HandleToolCall(JsonRpcRequest request)
        {
            if (!_initialized)
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidRequest, "Server not initialized", null);
                return;
            }

            string toolName = request.Params?.Value<string>("name");
            if (string.IsNullOrEmpty(toolName))
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidParams, "Tool name is required", null);
                return;
            }

            if (!_tools.TryGetValue(toolName, out ITool tool))
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidParams, $"Unknown tool: {toolName}", null);
                return;
            }

            var args = request.Params["arguments"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();

            ToolResult toolResult;
            try
            {
                toolResult = tool.Execute(args);
            }
            catch (ArgumentException ex)
            {
                toolResult = ToolResult.Error("INVALID_PARAMS", ex.Message);
            }
            catch (FormatException ex)
            {
                toolResult = ToolResult.Error("INVALID_PARAMS", ex.Message);
            }
            catch (Exception ex)
            {
                LogDebug($"Tool execution error: {ex}");
                toolResult = ToolResult.Error("INTERNAL_ERROR", ex.Message);
            }

            SendResponse(request.Id, new ToolCallResult
            {
                Content = toolResult.ContentList.Select(c => new ToolContentItem
                {
                    Type = c.Type,
                    Text = c.Text,
                    Data = c.Data,
                    MimeType = c.MimeType
                }).ToList(),
                IsError = toolResult.IsError ? (bool?)true : null
            });
        }

        private void SendResponse(object id, object result)
        {
            var response = new JsonRpcResponse { Id = id, Result = result };
            string json = JsonConvert.SerializeObject(response);
            LogDebug($"Send: {(json.Length > 400 ? json.Substring(0, 400) + "..." : json)}");
            WriteLine(json);
        }

        private void SendError(object id, int code, string message, object data)
        {
            var response = new JsonRpcResponse { Id = id, Error = new JsonRpcError { Code = code, Message = message, Data = data } };
            WriteLine(JsonConvert.SerializeObject(response));
        }

        private void WriteLine(string json)
        {
            lock (_writeLock)
            {
                Console.Out.Write(json);
                Console.Out.Write('\n');
                Console.Out.Flush();
            }
        }

        private void LogDebug(string message)
        {
            if (_logWriter != null)
            {
                try { _logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}"); } catch { }
            }
        }
    }
}
