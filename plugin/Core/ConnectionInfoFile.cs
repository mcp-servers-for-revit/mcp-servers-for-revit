using System;
using System.IO;
using Newtonsoft.Json;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Tells the MCP server which port to use and the token it must send with each request.
    /// The file sits in the current user's %LOCALAPPDATA%, so other users on a shared machine can't read it.
    /// </summary>
    public static class ConnectionInfoFile
    {
        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "mcp-servers-for-revit",
            "connection.json");

        public static void Write(int port, string token)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(new { port, token }));
        }

        public static void Delete()
        {
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
