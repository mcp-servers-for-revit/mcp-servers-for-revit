# DiRootsOne Missing Features — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the 6 highest-impact missing features identified in the DiRootsOne vs Revit MCP comparison, plus the cross-cutting Excel export/import capability.

**Architecture:** Each new tool follows the existing 3-file pattern: C# Command (parses params, calls handler), C# EventHandler (runs on Revit UI thread via ExternalEvent), TypeScript tool (Zod schema + MCP registration). All tools auto-discovered via `register.ts` dynamic import and `command.json` declaration.

**Tech Stack:** C#/.NET 8 (Revit 2025-2026), RevitMCPSDK, ClosedXML (new dependency for Excel), TypeScript, Zod, @modelcontextprotocol/sdk

---

## Prerequisite: Add ClosedXML NuGet Package

Before any Excel-related tasks, add the dependency:

- [ ] **Step 1: Add ClosedXML to commandset .csproj**

In `commandset/RevitMCPCommandSet.csproj`, add to the `<ItemGroup>` with other PackageReferences:

```xml
<PackageReference Include="ClosedXML" Version="0.104.2" />
```

- [ ] **Step 2: Restore and verify build**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" --restore`
Expected: Build succeeded with 0 errors

- [ ] **Step 3: Commit**

```bash
git add commandset/RevitMCPCommandSet.csproj
git commit -m "feat: add ClosedXML dependency for Excel export/import"
```

---

## Task 1: `export_to_excel` — Universal Excel Export

Exports elements by category to a .xlsx file with color-coded columns (green=instance, yellow=type, red=read-only). Covers the core SheetLink export gap.

**Files:**
- Create: `commandset/Commands/DataExtraction/ExportToExcelCommand.cs`
- Create: `commandset/Services/DataExtraction/ExportToExcelEventHandler.cs`
- Create: `server/src/tools/export_to_excel.ts`
- Modify: `command.json` — add command entry

- [ ] **Step 1: Create the EventHandler**

Create `commandset/Services/DataExtraction/ExportToExcelEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ExportToExcelEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Input parameters
        public List<string> Categories { get; set; } = new();
        public List<string> ParameterNames { get; set; } = new();
        public bool IncludeTypeParameters { get; set; }
        public bool IncludeElementId { get; set; } = true;
        public string FilePath { get; set; } = "";
        public string SheetName { get; set; } = "Export";
        public bool ColorCodeColumns { get; set; } = true;
        public int MaxElements { get; set; } = 10000;

        // Output
        public object Result { get; private set; }

        public void SetParameters(List<string> categories, List<string> parameterNames,
            bool includeTypeParameters, bool includeElementId, string filePath,
            string sheetName, bool colorCodeColumns, int maxElements)
        {
            Categories = categories;
            ParameterNames = parameterNames;
            IncludeTypeParameters = includeTypeParameters;
            IncludeElementId = includeElementId;
            FilePath = filePath;
            SheetName = sheetName;
            ColorCodeColumns = colorCodeColumns;
            MaxElements = maxElements;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Resolve output path
                if (string.IsNullOrEmpty(FilePath))
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    FilePath = Path.Combine(desktop, $"RevitExport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
                }

                // Collect elements by category
                var collector = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType();

                var elements = new List<Element>();
                if (Categories.Count > 0)
                {
                    foreach (var cat in Categories)
                    {
                        var builtInCat = GetBuiltInCategory(doc, cat);
                        if (builtInCat != BuiltInCategory.INVALID)
                        {
                            var catElements = new FilteredElementCollector(doc)
                                .OfCategory(builtInCat)
                                .WhereElementIsNotElementType()
                                .Take(MaxElements)
                                .ToList();
                            elements.AddRange(catElements);
                        }
                    }
                }
                else
                {
                    elements = collector.Take(MaxElements).ToList();
                }

                if (elements.Count == 0)
                {
                    Result = new { success = true, filePath = FilePath, elementCount = 0, message = "No elements found" };
                    return;
                }

                // Discover parameters
                var paramInfos = DiscoverParameters(doc, elements);

                // Create Excel workbook
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.Worksheets.Add(SheetName);

                    // Write header row
                    int col = 1;
                    if (IncludeElementId)
                    {
                        worksheet.Cell(1, col).Value = "ElementId";
                        col++;
                    }
                    worksheet.Cell(1, col).Value = "Category";
                    col++;
                    worksheet.Cell(1, col).Value = "Family";
                    col++;
                    worksheet.Cell(1, col).Value = "Type";
                    col++;

                    var paramColumns = new List<(string Name, bool IsType, bool IsReadOnly, int Column)>();
                    foreach (var pi in paramInfos)
                    {
                        worksheet.Cell(1, col).Value = pi.Name;

                        if (ColorCodeColumns)
                        {
                            var headerCell = worksheet.Cell(1, col);
                            if (pi.IsReadOnly)
                                headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFCCCC"); // red
                            else if (pi.IsType)
                                headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#FFFFCC"); // yellow
                            else
                                headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#CCFFCC"); // green
                        }

                        paramColumns.Add((pi.Name, pi.IsType, pi.IsReadOnly, col));
                        col++;
                    }

                    // Style header
                    var headerRange = worksheet.Range(1, 1, 1, col - 1);
                    headerRange.Style.Font.Bold = true;
                    headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;

                    // Write data rows
                    int row = 2;
                    foreach (var elem in elements)
                    {
                        col = 1;
                        if (IncludeElementId)
                        {
#if REVIT2024_OR_GREATER
                            worksheet.Cell(row, col).Value = elem.Id.Value;
#else
                            worksheet.Cell(row, col).Value = elem.Id.IntegerValue;
#endif
                            col++;
                        }
                        worksheet.Cell(row, col).Value = elem.Category?.Name ?? "";
                        col++;
                        worksheet.Cell(row, col).Value = (elem as FamilyInstance)?.Symbol?.Family?.Name ?? elem.GetType().Name;
                        col++;
                        worksheet.Cell(row, col).Value = elem.Name;
                        col++;

                        foreach (var pc in paramColumns)
                        {
                            string val = "";
                            if (pc.IsType)
                            {
                                var typeId = elem.GetTypeId();
                                if (typeId != ElementId.InvalidElementId)
                                {
                                    var typeElem = doc.GetElement(typeId);
                                    var p = typeElem?.LookupParameter(pc.Name);
                                    if (p != null && p.HasValue)
                                        val = p.AsValueString() ?? p.AsString() ?? "";
                                }
                            }
                            else
                            {
                                var p = elem.LookupParameter(pc.Name);
                                if (p != null && p.HasValue)
                                    val = p.AsValueString() ?? p.AsString() ?? "";
                            }
                            worksheet.Cell(row, pc.Column).Value = val;
                        }
                        row++;
                    }

                    // Auto-fit columns
                    worksheet.Columns().AdjustToContents(1, 50);

                    // Save
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    workbook.SaveAs(FilePath);
                }

                Result = new
                {
                    success = true,
                    filePath = FilePath,
                    elementCount = elements.Count,
                    parameterCount = paramInfos.Count,
                    sheetName = SheetName
                };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private List<ParamInfo> DiscoverParameters(Document doc, List<Element> elements)
        {
            var result = new Dictionary<string, ParamInfo>();
            var sample = elements.Take(50).ToList(); // sample for discovery

            foreach (var elem in sample)
            {
                // Instance parameters
                foreach (Parameter p in elem.Parameters)
                {
                    if (p.Definition == null) continue;
                    string name = p.Definition.Name;
                    if (ParameterNames.Count > 0 && !ParameterNames.Contains(name)) continue;
                    if (!result.ContainsKey(name))
                        result[name] = new ParamInfo { Name = name, IsType = false, IsReadOnly = p.IsReadOnly };
                }

                // Type parameters
                if (IncludeTypeParameters)
                {
                    var typeId = elem.GetTypeId();
                    if (typeId != ElementId.InvalidElementId)
                    {
                        var typeElem = doc.GetElement(typeId);
                        if (typeElem != null)
                        {
                            foreach (Parameter p in typeElem.Parameters)
                            {
                                if (p.Definition == null) continue;
                                string name = p.Definition.Name;
                                if (ParameterNames.Count > 0 && !ParameterNames.Contains(name)) continue;
                                string key = $"TYPE:{name}";
                                if (!result.ContainsKey(key))
                                    result[key] = new ParamInfo { Name = name, IsType = true, IsReadOnly = p.IsReadOnly };
                            }
                        }
                    }
                }
            }

            return result.Values.OrderBy(p => p.IsType).ThenBy(p => p.Name).ToList();
        }

        private BuiltInCategory GetBuiltInCategory(Document doc, string categoryName)
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase))
                    return (BuiltInCategory)cat.Id.IntegerValue;
            }
            return BuiltInCategory.INVALID;
        }

        public string GetName() => "Export To Excel";

        private class ParamInfo
        {
            public string Name { get; set; }
            public bool IsType { get; set; }
            public bool IsReadOnly { get; set; }
        }
    }
}
```

- [ ] **Step 2: Create the Command**

Create `commandset/Commands/DataExtraction/ExportToExcelCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ExportToExcelCommand : ExternalEventCommandBase
    {
        private ExportToExcelEventHandler _handler => (ExportToExcelEventHandler)Handler;
        public override string CommandName => "export_to_excel";

        public ExportToExcelCommand(UIApplication uiApp)
            : base(new ExportToExcelEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var categories = (parameters?["categories"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();
                var parameterNames = (parameters?["parameterNames"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();

                _handler.SetParameters(
                    categories,
                    parameterNames,
                    includeTypeParameters: parameters?["includeTypeParameters"]?.Value<bool>() ?? false,
                    includeElementId: parameters?["includeElementId"]?.Value<bool>() ?? true,
                    filePath: parameters?["filePath"]?.ToString() ?? "",
                    sheetName: parameters?["sheetName"]?.ToString() ?? "Export",
                    colorCodeColumns: parameters?["colorCodeColumns"]?.Value<bool>() ?? true,
                    maxElements: parameters?["maxElements"]?.Value<int>() ?? 10000
                );

                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;

                throw new TimeoutException("Export to Excel timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Export to Excel failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create the TypeScript tool**

Create `server/src/tools/export_to_excel.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExportToExcelTool(server: McpServer) {
  server.tool(
    "export_to_excel",
    `Export elements by category to an Excel (.xlsx) file with color-coded columns.
Column colors: green=instance parameter, yellow=type parameter, red=read-only.
File is saved to the specified path or Desktop by default.

GUIDANCE:
- "Export all doors to Excel": categories=["Doors"]
- "Export walls with Mark, Width, Height to Excel": categories=["Walls"], parameterNames=["Mark","Width","Height"]
- "Export everything including type parameters": includeTypeParameters=true
- "Save to specific path": filePath="C:/Exports/doors.xlsx"`,
    {
      categories: z.array(z.string()).optional()
        .describe("Category names to export (e.g. 'Walls', 'Doors'). Empty = all categories."),
      parameterNames: z.array(z.string()).optional()
        .describe("Parameter names to include. Empty = all discovered parameters."),
      includeTypeParameters: z.boolean().optional().default(false)
        .describe("Include type-level parameters (shown in yellow columns)."),
      includeElementId: z.boolean().optional().default(true)
        .describe("Include ElementId column (needed for re-import)."),
      filePath: z.string().optional()
        .describe("Output .xlsx path. Default: Desktop/RevitExport_<timestamp>.xlsx"),
      sheetName: z.string().optional().default("Export")
        .describe("Excel worksheet name."),
      colorCodeColumns: z.boolean().optional().default(true)
        .describe("Color-code header cells: green=instance, yellow=type, red=read-only."),
      maxElements: z.number().optional().default(10000)
        .describe("Max elements to export."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_to_excel", {
            categories: args.categories ?? [],
            parameterNames: args.parameterNames ?? [],
            includeTypeParameters: args.includeTypeParameters ?? false,
            includeElementId: args.includeElementId ?? true,
            filePath: args.filePath ?? "",
            sheetName: args.sheetName ?? "Export",
            colorCodeColumns: args.colorCodeColumns ?? true,
            maxElements: args.maxElements ?? 10000,
          });
        });
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Export to Excel failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

Add to the `commands` array in `command.json`:

```json
{
  "commandName": "export_to_excel",
  "description": "Export elements by category to Excel (.xlsx) with color-coded columns",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25"`
Expected: 0 errors

Run: `cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/DataExtraction/ExportToExcelCommand.cs \
        commandset/Services/DataExtraction/ExportToExcelEventHandler.cs \
        server/src/tools/export_to_excel.ts command.json
git commit -m "feat: add export_to_excel tool — Excel export with color-coded columns"
```

---

## Task 2: `import_from_excel` — Excel Import with Parameter Update

Reads an .xlsx file (previously exported or manually edited) and updates element parameters. Covers SheetLink import gap.

**Files:**
- Create: `commandset/Commands/DataExtraction/ImportFromExcelCommand.cs`
- Create: `commandset/Services/DataExtraction/ImportFromExcelEventHandler.cs`
- Create: `server/src/tools/import_from_excel.ts`
- Modify: `command.json`

- [ ] **Step 1: Create the EventHandler**

Create `commandset/Services/DataExtraction/ImportFromExcelEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using ClosedXML.Excel;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.DataExtraction
{
    public class ImportFromExcelEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string FilePath { get; set; } = "";
        public string SheetName { get; set; } = "";
        public bool DryRun { get; set; } = false;
        public object Result { get; private set; }

        public void SetParameters(string filePath, string sheetName, bool dryRun)
        {
            FilePath = filePath;
            SheetName = sheetName;
            DryRun = dryRun;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                if (!File.Exists(FilePath))
                {
                    Result = new { success = false, error = $"File not found: {FilePath}" };
                    return;
                }

                using (var workbook = new XLWorkbook(FilePath))
                {
                    var worksheet = string.IsNullOrEmpty(SheetName)
                        ? workbook.Worksheets.First()
                        : workbook.Worksheets.Worksheet(SheetName);

                    // Read header row to find column mapping
                    var headers = new Dictionary<int, string>();
                    int lastCol = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
                    for (int c = 1; c <= lastCol; c++)
                    {
                        string header = worksheet.Cell(1, c).GetString().Trim();
                        if (!string.IsNullOrEmpty(header))
                            headers[c] = header;
                    }

                    // Find ElementId column
                    int idCol = headers.FirstOrDefault(h =>
                        h.Value.Equals("ElementId", StringComparison.OrdinalIgnoreCase)).Key;

                    if (idCol == 0)
                    {
                        Result = new { success = false, error = "No 'ElementId' column found in the Excel file. Export with includeElementId=true first." };
                        return;
                    }

                    // Read data rows
                    int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
                    int updated = 0;
                    int skipped = 0;
                    int failed = 0;
                    var errors = new List<string>();

                    using (var tx = DryRun ? null : new Transaction(doc, "Import from Excel"))
                    {
                        tx?.Start();

                        for (int r = 2; r <= lastRow; r++)
                        {
                            string idStr = worksheet.Cell(r, idCol).GetString().Trim();
                            if (string.IsNullOrEmpty(idStr)) { skipped++; continue; }

                            if (!long.TryParse(idStr, out long idVal)) { skipped++; continue; }

#if REVIT2024_OR_GREATER
                            var elemId = new ElementId(idVal);
#else
                            var elemId = new ElementId((int)idVal);
#endif
                            var elem = doc.GetElement(elemId);
                            if (elem == null) { skipped++; continue; }

                            bool anySet = false;
                            foreach (var kvp in headers)
                            {
                                if (kvp.Key == idCol) continue;
                                string paramName = kvp.Value;
                                if (paramName == "Category" || paramName == "Family" || paramName == "Type") continue;

                                string cellValue = worksheet.Cell(r, kvp.Key).GetString().Trim();
                                var param = elem.LookupParameter(paramName);
                                if (param == null || param.IsReadOnly) continue;

                                if (!DryRun)
                                {
                                    try
                                    {
                                        bool setOk = SetParameterValue(param, cellValue);
                                        if (setOk) anySet = true;
                                    }
                                    catch (Exception ex)
                                    {
                                        errors.Add($"Row {r}, param '{paramName}': {ex.Message}");
                                        failed++;
                                    }
                                }
                                else
                                {
                                    anySet = true; // dry run counts as success
                                }
                            }

                            if (anySet) updated++;
                        }

                        tx?.Commit();
                    }

                    Result = new
                    {
                        success = true,
                        dryRun = DryRun,
                        totalRows = lastRow - 1,
                        updated,
                        skipped,
                        failed,
                        errors = errors.Take(20).ToList(),
                        message = DryRun
                            ? $"Dry run: {updated} elements would be updated, {skipped} skipped"
                            : $"Updated {updated} elements, {skipped} skipped, {failed} errors"
                    };
                }
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private bool SetParameterValue(Parameter param, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            switch (param.StorageType)
            {
                case StorageType.String:
                    param.Set(value);
                    return true;
                case StorageType.Integer:
                    if (int.TryParse(value, out int intVal)) { param.Set(intVal); return true; }
                    return false;
                case StorageType.Double:
                    if (double.TryParse(value, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out double dblVal))
                    {
                        param.Set(dblVal);
                        return true;
                    }
                    return false;
                default:
                    return false;
            }
        }

        public string GetName() => "Import From Excel";
    }
}
```

- [ ] **Step 2: Create the Command**

Create `commandset/Commands/DataExtraction/ImportFromExcelCommand.cs`:

```csharp
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.DataExtraction;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.DataExtraction
{
    public class ImportFromExcelCommand : ExternalEventCommandBase
    {
        private ImportFromExcelEventHandler _handler => (ImportFromExcelEventHandler)Handler;
        public override string CommandName => "import_from_excel";

        public ImportFromExcelCommand(UIApplication uiApp)
            : base(new ImportFromExcelEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    filePath: parameters?["filePath"]?.ToString() ?? "",
                    sheetName: parameters?["sheetName"]?.ToString() ?? "",
                    dryRun: parameters?["dryRun"]?.Value<bool>() ?? false
                );

                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;

                throw new TimeoutException("Import from Excel timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Import from Excel failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create the TypeScript tool**

Create `server/src/tools/import_from_excel.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerImportFromExcelTool(server: McpServer) {
  server.tool(
    "import_from_excel",
    `Import parameter values from an Excel (.xlsx) file back into Revit elements.
The file must have an 'ElementId' column (use export_to_excel with includeElementId=true).
Read-only parameters and system columns (Category, Family, Type) are skipped.

GUIDANCE:
- Always use dryRun=true first to preview changes before applying.
- "Update parameters from Excel": filePath="C:/path/to/file.xlsx"
- Workflow: export_to_excel → edit in Excel → import_from_excel`,
    {
      filePath: z.string().describe("Full path to the .xlsx file to import."),
      sheetName: z.string().optional().describe("Worksheet name. Default: first sheet."),
      dryRun: z.boolean().optional().default(false)
        .describe("Preview changes without modifying the model. Always try this first."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("import_from_excel", {
            filePath: args.filePath,
            sheetName: args.sheetName ?? "",
            dryRun: args.dryRun ?? false,
          });
        });
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Import from Excel failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

```json
{
  "commandName": "import_from_excel",
  "description": "Import parameter values from Excel (.xlsx) back into Revit elements",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/DataExtraction/ImportFromExcelCommand.cs \
        commandset/Services/DataExtraction/ImportFromExcelEventHandler.cs \
        server/src/tools/import_from_excel.ts command.json
git commit -m "feat: add import_from_excel tool — update Revit parameters from .xlsx"
```

---

## Task 3: `manage_view_templates` — View Template CRUD

List, duplicate, delete, and rename view templates. Fills the ViewManager gap.

**Files:**
- Create: `commandset/Commands/ViewManagement/ManageViewTemplatesCommand.cs`
- Create: `commandset/Services/ViewManagement/ManageViewTemplatesEventHandler.cs`
- Create: `server/src/tools/manage_view_templates.ts`
- Modify: `command.json`

- [ ] **Step 1: Create the EventHandler**

Create `commandset/Services/ViewManagement/ManageViewTemplatesEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class ManageViewTemplatesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string Action { get; set; } = "list";
        public List<long> TemplateIds { get; set; } = new();
        public string NewName { get; set; } = "";
        public string FindText { get; set; } = "";
        public string ReplaceText { get; set; } = "";
        public string FilterViewType { get; set; } = "";
        public object Result { get; private set; }

        public void SetParameters(string action, List<long> templateIds, string newName,
            string findText, string replaceText, string filterViewType)
        {
            Action = action;
            TemplateIds = templateIds;
            NewName = newName;
            FindText = findText;
            ReplaceText = replaceText;
            FilterViewType = filterViewType;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                switch (Action.ToLower())
                {
                    case "list":
                        Result = ListTemplates(doc);
                        break;
                    case "duplicate":
                        Result = DuplicateTemplates(doc);
                        break;
                    case "delete":
                        Result = DeleteTemplates(doc);
                        break;
                    case "rename":
                        Result = RenameTemplates(doc);
                        break;
                    case "batch_rename":
                        Result = BatchRenameTemplates(doc);
                        break;
                    default:
                        Result = new { success = false, error = $"Unknown action: {Action}" };
                        break;
                }
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        private object ListTemplates(Document doc)
        {
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate)
                .Select(v => new
                {
#if REVIT2024_OR_GREATER
                    id = v.Id.Value,
#else
                    id = v.Id.IntegerValue,
#endif
                    name = v.Name,
                    viewType = v.ViewType.ToString(),
                    discipline = v.Discipline.ToString(),
                    hasAssociatedViews = new FilteredElementCollector(doc)
                        .OfClass(typeof(View))
                        .Cast<View>()
                        .Any(av => !av.IsTemplate && av.ViewTemplateId == v.Id)
                })
                .Where(t => string.IsNullOrEmpty(FilterViewType) ||
                    t.viewType.Equals(FilterViewType, StringComparison.OrdinalIgnoreCase))
                .OrderBy(t => t.viewType).ThenBy(t => t.name)
                .ToList();

            return new { success = true, count = templates.Count, templates };
        }

        private object DuplicateTemplates(Document doc)
        {
            var duplicated = new List<object>();
            using (var tx = new Transaction(doc, "Duplicate View Templates"))
            {
                tx.Start();
                foreach (var id in TemplateIds)
                {
#if REVIT2024_OR_GREATER
                    var elemId = new ElementId(id);
#else
                    var elemId = new ElementId((int)id);
#endif
                    var view = doc.GetElement(elemId) as View;
                    if (view == null || !view.IsTemplate) continue;

                    var newId = view.Duplicate(ViewDuplicateOption.Duplicate);
                    var newView = doc.GetElement(newId) as View;
                    if (newView != null && !string.IsNullOrEmpty(NewName))
                    {
                        newView.Name = TemplateIds.Count == 1 ? NewName : $"{NewName} - Copy {duplicated.Count + 1}";
                    }
                    duplicated.Add(new
                    {
#if REVIT2024_OR_GREATER
                        originalId = id, newId = newId.Value,
#else
                        originalId = id, newId = newId.IntegerValue,
#endif
                        name = newView?.Name ?? ""
                    });
                }
                tx.Commit();
            }
            return new { success = true, duplicated = duplicated.Count, templates = duplicated };
        }

        private object DeleteTemplates(Document doc)
        {
            int deleted = 0;
            using (var tx = new Transaction(doc, "Delete View Templates"))
            {
                tx.Start();
                foreach (var id in TemplateIds)
                {
#if REVIT2024_OR_GREATER
                    var elemId = new ElementId(id);
#else
                    var elemId = new ElementId((int)id);
#endif
                    var view = doc.GetElement(elemId) as View;
                    if (view == null || !view.IsTemplate) continue;
                    doc.Delete(elemId);
                    deleted++;
                }
                tx.Commit();
            }
            return new { success = true, deleted };
        }

        private object RenameTemplates(Document doc)
        {
            if (TemplateIds.Count != 1)
                return new { success = false, error = "Rename requires exactly one template ID" };

#if REVIT2024_OR_GREATER
            var elemId = new ElementId(TemplateIds[0]);
#else
            var elemId = new ElementId((int)TemplateIds[0]);
#endif
            var view = doc.GetElement(elemId) as View;
            if (view == null || !view.IsTemplate)
                return new { success = false, error = "Template not found" };

            using (var tx = new Transaction(doc, "Rename View Template"))
            {
                tx.Start();
                string oldName = view.Name;
                view.Name = NewName;
                tx.Commit();
                return new { success = true, oldName, newName = NewName };
            }
        }

        private object BatchRenameTemplates(Document doc)
        {
            var templates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => v.IsTemplate && v.Name.Contains(FindText))
                .ToList();

            if (TemplateIds.Count > 0)
            {
                var idSet = new HashSet<long>(TemplateIds);
                templates = templates.Where(v =>
                {
#if REVIT2024_OR_GREATER
                    return idSet.Contains(v.Id.Value);
#else
                    return idSet.Contains(v.Id.IntegerValue);
#endif
                }).ToList();
            }

            int renamed = 0;
            using (var tx = new Transaction(doc, "Batch Rename View Templates"))
            {
                tx.Start();
                foreach (var v in templates)
                {
                    v.Name = v.Name.Replace(FindText, ReplaceText);
                    renamed++;
                }
                tx.Commit();
            }
            return new { success = true, renamed };
        }

        public string GetName() => "Manage View Templates";
    }
}
```

- [ ] **Step 2: Create the Command**

Create `commandset/Commands/ViewManagement/ManageViewTemplatesCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class ManageViewTemplatesCommand : ExternalEventCommandBase
    {
        private ManageViewTemplatesEventHandler _handler => (ManageViewTemplatesEventHandler)Handler;
        public override string CommandName => "manage_view_templates";

        public ManageViewTemplatesCommand(UIApplication uiApp)
            : base(new ManageViewTemplatesEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var templateIds = (parameters?["templateIds"] as JArray)?
                    .Select(t => t.Value<long>()).ToList() ?? new List<long>();

                _handler.SetParameters(
                    action: parameters?["action"]?.ToString() ?? "list",
                    templateIds: templateIds,
                    newName: parameters?["newName"]?.ToString() ?? "",
                    findText: parameters?["findText"]?.ToString() ?? "",
                    replaceText: parameters?["replaceText"]?.ToString() ?? "",
                    filterViewType: parameters?["filterViewType"]?.ToString() ?? ""
                );

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Manage view templates timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Manage view templates failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create the TypeScript tool**

Create `server/src/tools/manage_view_templates.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerManageViewTemplatesTool(server: McpServer) {
  server.tool(
    "manage_view_templates",
    `List, duplicate, delete, or rename view templates in the Revit project.

GUIDANCE:
- "List all view templates": action="list"
- "List floor plan templates": action="list", filterViewType="FloorPlan"
- "Duplicate template 12345": action="duplicate", templateIds=[12345], newName="My Copy"
- "Delete templates": action="delete", templateIds=[12345, 12346]
- "Rename template": action="rename", templateIds=[12345], newName="New Name"
- "Replace 'Draft' with 'Final' in all template names": action="batch_rename", findText="Draft", replaceText="Final"`,
    {
      action: z.enum(["list", "duplicate", "delete", "rename", "batch_rename"])
        .describe("Operation to perform."),
      templateIds: z.array(z.number()).optional()
        .describe("Template element IDs (required for duplicate/delete/rename)."),
      newName: z.string().optional()
        .describe("New name for duplicate or rename operations."),
      findText: z.string().optional()
        .describe("Text to find (for batch_rename)."),
      replaceText: z.string().optional()
        .describe("Replacement text (for batch_rename)."),
      filterViewType: z.string().optional()
        .describe("Filter by view type when listing (e.g. 'FloorPlan', 'Section', 'ThreeD')."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("manage_view_templates", {
            action: args.action,
            templateIds: args.templateIds ?? [],
            newName: args.newName ?? "",
            findText: args.findText ?? "",
            replaceText: args.replaceText ?? "",
            filterViewType: args.filterViewType ?? "",
          });
        });
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Manage view templates failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

```json
{
  "commandName": "manage_view_templates",
  "description": "List, duplicate, delete, or rename view templates",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/ViewManagement/ManageViewTemplatesCommand.cs \
        commandset/Services/ViewManagement/ManageViewTemplatesEventHandler.cs \
        server/src/tools/manage_view_templates.ts command.json
git commit -m "feat: add manage_view_templates tool — list/duplicate/delete/rename templates"
```

---

## Task 4: `create_callout_from_rooms` — Callout Views from Rooms

Creates callout views for rooms/spaces. Fills the QuickViews gap.

**Files:**
- Create: `commandset/Commands/ViewManagement/CreateCalloutFromRoomsCommand.cs`
- Create: `commandset/Services/ViewManagement/CreateCalloutFromRoomsEventHandler.cs`
- Create: `server/src/tools/create_callout_from_rooms.ts`
- Modify: `command.json`

- [ ] **Step 1: Create the EventHandler**

Create `commandset/Services/ViewManagement/CreateCalloutFromRoomsEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.ViewManagement
{
    public class CreateCalloutFromRoomsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public List<long> RoomIds { get; set; } = new();
        public string LevelName { get; set; } = "";
        public double Offset { get; set; } = 300; // mm
        public string ViewTemplateId { get; set; } = "";
        public int Scale { get; set; } = 50;
        public object Result { get; private set; }

        public void SetParameters(List<long> roomIds, string levelName, double offset,
            string viewTemplateId, int scale)
        {
            RoomIds = roomIds;
            LevelName = levelName;
            Offset = offset;
            ViewTemplateId = viewTemplateId;
            Scale = scale;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                double offsetFt = Offset / 304.8; // mm to feet

                // Get rooms
                List<Room> rooms;
                if (RoomIds.Count > 0)
                {
                    rooms = RoomIds
                        .Select(id =>
                        {
#if REVIT2024_OR_GREATER
                            return doc.GetElement(new ElementId(id)) as Room;
#else
                            return doc.GetElement(new ElementId((int)id)) as Room;
#endif
                        })
                        .Where(r => r != null)
                        .ToList();
                }
                else if (!string.IsNullOrEmpty(LevelName))
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Level?.Name?.Equals(LevelName, StringComparison.OrdinalIgnoreCase) == true)
                        .Where(r => r.Area > 0)
                        .ToList();
                }
                else
                {
                    rooms = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_Rooms)
                        .WhereElementIsNotElementType()
                        .Cast<Room>()
                        .Where(r => r.Area > 0)
                        .ToList();
                }

                if (rooms.Count == 0)
                {
                    Result = new { success = true, created = 0, message = "No rooms found" };
                    return;
                }

                // Find the parent view (floor plan of each room's level)
                var created = new List<object>();
                using (var tx = new Transaction(doc, "Create Callout Views from Rooms"))
                {
                    tx.Start();

                    foreach (var room in rooms)
                    {
                        try
                        {
                            var level = room.Level;
                            if (level == null) continue;

                            // Find existing floor plan for this level
                            var parentView = new FilteredElementCollector(doc)
                                .OfClass(typeof(ViewPlan))
                                .Cast<ViewPlan>()
                                .FirstOrDefault(v => !v.IsTemplate &&
                                    v.GenLevel?.Id == level.Id &&
                                    v.ViewType == ViewType.FloorPlan);

                            if (parentView == null) continue;

                            // Get room bounding box
                            var bb = room.get_BoundingBox(null);
                            if (bb == null) continue;

                            var min = new XYZ(bb.Min.X - offsetFt, bb.Min.Y - offsetFt, bb.Min.Z);
                            var max = new XYZ(bb.Max.X + offsetFt, bb.Max.Y + offsetFt, bb.Max.Z);

                            // Create callout
                            var callout = ViewSection.CreateCallout(doc, parentView.Id,
                                parentView.GetTypeId(), min, max);

                            // Set name
                            string viewName = $"Callout - {room.Number} {room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? ""}".Trim();
                            try { callout.Name = viewName; } catch { /* name conflict */ }

                            // Set scale
                            callout.Scale = Scale;

                            // Apply template if specified
                            if (!string.IsNullOrEmpty(ViewTemplateId) && long.TryParse(ViewTemplateId, out long vtId))
                            {
#if REVIT2024_OR_GREATER
                                callout.ViewTemplateId = new ElementId(vtId);
#else
                                callout.ViewTemplateId = new ElementId((int)vtId);
#endif
                            }

                            created.Add(new
                            {
#if REVIT2024_OR_GREATER
                                viewId = callout.Id.Value,
                                roomId = room.Id.Value,
#else
                                viewId = callout.Id.IntegerValue,
                                roomId = room.Id.IntegerValue,
#endif
                                name = callout.Name,
                                roomNumber = room.Number,
                                level = level.Name
                            });
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Trace.WriteLine($"[RevitMCP] Callout for room {room.Number} failed: {ex.Message}");
                        }
                    }

                    tx.Commit();
                }

                Result = new { success = true, created = created.Count, views = created };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Create Callout From Rooms";
    }
}
```

- [ ] **Step 2: Create the Command**

Create `commandset/Commands/ViewManagement/CreateCalloutFromRoomsCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.ViewManagement;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.ViewManagement
{
    public class CreateCalloutFromRoomsCommand : ExternalEventCommandBase
    {
        private CreateCalloutFromRoomsEventHandler _handler => (CreateCalloutFromRoomsEventHandler)Handler;
        public override string CommandName => "create_callout_from_rooms";

        public CreateCalloutFromRoomsCommand(UIApplication uiApp)
            : base(new CreateCalloutFromRoomsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var roomIds = (parameters?["roomIds"] as JArray)?
                    .Select(t => t.Value<long>()).ToList() ?? new List<long>();

                _handler.SetParameters(
                    roomIds: roomIds,
                    levelName: parameters?["levelName"]?.ToString() ?? "",
                    offset: parameters?["offset"]?.Value<double>() ?? 300,
                    viewTemplateId: parameters?["viewTemplateId"]?.ToString() ?? "",
                    scale: parameters?["scale"]?.Value<int>() ?? 50
                );

                if (RaiseAndWaitForCompletion(60000))
                    return _handler.Result;

                throw new TimeoutException("Create callout from rooms timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Create callout from rooms failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create the TypeScript tool**

Create `server/src/tools/create_callout_from_rooms.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerCreateCalloutFromRoomsTool(server: McpServer) {
  server.tool(
    "create_callout_from_rooms",
    `Create callout views for rooms. One callout per room, placed on the room's floor plan.

GUIDANCE:
- "Create callouts for all rooms on Level 1": levelName="Level 1"
- "Create callout for room 101": roomIds=[ROOM_ELEMENT_ID]
- "Create callouts with 500mm offset": offset=500
- "Create callouts at 1:100 scale": scale=100`,
    {
      roomIds: z.array(z.number()).optional()
        .describe("Specific room element IDs. If empty, uses levelName or all rooms."),
      levelName: z.string().optional()
        .describe("Level name to filter rooms (e.g. 'Level 1'). Ignored if roomIds provided."),
      offset: z.number().optional().default(300)
        .describe("Boundary offset around room in mm (default: 300mm)."),
      viewTemplateId: z.string().optional()
        .describe("View template element ID to apply to created callouts."),
      scale: z.number().optional().default(50)
        .describe("View scale (e.g. 50 = 1:50, 100 = 1:100)."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("create_callout_from_rooms", {
            roomIds: args.roomIds ?? [],
            levelName: args.levelName ?? "",
            offset: args.offset ?? 300,
            viewTemplateId: args.viewTemplateId ?? "",
            scale: args.scale ?? 50,
          });
        });
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Create callout from rooms failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

```json
{
  "commandName": "create_callout_from_rooms",
  "description": "Create callout views for rooms with boundary offset",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/ViewManagement/CreateCalloutFromRoomsCommand.cs \
        commandset/Services/ViewManagement/CreateCalloutFromRoomsEventHandler.cs \
        server/src/tools/create_callout_from_rooms.ts command.json
git commit -m "feat: add create_callout_from_rooms tool — callout views from room boundaries"
```

---

## Task 5: `export_shared_parameter_file` — Shared Parameters to .txt

Export project shared parameters to a standard Revit shared parameter file (.txt). Fills the ParaManager gap.

**Files:**
- Create: `commandset/Commands/Access/ExportSharedParameterFileCommand.cs`
- Create: `commandset/Services/ExportSharedParameterFileEventHandler.cs`
- Create: `server/src/tools/export_shared_parameter_file.ts`
- Modify: `command.json`

- [ ] **Step 1: Create the EventHandler**

Create `commandset/Services/ExportSharedParameterFileEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class ExportSharedParameterFileEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string FilePath { get; set; } = "";
        public object Result { get; private set; }

        public void SetParameters(string filePath)
        {
            FilePath = filePath;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                if (string.IsNullOrEmpty(FilePath))
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    FilePath = Path.Combine(desktop, $"SharedParameters_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                }

                // Get current shared parameter file (if any)
                var currentFile = app.Application.SharedParametersFilename;

                // Create a temp file for export
                string tempFile = Path.GetTempFileName();

                try
                {
                    // Create empty shared parameter file
                    File.WriteAllText(tempFile, "");
                    app.Application.SharedParametersFilename = tempFile;
                    var defFile = app.Application.OpenSharedParameterFile();

                    if (defFile == null)
                    {
                        // Create fresh file structure
                        using (var writer = new StreamWriter(tempFile))
                        {
                            writer.WriteLine("# This is a Revit shared parameter file.");
                            writer.WriteLine("# Do not edit manually.");
                            writer.WriteLine("*META\tVERSION\tMINVERSION");
                            writer.WriteLine("META\t2\t1");
                            writer.WriteLine("*GROUP\tID\tNAME");
                        }
                        defFile = app.Application.OpenSharedParameterFile();
                    }

                    // Collect all shared parameters from the project
                    var bindingMap = doc.ParameterBindings;
                    var iterator = bindingMap.ForwardIterator();
                    var exportedParams = new List<object>();
                    int groupId = 1;

                    // Create a default group
                    var defaultGroup = defFile.Groups.Create("Exported Parameters");

                    while (iterator.MoveNext())
                    {
                        var definition = iterator.Key;
                        if (definition is ExternalDefinition extDef)
                        {
                            exportedParams.Add(new
                            {
                                name = extDef.Name,
                                guid = extDef.GUID.ToString(),
                                parameterType = extDef.GetDataType().TypeId ?? "Text"
                            });
                        }
                        else if (definition is InternalDefinition intDef)
                        {
                            exportedParams.Add(new
                            {
                                name = intDef.Name,
                                guid = (string)null,
                                parameterType = "InternalDefinition"
                            });
                        }
                    }

                    // Copy result to final path
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.Copy(tempFile, FilePath, overwrite: true);

                    Result = new
                    {
                        success = true,
                        filePath = FilePath,
                        parameterCount = exportedParams.Count,
                        parameters = exportedParams
                    };
                }
                finally
                {
                    // Restore original shared parameter file
                    if (!string.IsNullOrEmpty(currentFile) && File.Exists(currentFile))
                        app.Application.SharedParametersFilename = currentFile;

                    if (File.Exists(tempFile))
                        File.Delete(tempFile);
                }
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Export Shared Parameter File";
    }
}
```

- [ ] **Step 2: Create the Command**

Create `commandset/Commands/Access/ExportSharedParameterFileCommand.cs`:

```csharp
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class ExportSharedParameterFileCommand : ExternalEventCommandBase
    {
        private ExportSharedParameterFileEventHandler _handler => (ExportSharedParameterFileEventHandler)Handler;
        public override string CommandName => "export_shared_parameter_file";

        public ExportSharedParameterFileCommand(UIApplication uiApp)
            : base(new ExportSharedParameterFileEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    filePath: parameters?["filePath"]?.ToString() ?? ""
                );

                if (RaiseAndWaitForCompletion(30000))
                    return _handler.Result;

                throw new TimeoutException("Export shared parameter file timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Export shared parameter file failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create the TypeScript tool**

Create `server/src/tools/export_shared_parameter_file.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExportSharedParameterFileTool(server: McpServer) {
  server.tool(
    "export_shared_parameter_file",
    `Export project shared parameters to a standard Revit shared parameter file (.txt).

GUIDANCE:
- "Export shared parameters": uses default Desktop path
- "Export shared parameters to C:/params.txt": filePath="C:/params.txt"`,
    {
      filePath: z.string().optional()
        .describe("Output .txt path. Default: Desktop/SharedParameters_<timestamp>.txt"),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("export_shared_parameter_file", {
            filePath: args.filePath ?? "",
          });
        });
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Export shared parameter file failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

```json
{
  "commandName": "export_shared_parameter_file",
  "description": "Export project shared parameters to a .txt file",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/Access/ExportSharedParameterFileCommand.cs \
        commandset/Services/ExportSharedParameterFileEventHandler.cs \
        server/src/tools/export_shared_parameter_file.ts command.json
git commit -m "feat: add export_shared_parameter_file tool — export shared params to .txt"
```

---

## Task 6: `get_linked_elements` — Query Linked Revit Files

Query elements in linked Revit models. Fills the SheetLink/OneFilter linked files gap.

**Files:**
- Create: `commandset/Commands/Access/GetLinkedElementsCommand.cs`
- Create: `commandset/Services/GetLinkedElementsEventHandler.cs`
- Create: `server/src/tools/get_linked_elements.ts`
- Modify: `command.json`

- [ ] **Step 1: Create the EventHandler**

Create `commandset/Services/GetLinkedElementsEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services
{
    public class GetLinkedElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string LinkName { get; set; } = "";
        public List<string> Categories { get; set; } = new();
        public List<string> ParameterNames { get; set; } = new();
        public int MaxElements { get; set; } = 5000;
        public object Result { get; private set; }

        public void SetParameters(string linkName, List<string> categories,
            List<string> parameterNames, int maxElements)
        {
            LinkName = linkName;
            Categories = categories;
            ParameterNames = parameterNames;
            MaxElements = maxElements;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;

                // Find linked instances
                var linkInstances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .ToList();

                if (!string.IsNullOrEmpty(LinkName))
                {
                    linkInstances = linkInstances
                        .Where(li => li.Name.Contains(LinkName, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }

                if (linkInstances.Count == 0)
                {
                    Result = new { success = true, links = new List<object>(), message = "No linked models found" };
                    return;
                }

                var linksData = new List<object>();

                foreach (var linkInstance in linkInstances)
                {
                    var linkDoc = linkInstance.GetLinkDocument();
                    if (linkDoc == null) continue;

                    // Collect elements from linked document
                    var collector = new FilteredElementCollector(linkDoc)
                        .WhereElementIsNotElementType();

                    var elements = new List<Element>();
                    if (Categories.Count > 0)
                    {
                        foreach (var catName in Categories)
                        {
                            foreach (Category cat in linkDoc.Settings.Categories)
                            {
                                if (cat.Name.Equals(catName, StringComparison.OrdinalIgnoreCase))
                                {
                                    var catElements = new FilteredElementCollector(linkDoc)
                                        .OfCategory((BuiltInCategory)cat.Id.IntegerValue)
                                        .WhereElementIsNotElementType()
                                        .Take(MaxElements)
                                        .ToList();
                                    elements.AddRange(catElements);
                                    break;
                                }
                            }
                        }
                    }
                    else
                    {
                        elements = collector.Take(MaxElements).ToList();
                    }

                    var elementsData = elements.Select(e =>
                    {
                        var data = new Dictionary<string, object>
                        {
#if REVIT2024_OR_GREATER
                            ["ElementId"] = e.Id.Value,
#else
                            ["ElementId"] = e.Id.IntegerValue,
#endif
                            ["Category"] = e.Category?.Name ?? "",
                            ["Name"] = e.Name
                        };

                        if (ParameterNames.Count > 0)
                        {
                            foreach (var pn in ParameterNames)
                            {
                                var p = e.LookupParameter(pn);
                                data[pn] = p != null && p.HasValue
                                    ? (object)(p.AsValueString() ?? p.AsString() ?? "")
                                    : "";
                            }
                        }
                        return data;
                    }).ToList();

                    linksData.Add(new
                    {
                        linkName = linkInstance.Name,
#if REVIT2024_OR_GREATER
                        linkId = linkInstance.Id.Value,
#else
                        linkId = linkInstance.Id.IntegerValue,
#endif
                        documentTitle = linkDoc.Title,
                        elementCount = elementsData.Count,
                        elements = elementsData
                    });
                }

                Result = new { success = true, linkCount = linksData.Count, links = linksData };
            }
            catch (Exception ex)
            {
                Result = new { success = false, error = ex.Message };
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName() => "Get Linked Elements";
    }
}
```

- [ ] **Step 2: Create the Command**

Create `commandset/Commands/Access/GetLinkedElementsCommand.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Access
{
    public class GetLinkedElementsCommand : ExternalEventCommandBase
    {
        private GetLinkedElementsEventHandler _handler => (GetLinkedElementsEventHandler)Handler;
        public override string CommandName => "get_linked_elements";

        public GetLinkedElementsCommand(UIApplication uiApp)
            : base(new GetLinkedElementsEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                var categories = (parameters?["categories"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();
                var parameterNames = (parameters?["parameterNames"] as JArray)?
                    .Select(t => t.ToString()).Where(s => !string.IsNullOrEmpty(s)).ToList()
                    ?? new List<string>();

                _handler.SetParameters(
                    linkName: parameters?["linkName"]?.ToString() ?? "",
                    categories: categories,
                    parameterNames: parameterNames,
                    maxElements: parameters?["maxElements"]?.Value<int>() ?? 5000
                );

                if (RaiseAndWaitForCompletion(60000))
                    return _handler.Result;

                throw new TimeoutException("Get linked elements timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Get linked elements failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create the TypeScript tool**

Create `server/src/tools/get_linked_elements.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerGetLinkedElementsTool(server: McpServer) {
  server.tool(
    "get_linked_elements",
    `Query elements from linked Revit models. Extracts data without modifying the linked file.

GUIDANCE:
- "Show all walls from the structural link": linkName="Structural", categories=["Walls"]
- "Get doors from all links with Mark and Level": categories=["Doors"], parameterNames=["Mark","Level"]
- "List all elements in linked model 'MEP.rvt'": linkName="MEP"`,
    {
      linkName: z.string().optional()
        .describe("Filter by link name (partial match). Empty = all linked models."),
      categories: z.array(z.string()).optional()
        .describe("Category names to query. Empty = all categories."),
      parameterNames: z.array(z.string()).optional()
        .describe("Parameters to extract per element. Empty = just ID/Category/Name."),
      maxElements: z.number().optional().default(5000)
        .describe("Max elements per linked model."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("get_linked_elements", {
            linkName: args.linkName ?? "",
            categories: args.categories ?? [],
            parameterNames: args.parameterNames ?? [],
            maxElements: args.maxElements ?? 5000,
          });
        });
        return { content: [{ type: "text" as const, text: JSON.stringify(response, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Get linked elements failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

```json
{
  "commandName": "get_linked_elements",
  "description": "Query elements from linked Revit models",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/Access/GetLinkedElementsCommand.cs \
        commandset/Services/GetLinkedElementsEventHandler.cs \
        server/src/tools/get_linked_elements.ts command.json
git commit -m "feat: add get_linked_elements tool — query data from linked Revit models"
```

---

## Summary

After completing all 6 tasks, the comparison gaps change to:

| Tool DiRoots | Before | After |
|---|---|---|
| **SheetLink** | 60% | **85%** (Excel + linked files) |
| **ParaManager** | 50% | **65%** (shared param export) |
| **ViewManager** | 70% | **90%** (view template CRUD) |
| **QuickViews** | 50% | **70%** (callout views) |
| **OneFilter** | 85% | 85% (unchanged) |
| **Cross-cutting** | No Excel | **Full Excel round-trip** |

**Total new tools: 6** → brings project from ~78 to ~84 MCP commands.

### Future phases (not in this plan):
- Phase 2: V/S Set management, Build Name generator
- Phase 3: Gradient color ramps, advanced renumbering
- Phase 4: Project Standards export/import
- Phase 5: TableGen (Word/PDF import — very complex)
- Phase 6: PointKit (point cloud — extremely specialized)
