using System;
using System.IO;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using revit_mcp_plugin.Helpers;

namespace revit_mcp_plugin.Utils
{
    /// <summary>
    /// Auto-configures Claude Desktop (claude_desktop_config.json) so the user
    /// never needs to run any script or edit any file.
    /// Called once during Revit startup — silently skips if already configured.
    /// </summary>
    public static class ClaudeDesktopConfigurator
    {
        private const string ConfigFileName = "claude_desktop_config.json";
        private const string McpServerName = "mcp-server-for-revit";

        /// <summary>
        /// Ensures that Claude Desktop is configured to connect to the Revit MCP server.
        /// Safe to call every startup — only writes when the config is missing or stale.
        /// Creates timestamped backups before any modification and validates after write.
        /// </summary>
        public static void EnsureConfigured()
        {
            const string Tag = "ClaudeDesktopConfigurator";
            try
            {
                // 1. Log start
                McpLogger.Info(Tag, "Checking Claude Desktop configuration");

                // 2. Find Claude Desktop directory
                string claudeDir = GetClaudeDesktopDir();
                if (claudeDir == null)
                {
                    McpLogger.Warn(Tag, "Claude Desktop directory not found — skipping auto-config");
                    return;
                }

                // 3. Resolve server and node paths
                string serverPath = GetServerPath();
                string nodePath = GetNodePath();

                if (serverPath == null || nodePath == null)
                {
                    McpLogger.Warn(Tag,
                        $"Auto-config skipped: server={serverPath ?? "null"}, node={nodePath ?? "null"}");
                    return;
                }

                // 4. Read existing config file (if present)
                string configPath = Path.Combine(claudeDir, ConfigFileName);
                string existingJson = null;

                if (File.Exists(configPath))
                {
                    try
                    {
                        existingJson = File.ReadAllText(configPath);
                    }
                    catch (Exception readEx)
                    {
                        McpLogger.Warn(Tag, $"Could not read config file: {readEx.Message}");
                        return;
                    }
                }

                // 5. Parse JSON — if corrupt, backup as .corrupted and start fresh
                JObject config = null;
                if (existingJson != null)
                {
                    try
                    {
                        config = JObject.Parse(existingJson);
                    }
                    catch (Exception parseEx)
                    {
                        McpLogger.Warn(Tag, $"Skipping invalid Claude Desktop configuration: {parseEx.Message}");
                        return;
                    }
                }

                if (config == null)
                {
                    config = new JObject();
                }

                // 6. Ensure mcpServers object exists
                var mcpServers = config["mcpServers"] as JObject;
                if (mcpServers == null)
                {
                    mcpServers = new JObject();
                    config["mcpServers"] = mcpServers;
                }

                // 7. Build the correct entry
                var correctEntry = new JObject
                {
                    ["command"] = nodePath,
                    ["args"] = new JArray(serverPath)
                };

                // Preserve existing user, suite, and standalone installer configurations.
                if (mcpServers[McpServerName] != null || mcpServers["revit-mcp"] != null) return;

                // 9. Backup existing file before modification
                if (File.Exists(configPath))
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupPath = configPath + $".{timestamp}.bak";
                    try
                    {
                        File.Copy(configPath, backupPath, overwrite: true);
                        McpLogger.Info(Tag, $"Backed up existing config to {backupPath}");
                    }
                    catch (Exception backupEx)
                    {
                        McpLogger.Error(Tag, "Failed to backup config before write", backupEx);
                        // Continue anyway — the update is more important than the backup
                    }
                }

                // 10. Update the revit-mcp entry (preserves all other servers)
                mcpServers[McpServerName] = correctEntry;

                // 11. Write with UTF-8 no BOM
                Directory.CreateDirectory(claudeDir);
                string outputJson = config.ToString(Formatting.Indented);
                var utf8NoBom = new System.Text.UTF8Encoding(false);
                File.WriteAllText(configPath, outputJson, utf8NoBom);

                // 12. Validate by re-reading and parsing
                try
                {
                    string verification = File.ReadAllText(configPath);
                    JObject.Parse(verification);
                    McpLogger.Info(Tag,
                        $"Claude Desktop configured successfully: {nodePath} {serverPath}");
                }
                catch (Exception valEx)
                {
                    McpLogger.Error(Tag,
                        "Post-write validation failed — config file may be corrupt", valEx);
                }

                // 13. Notify the user (only when we actually changed something)
                try
                {
                    var td = new TaskDialog("Revit MCP Plugin")
                    {
                        MainInstruction = "Claude Desktop configured automatically",
                        MainContent =
                            "The Revit MCP server has been registered in Claude Desktop.\n\n" +
                            "Restart Claude Desktop to activate the tools.\n\n" +
                            "(This message only appears when the configuration is updated)",
                        CommonButtons = TaskDialogCommonButtons.Ok
                    };
                    td.Show();
                }
                catch
                {
                    // If TaskDialog fails (e.g. during silent startup), ignore
                }
            }
            catch (Exception ex)
            {
                // Never crash Revit because of auto-config
                McpLogger.Error(Tag, "Claude Desktop auto-config failed (non-fatal)", ex);
            }
        }

        // -----------------------------------------------------------------
        // Path resolution
        // -----------------------------------------------------------------

        private static string GetServerPath()
        {
            string pluginDir = PathManager.GetAppDataDirectoryPath();
            string serverJs = Path.Combine(
                pluginDir, "Commands", "RevitMCPCommandSet", "server", "build", "index.js");
            return File.Exists(serverJs) ? serverJs : null;
        }

        private static string GetNodePath()
        {
            // 1. Bundled portable node.exe (ships with the Release ZIP)
            string pluginDir = PathManager.GetAppDataDirectoryPath();
            string bundledNode = Path.Combine(
                pluginDir, "Commands", "RevitMCPCommandSet", "server", "runtime", "node.exe");
            if (File.Exists(bundledNode))
                return bundledNode;

            // 2. System node in PATH
            return FindInPath("node.exe");
        }

        private static string FindInPath(string executable)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (string dir in pathEnv.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                try
                {
                    string fullPath = Path.Combine(dir.Trim(), executable);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch
                {
                    // Invalid path entry — skip
                }
            }
            return null;
        }

        // -----------------------------------------------------------------
        // Claude Desktop detection
        // -----------------------------------------------------------------

        private static string GetClaudeDesktopDir()
        {
            // Standard install
            string standard = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude");
            if (Directory.Exists(standard))
                return standard;

            // MSIX (Microsoft Store) install
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string packagesDir = Path.Combine(localAppData, "Packages");
            if (Directory.Exists(packagesDir))
            {
                try
                {
                    string claudePkg = Directory.GetDirectories(packagesDir, "Claude_*").FirstOrDefault();
                    if (claudePkg != null)
                    {
                        string msixDir = Path.Combine(claudePkg, "LocalCache", "Roaming", "Claude");
                        if (Directory.Exists(msixDir))
                            return msixDir;
                    }
                }
                catch
                {
                    // Permission issue reading Packages — skip MSIX check
                }
            }

            // Claude Desktop not installed — return standard path anyway so the
            // config is ready when the user eventually installs it.
            return standard;
        }
    }
}
