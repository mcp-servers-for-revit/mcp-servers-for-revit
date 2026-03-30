# Guida all'Installazione

Guida completa per installare e configurare **mcp-servers-for-revit** su Autodesk Revit 2023-2026.

---

## Indice

- [Prerequisiti](#prerequisiti)
- [Metodo 1: Installazione da Release (consigliato)](#metodo-1-installazione-da-release-consigliato)
- [Metodo 2: Build da Sorgente](#metodo-2-build-da-sorgente)
- [Configurazione del Server MCP](#configurazione-del-server-mcp)
- [Configurazione della Chiave API (Chat Panel)](#configurazione-della-chiave-api-chat-panel)
- [Verifica dell'Installazione](#verifica-dellinstallazione)
- [Aggiornamento](#aggiornamento)
- [Disinstallazione](#disinstallazione)
- [Risoluzione Problemi](#risoluzione-problemi)

---

## Prerequisiti

### Software Richiesto

| Componente | Versione | Note |
|------------|----------|------|
| **Autodesk Revit** | 2023, 2024, 2025 o 2026 | Installato e con licenza attiva |
| **Node.js** | 18 o superiore | Per il server MCP |
| **npm** | Incluso con Node.js | Per installare dipendenze |

### Requisiti di Sistema

- **OS**: Windows 10/11 (64-bit)
- **RAM**: Minimo 8 GB (consigliati 16 GB con Revit aperto)
- **Disco**: ~100 MB per plugin + server
- **Rete**: Porta TCP **8080** libera su localhost

### Requisiti Aggiuntivi per Build da Sorgente

| Componente | Versione | Per quale versione Revit |
|------------|----------|--------------------------|
| **.NET Framework 4.8 SDK** | 4.8+ | Revit 2023-2024 |
| **.NET 8.0 SDK** | 8.0+ | Revit 2025-2026 |
| **Visual Studio 2022** | 17.x (opzionale) | Build e debug |
| **MSBuild** | Incluso con VS o .NET SDK | Build da CLI |

### Verifica Prerequisiti

Apri un terminale e verifica:

```bash
# Verifica Node.js
node --version
# Output atteso: v18.x.x o superiore

# Verifica npm
npm --version

# Verifica .NET SDK (per build da sorgente)
dotnet --list-sdks
```

---

## Metodo 1: Installazione da Release (consigliato)

### Passo 1: Scarica la Release

1. Vai alla pagina [Releases](https://github.com/LuDattilo/revit-mcp-server/releases)
2. Scarica lo ZIP corrispondente alla tua versione di Revit:
   - `mcp-servers-for-revit-vX.Y.Z-Revit2023.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2024.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2025.zip`
   - `mcp-servers-for-revit-vX.Y.Z-Revit2026.zip`

### Passo 2: Estrai nella Cartella Addins di Revit

1. **Chiudi Revit** se aperto
2. Apri la cartella Addins di Revit:
   ```
   %AppData%\Autodesk\Revit\Addins\<versione>\
   ```
   Per esempio, per Revit 2026:
   ```
   %AppData%\Autodesk\Revit\Addins\2026\
   ```

   > **Suggerimento**: Premi `Win+R`, incolla il percorso e premi Invio per aprire direttamente la cartella.

3. Estrai il contenuto dello ZIP nella cartella. La struttura finale deve essere:

```
Addins/<versione>/
├── mcp-servers-for-revit.addin          <-- File manifesto
└── revit_mcp_plugin/                    <-- Cartella plugin
    ├── RevitMCPPlugin.dll
    ├── Newtonsoft.Json.dll
    ├── ...
    └── Commands/
        └── RevitMCPCommandSet/
            ├── command.json
            └── <versione>/              <-- Es: 2026/
                ├── RevitMCPCommandSet.dll
                └── ...
```

### Passo 3: Avvia Revit

1. Apri Revit
2. Se appare un avviso di sicurezza per l'add-in, clicca **"Carica sempre"**
3. Dovresti vedere il pannello **"Revit MCP Plugin"** nel ribbon

---

## Metodo 2: Build da Sorgente

### Passo 1: Clona il Repository

```bash
git clone https://github.com/LuDattilo/revit-mcp-server.git
cd mcp-servers-for-revit
```

### Passo 2: Build del Server MCP

```bash
cd server
npm install
npm run build
cd ..
```

### Passo 3: Build del Plugin Revit

Scegli il comando in base alla tua versione di Revit:

```bash
# Revit 2023 (.NET Framework 4.8) - richiede MSBuild
msbuild mcp-servers-for-revit.sln -p:Configuration="Release R23" -restore

# Revit 2024 (.NET Framework 4.8) - richiede MSBuild
msbuild mcp-servers-for-revit.sln -p:Configuration="Release R24" -restore

# Revit 2025 (.NET 8)
dotnet build mcp-servers-for-revit.sln -c "Release R25"

# Revit 2026 (.NET 8)
dotnet build mcp-servers-for-revit.sln -c "Release R26"
```

> **Nota**: Per Revit 2023/2024 serve MSBuild (incluso con Visual Studio). Per Revit 2025/2026 basta il .NET 8 SDK.

### Passo 4: Deploy Automatico (Debug)

In modalita' Debug, il build copia automaticamente i file nella cartella Addins di Revit:

```bash
# Il build Debug installa direttamente in Revit
dotnet build mcp-servers-for-revit.sln -c "Debug R26"
```

### Passo 5: Deploy Manuale (Release)

Per una build Release, copia manualmente l'output:

```bash
# L'output si trova in:
# plugin/bin/AddIn <anno> Release R<xx>/

# Copia tutto il contenuto nella cartella Addins di Revit
```

---

## Configurazione del Server MCP

Il server MCP e' il ponte tra gli assistenti AI (Claude, Cline) e il plugin Revit. Va configurato nel tuo client AI.

### Per Claude Code (CLI)

```bash
claude mcp add mcp-server-for-revit -- cmd /c npx -y mcp-server-for-revit
```

### Per Claude Desktop

1. Apri Claude Desktop
2. Vai in **Settings > Developer > Edit Config**
3. Modifica `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "cmd",
            "args": ["/c", "npx", "-y", "mcp-server-for-revit"]
        }
    }
}
```

4. Riavvia Claude Desktop
5. Verifica che l'icona del martello appaia (indica connessione MCP attiva)

### Per Altri Client MCP (Cline, Continue, ecc.)

Configura il server MCP con:
- **Comando**: `cmd`
- **Argomenti**: `/c npx -y mcp-server-for-revit`
- **Trasporto**: stdio

### Configurazione per Sviluppo Locale

Se hai buildato il server da sorgente:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "node",
            "args": ["C:/percorso/mcp-servers-for-revit/server/build/index.js"]
        }
    }
}
```

---

## Configurazione della Chiave API (Chat Panel)

Il pannello chat integrato in Revit richiede una chiave API **Anthropic** per funzionare. Questo e' necessario **solo** per il chat panel — l'uso tramite Claude Desktop/Claude Code non richiede configurazione aggiuntiva.

### Ottenere una Chiave API

1. Vai su [console.anthropic.com](https://console.anthropic.com)
2. Registrati o accedi
3. Vai in **API Keys** e crea una nuova chiave
4. Copia la chiave (viene mostrata una sola volta)

### Metodo 1: Variabile d'Ambiente (consigliato)

1. Apri **Impostazioni di Sistema > Variabili d'ambiente**
2. Aggiungi una nuova variabile utente:
   - **Nome**: `ANTHROPIC_API_KEY`
   - **Valore**: `sk-ant-...` (la tua chiave API)
3. Riavvia Revit

Oppure da terminale (sessione corrente):
```bash
setx ANTHROPIC_API_KEY "sk-ant-..."
```

### Metodo 2: File di Testo

1. Crea la cartella (se non esiste):
   ```
   %USERPROFILE%\.claude\
   ```

2. Crea il file `api_key.txt` con la tua chiave API:
   ```
   %USERPROFILE%\.claude\api_key.txt
   ```
   Contenuto del file (solo la chiave, senza spazi):
   ```
   sk-ant-...
   ```

> **Sicurezza**: Non condividere mai la chiave API. Il file `api_key.txt` e' letto solo localmente dal plugin.

---

## Verifica dell'Installazione

### 1. Verifica Plugin Revit

1. Apri Revit
2. Cerca il pannello **"Revit MCP Plugin"** nel ribbon (tab Add-Ins)
3. Dovresti vedere 3 pulsanti:
   - **Revit MCP Switch** — Avvia/ferma il server socket
   - **MCP Panel** — Mostra/nasconde il pannello chat
   - **Settings** — Apre le impostazioni

### 2. Avvia il Servizio MCP

1. Clicca **"Revit MCP Switch"** nel ribbon
2. Il servizio si avvia sulla porta TCP 8080
3. L'indicatore nel pannello chat passa a verde: **"MCP Online"**

### 3. Test di Connessione

Da Claude Desktop o Claude Code, prova:

```
Usa il tool say_hello con messaggio "Test connessione"
```

Se tutto funziona, apparira' un dialog in Revit con il messaggio.

### 4. Test del Chat Panel (opzionale)

1. Clicca **"MCP Panel"** per aprire il pannello
2. Verifica che mostri **"MCP Online"** in alto a destra
3. Scrivi un messaggio, es: "Dimmi le info del progetto"
4. Claude dovrebbe rispondere usando i tool Revit

---

## Aggiornamento

### Da Release

1. Chiudi Revit
2. Scarica la nuova release
3. Sovrascrivi i file nella cartella Addins
4. Riapri Revit

### Da Sorgente

```bash
cd mcp-servers-for-revit
git pull
cd server && npm install && npm run build && cd ..
dotnet build mcp-servers-for-revit.sln -c "Debug R26"
```

---

## Disinstallazione

1. Chiudi Revit
2. Vai nella cartella Addins:
   ```
   %AppData%\Autodesk\Revit\Addins\<versione>\
   ```
3. Elimina:
   - `mcp-servers-for-revit.addin`
   - La cartella `revit_mcp_plugin/`
4. (Opzionale) Rimuovi la configurazione MCP dal tuo client AI

---

## Risoluzione Problemi

### Il plugin non appare nel ribbon

- Verifica che il file `.addin` sia nella cartella corretta
- Controlla che la versione dello ZIP corrisponda alla tua versione di Revit
- Guarda il journal di Revit per errori: `%LOCALAPPDATA%\Autodesk\Revit\Autodesk Revit <versione>\Journals\`

### "MCP Offline" nel pannello chat

- Clicca **"Revit MCP Switch"** per avviare il servizio
- Verifica che la porta 8080 non sia occupata da un altro programma
- Controlla il firewall di Windows (deve permettere connessioni locali sulla porta 8080)

### Claude non riesce a connettersi ai tool

- Verifica che il server MCP sia configurato nel client AI
- Assicurati che Revit sia aperto e il servizio MCP attivo (indicatore verde)
- Riavvia il client AI dopo aver modificato la configurazione

### Errore "API key not configured" nel chat panel

- Configura la chiave API Anthropic con uno dei metodi descritti sopra
- Riavvia Revit dopo aver impostato la variabile d'ambiente
- Verifica che il file `api_key.txt` contenga solo la chiave (`sk-ant-...`), senza spazi o newline extra

### Build fallisce per Revit 2023/2024

- Installa Visual Studio 2022 con il workload ".NET desktop development"
- Oppure installa il [Build Tools per Visual Studio 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022) con MSBuild

### Porta 8080 gia' in uso

Se un altro programma usa la porta 8080, verifica con:
```bash
netstat -ano | findstr :8080
```
Chiudi il programma che occupa la porta, oppure modifica la porta nel codice sorgente (`SocketService.cs` e `SocketClient.ts`).
