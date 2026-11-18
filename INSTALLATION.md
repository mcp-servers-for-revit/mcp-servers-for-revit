# Installation Guide

Complete guide to install and configure **mcp-servers-for-revit** on Autodesk Revit 2023-2027.

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Method 1: Install from Release (recommended)](#method-1-install-from-release-recommended)
- [Method 2: Build from Source](#method-2-build-from-source)
- [MCP Server Configuration](#mcp-server-configuration)
- [API Key Configuration (Chat Panel)](#api-key-configuration-chat-panel)
- [Verifying the Installation](#verifying-the-installation)
- [Updating](#updating)
- [Uninstalling](#uninstalling)
- [Troubleshooting](#troubleshooting)

---

## Prerequisites

### Required Software (Install from Release)

| Component | Version | Notes |
|-----------|---------|-------|
| **Autodesk Revit** | 2023, 2024, 2025, 2026, or 2027 | Installed with an active license |
| **Claude Desktop** | Any | Download from [claude.ai/download](https://claude.ai/download) |

> **Node.js not required.** The Node.js runtime is included in the installation package (portable, not installed system-wide).

### System Requirements

- **OS**: Windows 10/11 (64-bit)
- **RAM**: Minimum 8 GB (16 GB recommended with Revit open)
- **Disk**: ~150 MB for plugin + server + bundled Node.js runtime
- **Network**: TCP port **8080** available on localhost

### Additional Requirements for Building from Source

| Component | Version | For which Revit version |
|-----------|---------|------------------------|
| **Node.js** | 18 or higher | To compile the TypeScript server |
| **npm** | Included with Node.js | To install dependencies |
| **.NET Framework 4.8 SDK** | 4.8+ | Revit 2023-2024 |
| **.NET 8.0 SDK** | 8.0+ | Revit 2025-2026 |
| **.NET 10.0 SDK** | 10.0+ (preview) | Revit 2027 |
| **Visual Studio 2022** | 17.x (optional) | Build and debug |
| **MSBuild** | Included with VS or .NET SDK | CLI build |

### Verify Prerequisites (only for Building from Source)

Open a terminal and verify:

```bash
# Verify Node.js
node --version
# Expected output: v18.x.x or higher

# Verify npm
npm --version

# Verify .NET SDK (for building from source)
dotnet --list-sdks
```

---

## Method 1: Install from Release (recommended)

### Step 1: Download the Release

1. Go to the [Releases](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/releases) page
2. Download the ZIP matching your Revit version:
   - `mcp-servers-for-revit-vX.Y.Z-Revit2023.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2024.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2025.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2026.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2027.zip`

### Step 2: Extract to the Revit Addins Folder

1. **Close Revit** if it is open
2. Open the Revit Addins folder:
   ```
   %AppData%\Autodesk\Revit\Addins\<version>\
   ```
   For example, for Revit 2026:
   ```
   %AppData%\Autodesk\Revit\Addins\2026\
   ```

   > **Tip**: Press `Win+R`, paste the path and press Enter to open the folder directly.

3. Extract the ZIP contents into the folder. The final structure should be:

```
Addins/<version>/
├── mcp-servers-for-revit.addin          <-- Manifest file
└── revit_mcp_plugin/                    <-- Plugin folder
    ├── RevitMCPPlugin.dll
    ├── Newtonsoft.Json.dll
    ├── ...
    └── Commands/
        └── RevitMCPCommandSet/
            ├── command.json
            ├── <version>/              <-- E.g.: 2026/
            │   ├── RevitMCPCommandSet.dll
            │   └── ...
            └── server/                  <-- MCP Server (included in the ZIP)
                ├── build/               <-- Server JS code
                ├── node_modules/        <-- Pre-installed dependencies
                ├── runtime/
                │   └── node.exe         <-- Portable Node.js (no installation required)
                └── package.json
```

### Step 3: Launch Revit

1. Open Revit
2. If a security warning appears for the add-in, click **"Always Load"**
3. You should see the **"Revit MCP Plugin"** panel in the ribbon

---

## Method 2: Build from Source

### Step 1: Clone the Repository

```bash
git clone https://github.com/mcp-servers-for-revit/mcp-servers-for-revit.git
cd mcp-servers-for-revit
```

### Step 2: Build the MCP Server

> **Note**: `server/build/` is already included in the repository — this step is only needed if you modify the TypeScript source.

```bash
cd server
npm install
npm run build
cd ..
```

### Step 3: Build the Revit Plugin

Choose the command based on your Revit version:

```bash
# Revit 2023 (.NET Framework 4.8) - requires MSBuild
msbuild mcp-servers-for-revit.sln -p:Configuration="Release R23" -restore

# Revit 2024 (.NET Framework 4.8) - requires MSBuild
msbuild mcp-servers-for-revit.sln -p:Configuration="Release R24" -restore

# Revit 2025 (.NET 8)
dotnet build mcp-servers-for-revit.sln -c "Release R25"

# Revit 2026 (.NET 8)
dotnet build mcp-servers-for-revit.sln -c "Release R26"

# Revit 2027 (.NET 10)
dotnet build mcp-servers-for-revit.sln -c "Release R27"
```

> **Note**: For Revit 2023/2024, MSBuild is required (included with Visual Studio). For Revit 2025/2026, the .NET 8 SDK is sufficient. For Revit 2027, the .NET 10 SDK (preview) is required.

### Step 4: Automatic Deploy (Debug)

In Debug mode, the build automatically copies the files to the Revit Addins folder:

```bash
# The Debug build installs directly into Revit
dotnet build mcp-servers-for-revit.sln -c "Debug R26"
```

### Step 5: Manual Deploy (Release)

For a Release build, manually copy the output:

```bash
# The output is located in:
# plugin/bin/AddIn <year> Release R<xx>/

# Copy all contents to the Revit Addins folder
```

---

## MCP Server Configuration

The MCP server is the bridge between AI assistants (Claude, Cline) and the Revit plugin.
The local server (`server/build/index.js`) is already included in the installation package — no external npm package installation is needed.

### For Claude Desktop (after installation via install.ps1)

The `install.ps1` script automatically configures Claude Desktop. If you need to configure it manually or restore the configuration, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\fix-mcp.ps1
```

Or manually edit `%APPDATA%\Claude\claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "npx",
            "args": ["-y", "mcp-server-for-revit"]
        }
    }
}
```

Restart Claude Desktop and verify that the hammer icon appears at the bottom right.

### For Claude Code (CLI)

Run the following command to register the MCP server:

```bash
claude mcp add mcp-server-for-revit -- npx -y mcp-server-for-revit
```

### For Other MCP Clients (Cline, Continue, etc.)

Configure the MCP server with:
- **Command**: `node`
- **Arguments**: absolute path to `server\build\index.js` in the Addins folder
- **Transport**: stdio

---

## API Key Configuration (Chat Panel)

The built-in chat panel in Revit requires an **Anthropic** API key to work. This is only needed for the chat panel — using it through Claude Desktop/Claude Code does not require additional configuration.

### Obtaining an API Key

1. Go to [console.anthropic.com](https://console.anthropic.com)
2. Sign up or log in
3. Go to **API Keys** and create a new key
4. Copy the key (it is shown only once)

### Method 1: Environment Variable (recommended)

1. Open **System Settings > Environment Variables**
2. Add a new user variable:
   - **Name**: `ANTHROPIC_API_KEY`
   - **Value**: `sk-ant-...` (your API key)
3. Restart Revit

Or from a terminal (current session):
```bash
setx ANTHROPIC_API_KEY "sk-ant-..."
```

### Method 2: Text File

1. Create the folder (if it doesn't exist):
   ```
   %USERPROFILE%\.claude\
   ```

2. Create the file `api_key.txt` with your API key:
   ```
   %USERPROFILE%\.claude\api_key.txt
   ```
   File contents (key only, no spaces):
   ```
   sk-ant-...
   ```

> **Security**: Never share your API key. The `api_key.txt` file is read locally by the plugin only.

---

## Verifying the Installation

### 1. Verify the Revit Plugin

1. Open Revit
2. Look for the **"Revit MCP Plugin"** panel in the ribbon (Add-Ins tab)
3. You should see 3 buttons:
   - **Revit MCP Switch** — Start/stop the socket service
   - **MCP Panel** — Show/hide the chat panel
   - **Settings** — Open settings

### 2. Start the MCP Service

1. Click **"Revit MCP Switch"** in the ribbon
2. The service starts on TCP port 8080
3. The indicator in the chat panel turns green: **"MCP Online"**

### 3. Connection Test

From Claude Desktop or Claude Code, try:

```
Use the say_hello tool with the message "Connection test"
```

If everything works, a dialog will appear in Revit with the message.

### 4. Chat Panel Test (optional)

1. Click **"MCP Panel"** to open the panel
2. Verify that it shows **"MCP Online"** in the top right
3. Type a message, e.g.: "Tell me the project info"
4. Claude should respond using the Revit tools

---

## Updating

### From Release

1. Close Revit
2. Download the new release
3. Overwrite the files in the Addins folder
4. Reopen Revit

### From Source

```bash
cd mcp-servers-for-revit
git pull
cd server && npm install && npm run build && cd ..
dotnet build mcp-servers-for-revit.sln -c "Debug R26"
```

---

## Uninstalling

1. Close Revit
2. Go to the Addins folder:
   ```
   %AppData%\Autodesk\Revit\Addins\<version>\
   ```
3. Delete:
   - `mcp-servers-for-revit.addin`
   - The `revit_mcp_plugin/` folder
4. (Optional) Remove the MCP configuration from your AI client

---

## Troubleshooting

### The plugin does not appear in the ribbon

- Verify that the `.addin` file is in the correct folder
- Check that the ZIP version matches your Revit version
- Check the Revit journal for errors: `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <version>\Journals\`

### "MCP Offline" in the chat panel

- Click **"Revit MCP Switch"** to start the service
- Verify that port 8080 is not occupied by another program
- Check the Windows firewall (it must allow local connections on port 8080)

### Claude cannot connect to tools

- Verify that the MCP server is configured in the AI client
- Make sure Revit is open and the MCP service is active (green indicator)
- Restart the AI client after modifying the configuration

### "API key not configured" error in the chat panel

- Configure the Anthropic API key using one of the methods described above
- Restart Revit after setting the environment variable
- Verify that the `api_key.txt` file contains only the key (`sk-ant-...`), with no extra spaces or newlines

### Build fails for Revit 2023/2024

- Install Visual Studio 2022 with the ".NET desktop development" workload
- Or install the [Build Tools for Visual Studio 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) with MSBuild

### Port 8080 already in use

If another program is using port 8080, check with:
```bash
netstat -ano | findstr :8080
```
Close the program occupying the port, or change the port in the source code (`SocketService.cs` and `SocketClient.ts`).
