using System;
using System.IO;
using RevitMCPSDK.API.Interfaces;
using revit_mcp_plugin.Utils;

namespace revit_mcp_plugin.Core
{
    public static class McpServerSyncService
    {
        public static void Sync(ILogger logger)
        {
            string sourceDir = PathManager.GetConfigSetting("NetworkMcpServerBuildPath");
            string targetDir = PathManager.GetConfigSetting("LocalMcpServerBuildPath");
            if (sourceDir == null || targetDir == null) return;

            // The integrated server is one self-contained JS bundle. Replace only that file.
            string temporary = null;
            try
            {
                var source = new FileInfo(Path.Combine(sourceDir, "index.js"));
                if (!source.Exists) { logger.Warning("Server bundle unavailable: {0}", source.FullName); return; }
                string target = Path.GetFullPath(Path.Combine(targetDir, "index.js"));
                if (string.Equals(source.FullName, target, StringComparison.OrdinalIgnoreCase)) return;
                var local = new FileInfo(target);
                if (local.Exists && local.LastWriteTimeUtc == source.LastWriteTimeUtc && local.Length == source.Length) return;
                Directory.CreateDirectory(targetDir);
                temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.Copy(source.FullName, temporary);
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
                logger.Info("Updated MCP server bundle at {0}", target);
            }
            catch (Exception ex) { logger.Warning("Server bundle sync failed: {0}", ex.Message); }
            finally
            {
                try { if (temporary != null) File.Delete(temporary); }
                catch (IOException ex) { logger.Warning("Could not remove temporary server bundle: {0}", ex.Message); }
                catch (UnauthorizedAccessException ex) { logger.Warning("Could not remove temporary server bundle: {0}", ex.Message); }
            }
        }
    }
}
