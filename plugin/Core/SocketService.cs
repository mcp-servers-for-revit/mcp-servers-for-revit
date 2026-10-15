using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitMCPSDK.API.Models.JsonRPC;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Configuration;
using revit_mcp_plugin.Utils;

namespace revit_mcp_plugin.Core
{
    public class SocketService
    {
        // Far larger than any real request; it only stops a misbehaving client from exhausting memory.
        private const int MaxMessageLength = 16 * 1024 * 1024;

        // JSON-RPC reserves -32000 to -32099 for server-defined errors.
        private const int UnauthorizedErrorCode = -32001;

        private static SocketService _instance;
        private TcpListener _listener;
        private string _token;
        private Thread _listenerThread;
        private bool _isRunning;
        private int _port = 8080;
        private UIApplication _uiApp;
        private ICommandRegistry _commandRegistry;
        private ILogger _logger;
        private CommandExecutor _commandExecutor;

        public static SocketService Instance
        {
            get
            {
                if(_instance == null)
                    _instance = new SocketService();
                return _instance;
            }
        }

        private SocketService()
        {
            _commandRegistry = new RevitCommandRegistry();
            _logger = new Logger();
        }

        public bool IsRunning => _isRunning;

        public int Port
        {
            get => _port;
            set => _port = value;
        }

        // 初始化
        // Initialization.
        public void Initialize(UIApplication uiApp)
        {
            _uiApp = uiApp;

            // 初始化事件管理器
            // Initialize ExternalEventManager
            ExternalEventManager.Instance.Initialize(uiApp, _logger);

            // 记录当前 Revit 版本
            // Get the current Revit version.
            var versionAdapter = new RevitMCPSDK.API.Utils.RevitVersionAdapter(_uiApp.Application);
            string currentVersion = versionAdapter.GetRevitVersion();
            _logger.Info("当前 Revit 版本: {0}\nCurrent Revit version: {0}", currentVersion);



            // 创建命令执行器
            // Create CommandExecutor
            _commandExecutor = new CommandExecutor(_commandRegistry, _logger);

            // 加载配置并注册命令
            // Load configuration and register commands.
            ConfigurationManager configManager = new ConfigurationManager(_logger);
            configManager.LoadConfiguration();
            

            //// 从配置中读取服务端口
            //// Read the service port from the configuration.
            //if (configManager.Config.Settings.Port > 0)
            //{
            //    _port = configManager.Config.Settings.Port;
            //}
            _port = 8080; // 固定端口号 - Hard-wired port number.

            // 加载命令
            // Load command.
            CommandManager commandManager = new CommandManager(
                _commandRegistry, _logger, configManager, _uiApp);
            commandManager.LoadCommands();

            _logger.Info($"Socket service initialized on port {_port}");
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                _isRunning = true;

                // Loopback only: send_code_to_revit runs arbitrary C#, so nothing on the network may reach this port.
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();

                // A fresh token per session stops other local users (e.g. on a shared Citrix host) from sending commands.
                _token = GenerateToken();
                ConnectionInfoFile.Write(_port, _token);

                _listenerThread = new Thread(ListenForClients)
                {
                    IsBackground = true
                };
                _listenerThread.Start();
            }
            catch (Exception)
            {
                _isRunning = false;
                _listener?.Stop();
                _listener = null;

                // Let the ribbon button report the failure (e.g. port already in use) instead of claiming success.
                throw;
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _isRunning = false;

                _listener?.Stop();
                _listener = null;

                ConnectionInfoFile.Delete();
                _token = null;

                if(_listenerThread!=null && _listenerThread.IsAlive)
                {
                    _listenerThread.Join(1000);
                }
            }
            catch (Exception)
            {
                // log error
            }
        }

        private void ListenForClients()
        {
            try
            {
                while (_isRunning)
                {
                    TcpClient client = _listener.AcceptTcpClient();

                    Thread clientThread = new Thread(HandleClientCommunication)
                    {
                        IsBackground = true
                    };
                    clientThread.Start(client);
                }
            }
            catch (SocketException)
            {
                
            }
            catch(Exception)
            {
                // log
            }
        }

        private void HandleClientCommunication(object clientObj)
        {
            TcpClient tcpClient = (TcpClient)clientObj;
            NetworkStream stream = tcpClient.GetStream();

            try
            {
                byte[] buffer = new byte[8192];
                var framer = new MessageFramer(MaxMessageLength);

                while (_isRunning && tcpClient.Connected)
                {
                    // 读取客户端消息
                    // Read client messages.
                    int bytesRead = 0;

                    try
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (IOException)
                    {
                        // 客户端断开连接
                        // Client disconnected.
                        break;
                    }

                    if (bytesRead == 0)
                    {
                        // 客户端断开连接
                        // Client disconnected.
                        break;
                    }

                    List<string> messages;
                    try
                    {
                        messages = framer.Append(buffer, bytesRead);
                    }
                    catch (InvalidDataException ex)
                    {
                        WriteResponse(stream, CreateErrorResponse(null, JsonRPCErrorCodes.InvalidRequest, ex.Message));
                        break;
                    }

                    foreach (string message in messages)
                    {
                        System.Diagnostics.Trace.WriteLine($"收到消息: {message}\nReceived message: {message}");

                        string response = ProcessJsonRPCRequest(message, out bool closeConnection);

                        // 发送响应
                        // Send response.
                        WriteResponse(stream, response);

                        if (closeConnection)
                        {
                            return;
                        }
                    }
                }
            }
            catch(Exception)
            {
                // log
            }
            finally
            {
                tcpClient.Close();
            }
        }

        private string ProcessJsonRPCRequest(string requestJson, out bool closeConnection)
        {
            closeConnection = false;
            JsonRPCRequest request;

            try
            {
                // 解析JSON-RPC请求
                // Parse JSON-RPC requests.
                JObject requestObject = JObject.Parse(requestJson);

                // "auth" isn't part of JSON-RPC, so it's checked here before the request is handed to a command.
                if (!IsAuthorized((requestObject["auth"] as JValue)?.Value as string))
                {
                    _logger.Warning("Rejected a request without a valid session token.");
                    closeConnection = true;
                    return CreateErrorResponse(
                        (requestObject["id"] as JValue)?.Value?.ToString(),
                        UnauthorizedErrorCode,
                        "Unauthorized: the request did not include this Revit session's token. " +
                        "Update the MCP server to the latest mcp-server-for-revit release; it reads the token from " +
                        ConnectionInfoFile.FilePath + "."
                    );
                }

                request = requestObject.ToObject<JsonRPCRequest>();

                // 验证请求格式是否有效
                // Verify that the request format is valid.
                if (request == null || !request.IsValid())
                {
                    return CreateErrorResponse(
                        null,
                        JsonRPCErrorCodes.InvalidRequest,
                        "Invalid JSON-RPC request"
                    );
                }

                // 查找命令
                // Search for the command in the registry.
                if (!_commandRegistry.TryGetCommand(request.Method, out var command))
                {
                    return CreateErrorResponse(request.Id, JsonRPCErrorCodes.MethodNotFound,
                        $"Method '{request.Method}' not found");
                }

                // 执行命令
                // Execute command.
                try
                {                
                    object result = command.Execute(request.GetParamsObject(), request.Id);

                    return CreateSuccessResponse(request.Id, result);
                }
                catch (Exception ex)
                {
                    return CreateErrorResponse(request.Id, JsonRPCErrorCodes.InternalError, ex.Message);
                }
            }
            catch (JsonException)
            {
                // JSON解析错误
                // JSON parsing error.

                // Anything that isn't JSON, such as an HTTP request a web page sent to this port, ends the connection.
                closeConnection = true;
                return CreateErrorResponse(
                    null,
                    JsonRPCErrorCodes.ParseError,
                    "Invalid JSON"
                );
            }
            catch (Exception ex)
            {
                // 处理请求时的其他错误
                // Catch other errors produced when processing requests.
                return CreateErrorResponse(
                    null,
                    JsonRPCErrorCodes.InternalError,
                    $"Internal error: {ex.Message}"
                );
            }
        }

        private string CreateSuccessResponse(string id, object result)
        {
            var response = new JsonRPCSuccessResponse
            {
                Id = id,
                Result = result is JToken jToken ? jToken : JToken.FromObject(result)
            };

            return response.ToJson();
        }

        private string CreateErrorResponse(string id, int code, string message, object data = null)
        {
            var response = new JsonRPCErrorResponse
            {
                Id = id,
                Error = new JsonRPCError
                {
                    Code = code,
                    Message = message,
                    Data = data != null ? JToken.FromObject(data) : null
                }
            };

            return response.ToJson();
        }

        private bool IsAuthorized(string token)
        {
            string expected = _token;
            if (token == null || expected == null || token.Length != expected.Length)
            {
                return false;
            }

            // Compare every character so response timing doesn't reveal how much of the token matched.
            int difference = 0;
            for (int i = 0; i < token.Length; i++)
            {
                difference |= token[i] ^ expected[i];
            }
            return difference == 0;
        }

        private static string GenerateToken()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        private static void WriteResponse(NetworkStream stream, string response)
        {
            // The SDK emits compact JSON today, but its version floats. JSON escapes line breaks inside strings,
            // so any raw line break is formatting and can be dropped to keep the response on one line.
            string line = response.Replace("\r", "").Replace("\n", "") + "\n";
            byte[] responseData = Encoding.UTF8.GetBytes(line);
            stream.Write(responseData, 0, responseData.Length);
        }
    }
}
