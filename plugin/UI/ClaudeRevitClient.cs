using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace revit_mcp_plugin.UI
{
    public class ClaudeRevitClient
    {
        private readonly List<JObject> _conversationHistory = new List<JObject>();
        private string _apiKey;
        private const string ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
        private string _model = "claude-sonnet-4-6";
        private const int MCP_PORT = 8080;
        private CancellationTokenSource _cts;

        public void Cancel()
        {
            _cts?.Cancel();
        }

        private const string SYSTEM_PROMPT = @"Sei Claude, un assistente AI integrato direttamente in Autodesk Revit. Hai accesso a tool che eseguono comandi sul modello Revit attivo in tempo reale.

REGOLE:
1. Quando l'utente ti chiede di fare qualcosa sul modello, USA I TOOL disponibili per eseguirlo. Non limitarti a descrivere — esegui l'azione.
2. Se ti mancano informazioni necessarie per eseguire un comando (es. quale livello, quale tipo di muro, quali elementi, dove posizionare), CHIEDI ALL'UTENTE prima di procedere. Non indovinare parametri critici.
3. Per scoprire cosa è disponibile nel modello, usa prima i tool di lettura: get_available_family_types, get_project_info (per livelli), get_current_view_elements, get_selected_elements. Poi proponi all'utente le opzioni trovate.
4. Se l'utente dice 'gli elementi selezionati' o 'la selezione corrente', usa get_selected_elements. Se il risultato è vuoto, chiedi all'utente di selezionare qualcosa in Revit.
5. Dopo aver eseguito un tool, descrivi brevemente il risultato con gli ID degli elementi creati/modificati.

Rispondi in italiano, sii conciso.

Coordinate: tutti i valori sono in millimetri (mm).
Categorie comuni: OST_Walls, OST_Floors, OST_Doors, OST_Windows, OST_StructuralColumns, OST_StructuralFraming, OST_Rooms.
Fai sempre riferimento alle API e ai comandi di Revit quando spieghi cosa stai facendo.";

        private bool _thinkingEnabled;
        private int _thinkingBudget = 10000;

        public string Model
        {
            get => _model;
            set => _model = value;
        }

        public bool ThinkingEnabled
        {
            get => _thinkingEnabled;
            set => _thinkingEnabled = value;
        }

        public int ThinkingBudget
        {
            get => _thinkingBudget;
            set => _thinkingBudget = value;
        }

        public ClaudeRevitClient()
        {
            LoadApiKey();
        }

        private void LoadApiKey()
        {
            string claudeDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

            // Try Anthropic API key (env → file)
            _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrEmpty(_apiKey))
            {
                try
                {
                    string path = Path.Combine(claudeDir, "api_key.txt");
                    if (File.Exists(path)) _apiKey = File.ReadAllText(path).Trim();
                }
                catch { }
            }
        }

        public async Task<string> SendMessage(string userMessage)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                return "API key not configured.\n\n" +
                       "Set your Anthropic API key:\n" +
                       "  File: %USERPROFILE%\\.claude\\api_key.txt\n" +
                       "  or env variable: ANTHROPIC_API_KEY=sk-ant-...";
            }

            _conversationHistory.Add(new JObject { ["role"] = "user", ["content"] = userMessage });

            while (_conversationHistory.Count > 30)
                _conversationHistory.RemoveAt(0);

            _cts = new CancellationTokenSource();

            try
            {
                return await ProcessConversation();
            }
            catch (OperationCanceledException)
            {
                return "Operazione annullata.";
            }
            catch (Exception ex)
            {
                return $"Errore: {ex.Message}";
            }
        }

        private async Task<string> ProcessConversation()
        {
            int maxToolRounds = 15;

            for (int round = 0; round < maxToolRounds; round++)
            {
                var response = await CallClaudeApi();

                if (response == null)
                    return "Nessuna risposta dall'API.";

                var content = response["content"] as JArray;
                if (content == null) return "Risposta vuota.";

                // Check stop reason
                string stopReason = response["stop_reason"]?.ToString() ?? "end_turn";

                // Collect thinking, text and tool_use blocks
                var thinkingParts = new List<string>();
                var textParts = new List<string>();
                var toolUses = new List<JObject>();

                foreach (var block in content)
                {
                    string blockType = block["type"]?.ToString();
                    if (blockType == "thinking")
                        thinkingParts.Add(block["thinking"]?.ToString() ?? "");
                    else if (blockType == "text")
                        textParts.Add(block["text"]?.ToString() ?? "");
                    else if (blockType == "tool_use")
                        toolUses.Add((JObject)block);
                }

                // Notify panel about thinking content
                if (thinkingParts.Count > 0)
                    MCPDockablePanel.Instance?.OnThinkingReceived(string.Join("\n", thinkingParts));

                // Add assistant message to history
                _conversationHistory.Add(new JObject { ["role"] = "assistant", ["content"] = content });

                // Show intermediate text (Claude's reasoning between tool calls)
                if (textParts.Count > 0 && toolUses.Count > 0)
                    MCPDockablePanel.Instance?.OnIntermediateText(string.Join("\n", textParts));

                if (stopReason == "tool_use" && toolUses.Count > 0)
                {
                    // Execute tools and add results
                    var toolResults = new JArray();
                    foreach (var toolUse in toolUses)
                    {
                        string toolName = toolUse["name"]?.ToString();
                        string toolId = toolUse["id"]?.ToString();
                        JObject toolInput = toolUse["input"] as JObject ?? new JObject();

                        // Notify panel about tool execution
                        MCPDockablePanel.Instance?.OnToolExecuting(toolName);

                        string result = await ExecuteMcpCommand(toolName, toolInput);
                        bool isError = result.StartsWith("Error:") || result.StartsWith("MCP command failed:");

                        // Notify panel about tool result
                        MCPDockablePanel.Instance?.OnToolCompleted(toolName, isError, result);

                        toolResults.Add(new JObject
                        {
                            ["type"] = "tool_result",
                            ["tool_use_id"] = toolId,
                            ["content"] = result
                        });
                    }

                    _conversationHistory.Add(new JObject { ["role"] = "user", ["content"] = toolResults });

                    // Show round progress
                    MCPDockablePanel.Instance?.OnRoundProgress(round + 1, maxToolRounds);

                    continue; // Loop to get Claude's response after tool results
                }

                // No more tool calls — return text
                return string.Join("\n", textParts);
            }

            return "Troppe iterazioni di tool. Riprova con una richiesta più semplice.";
        }

        private async Task<JObject> CallClaudeApi()
        {
            var requestBody = new JObject
            {
                ["model"] = _model,
                ["max_tokens"] = _thinkingEnabled ? 16000 : 2048,
                ["system"] = SYSTEM_PROMPT,
                ["tools"] = GetToolDefinitions(),
                ["messages"] = JArray.FromObject(_conversationHistory)
            };

            if (_thinkingEnabled)
            {
                requestBody["thinking"] = new JObject
                {
                    ["type"] = "enabled",
                    ["budget_tokens"] = _thinkingBudget
                };
            }

            byte[] data = Encoding.UTF8.GetBytes(requestBody.ToString());
            int maxRetries = 3;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                _cts?.Token.ThrowIfCancellationRequested();

                var request = (HttpWebRequest)WebRequest.Create(ANTHROPIC_URL);
                request.Method = "POST";
                request.ContentType = "application/json";
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                request.Timeout = _thinkingEnabled ? 180000 : 60000;

                using (var stream = await Task.Factory.FromAsync(request.BeginGetRequestStream, request.EndGetRequestStream, null))
                {
                    await stream.WriteAsync(data, 0, data.Length);
                }

                try
                {
                    using (var response = (HttpWebResponse)await Task.Factory.FromAsync(request.BeginGetResponse, request.EndGetResponse, null))
                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        string responseText = await reader.ReadToEndAsync();
                        return JObject.Parse(responseText);
                    }
                }
                catch (WebException ex)
                {
                    var httpResponse = ex.Response as HttpWebResponse;

                    // Rate limit (429) or overloaded (529) — retry with backoff
                    if (httpResponse != null &&
                        ((int)httpResponse.StatusCode == 429 || (int)httpResponse.StatusCode == 529) &&
                        attempt < maxRetries - 1)
                    {
                        int delayMs = (int)Math.Pow(2, attempt + 1) * 1000; // 2s, 4s
                        MCPDockablePanel.Instance?.OnRetrying(delayMs / 1000);
                        await Task.Delay(delayMs, _cts?.Token ?? CancellationToken.None);
                        continue;
                    }

                    if (ex.Response != null)
                    {
                        using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                        {
                            string errorText = await reader.ReadToEndAsync();
                            try
                            {
                                var errorJson = JObject.Parse(errorText);
                                throw new Exception($"API Error: {errorJson["error"]?["message"]}");
                            }
                            catch (JsonReaderException)
                            {
                                throw new Exception($"API Error ({(int)httpResponse?.StatusCode}): {errorText}");
                            }
                        }
                    }
                    throw;
                }
            }

            throw new Exception("Max retries exceeded");
        }

        private async Task<string> ExecuteMcpCommand(string commandName, JObject parameters)
        {
            try
            {
                var jsonRpc = new JObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = Guid.NewGuid().ToString(),
                    ["method"] = commandName,
                    ["params"] = parameters
                };

                string request = jsonRpc.ToString(Formatting.None);

                using (var client = new TcpClient())
                {
                    // Connect with timeout
                    var connectTask = client.ConnectAsync("127.0.0.1", MCP_PORT);
                    if (await Task.WhenAny(connectTask, Task.Delay(10000)) != connectTask)
                        return "MCP command failed: Connection timeout (server not responding)";
                    await connectTask; // propagate any connection exception

                    var stream = client.GetStream();

                    byte[] requestData = Encoding.UTF8.GetBytes(request);
                    await stream.WriteAsync(requestData, 0, requestData.Length);

                    // Read response with streaming buffer (no 65KB limit)
                    byte[] buffer = new byte[8192];
                    var responseBuilder = new StringBuilder();
                    int bytesRead;

                    client.ReceiveTimeout = 120000;

                    // Read first chunk (blocks until data arrives)
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    // Continue reading if more data is available
                    while (stream.DataAvailable)
                    {
                        bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0) break;
                        responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                    }

                    string responseStr = responseBuilder.ToString();
                    var responseJson = JObject.Parse(responseStr);

                    if (responseJson["result"] != null)
                        return responseJson["result"].ToString(Formatting.Indented);
                    if (responseJson["error"] != null)
                        return $"Error: {responseJson["error"]?["message"]}";

                    return responseStr;
                }
            }
            catch (Exception ex)
            {
                return $"MCP command failed: {ex.Message}";
            }
        }

        public void ClearHistory()
        {
            _conversationHistory.Clear();
        }

        private JArray _cachedToolDefinitions;

        private JArray GetToolDefinitions()
        {
            if (_cachedToolDefinitions != null)
                return _cachedToolDefinitions;

            _cachedToolDefinitions = LoadToolsFromSchemaFile() ?? LoadToolsFromCommandJson() ?? BuildFallbackTools();
            return _cachedToolDefinitions;
        }

        private JArray LoadToolsFromSchemaFile()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                string schemaPath = Path.Combine(dllDir, "tool_schemas.json");

                if (!File.Exists(schemaPath))
                    return null;

                var tools = JArray.Parse(File.ReadAllText(schemaPath));
                return tools.Count > 0 ? tools : null;
            }
            catch
            {
                return null;
            }
        }

        private JArray LoadToolsFromCommandJson()
        {
            try
            {
                string dllDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
                string commandJsonPath = Path.Combine(dllDir, "Commands", "RevitMCPCommandSet", "command.json");

                if (!File.Exists(commandJsonPath))
                    return null;

                var json = JObject.Parse(File.ReadAllText(commandJsonPath));
                var commands = json["commands"] as JArray;
                if (commands == null || commands.Count == 0)
                    return null;

                var tools = new JArray();
                foreach (var cmd in commands)
                {
                    string name = cmd["commandName"]?.ToString();
                    string desc = cmd["description"]?.ToString();
                    if (string.IsNullOrEmpty(name)) continue;

                    tools.Add(new JObject
                    {
                        ["name"] = name,
                        ["description"] = desc ?? name,
                        ["input_schema"] = new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject()
                        }
                    });
                }

                return tools.Count > 0 ? tools : null;
            }
            catch
            {
                return null;
            }
        }

        private JArray BuildFallbackTools()
        {
            // Minimal fallback if command.json is not found
            var tools = new JArray();
            var fallbackCommands = new[]
            {
                ("get_project_info", "Get project info: name, address, author, levels, phases, links"),
                ("analyze_model_statistics", "Analyze model: element counts by category, types, families, levels"),
                ("get_warnings", "Get all warnings/errors from the Revit model"),
                ("create_level", "Create levels at specified elevations (mm)"),
                ("create_line_based_element", "Create walls or other line-based elements (mm)"),
                ("create_room", "Create rooms at specified positions (mm)"),
                ("create_grid", "Create grid system with automatic spacing (mm)"),
                ("delete_element", "Delete elements by ID"),
                ("export_room_data", "Export all room data: name, number, level, area, volume"),
                ("get_materials", "List all project materials with color and properties"),
                ("purge_unused", "Identify and optionally remove unused families, types, materials"),
                ("say_hello", "Show a dialog in Revit with a message (connection test)")
            };

            foreach (var (name, desc) in fallbackCommands)
            {
                tools.Add(new JObject
                {
                    ["name"] = name,
                    ["description"] = desc,
                    ["input_schema"] = new JObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JObject()
                    }
                });
            }

            return tools;
        }
    }
}
