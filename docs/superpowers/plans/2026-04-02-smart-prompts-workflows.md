# Smart Prompts, Workflows & Next-Step Suggestions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add contextual prompt chips to the Revit chat panel, 5 pre-packaged workflow MCP tools, and next-step suggestions to ~8 existing tool responses.

**Architecture:** Feature 1 (chips) is C#/WPF only. Feature 2 (workflows) follows the existing 3-file MCP tool pattern (C# Command + EventHandler + TypeScript tool). Feature 3 (suggestions) is TypeScript-only modifications to existing tools. All features are independent and can be built in any order.

**Tech Stack:** C#/.NET 8, WPF, Revit API, TypeScript, Zod, @modelcontextprotocol/sdk

---

## Task 1: Next-Step Suggestions Utility + First Batch

Add `suggestedNextSteps` to 4 existing tool responses: check_model_health, get_warnings, export_to_excel, export_elements_data.

**Files:**
- Create: `server/src/utils/suggestions.ts`
- Modify: `server/src/tools/check_model_health.ts`
- Modify: `server/src/tools/get_warnings.ts`
- Modify: `server/src/tools/export_to_excel.ts`
- Modify: `server/src/tools/export_elements_data.ts`

- [ ] **Step 1: Create suggestions utility**

Create `server/src/utils/suggestions.ts`:

```typescript
export interface NextStep {
  prompt: string;
  reason: string;
}

export function addSuggestions(response: any, suggestions: (NextStep | null)[]): any {
  const filtered = suggestions.filter((s): s is NextStep => s !== null);
  if (filtered.length === 0) return response;
  return { ...response, suggestedNextSteps: filtered };
}

export function suggestIf(condition: boolean, prompt: string, reason: string): NextStep | null {
  return condition ? { prompt, reason } : null;
}
```

- [ ] **Step 2: Add suggestions to check_model_health.ts**

In `server/src/tools/check_model_health.ts`, add imports and wrap the response:

```typescript
// Add at top:
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

// Replace the return block (inside try, after const response = ...):
const data = typeof response === 'object' ? response : {};
const score = data.healthScore ?? data.score ?? 100;
const warningCount = data.warningCount ?? data.warnings?.length ?? 0;
const unusedFamilies = data.unusedFamilies ?? data.unusedFamilyCount ?? 0;

const enriched = addSuggestions(response, [
  suggestIf(warningCount > 0, `Show me all ${warningCount} model warnings`, `${warningCount} warnings found in the model`),
  suggestIf(unusedFamilies > 0, `Purge ${unusedFamilies} unused families`, `${unusedFamilies} unused families detected`),
  suggestIf(score < 80, "Audit all families for health issues", "Health score below 80 — detailed family audit recommended"),
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 3: Add suggestions to get_warnings.ts**

Read the file first. Then add imports and wrap the response similarly:

```typescript
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

// After const response = ...:
const data = typeof response === 'object' ? response : {};
const count = data.warningCount ?? data.warnings?.length ?? 0;

const enriched = addSuggestions(response, [
  suggestIf(count > 0, "Isolate the elements with the most warnings in the current view", `${count} warnings need attention`),
  suggestIf(count > 20, "Check model health for an overall score", "Many warnings — a health audit gives the big picture"),
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 4: Add suggestions to export_to_excel.ts**

```typescript
import { addSuggestions } from "../utils/suggestions.js";

// After const response = ...:
const data = typeof response === 'object' ? response : {};
const filePath = data.filePath ?? "";

const enriched = addSuggestions(response, [
  { prompt: `When you're done editing, ask me to import ${filePath} back into Revit`, reason: "Excel roundtrip: edit the file then re-import" },
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 5: Add suggestions to export_elements_data.ts**

```typescript
import { addSuggestions } from "../utils/suggestions.js";

// After const response = ...:
const enriched = addSuggestions(response, [
  { prompt: "Update these elements with the modified data using sync_csv_parameters", reason: "Export-edit-import workflow" },
  { prompt: "Export this data to Excel for easier editing", reason: "Excel is more convenient for bulk parameter editing" },
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 6: Build and verify**

Run: `cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 7: Commit**

```bash
git add server/src/utils/suggestions.ts server/src/tools/check_model_health.ts \
        server/src/tools/get_warnings.ts server/src/tools/export_to_excel.ts \
        server/src/tools/export_elements_data.ts
git commit -m "feat: add next-step suggestions to check_model_health, get_warnings, export tools"
```

---

## Task 2: Next-Step Suggestions — Second Batch

Add `suggestedNextSteps` to 4 more tools: clash_detection, create_views_from_rooms, batch_create_sheets, audit_families.

**Files:**
- Modify: `server/src/tools/clash_detection.ts`
- Modify: `server/src/tools/create_views_from_rooms.ts`
- Modify: `server/src/tools/batch_create_sheets.ts`
- Modify: `server/src/tools/audit_families.ts`

- [ ] **Step 1: Add suggestions to clash_detection.ts**

Read the file first. Add imports and wrap response:

```typescript
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

// After const response = ...:
const data = typeof response === 'object' ? response : {};
const clashCount = data.clashCount ?? data.clashes?.length ?? 0;

const enriched = addSuggestions(response, [
  suggestIf(clashCount > 0, "Isolate the clashing elements in the current view", `${clashCount} clashes found`),
  suggestIf(clashCount > 0, "Create a section box around the clash area", "Visual review of interference zone"),
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 2: Add suggestions to create_views_from_rooms.ts**

```typescript
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

// After const response = ...:
const data = typeof response === 'object' ? response : {};
const viewCount = data.viewsCreated ?? data.created ?? 0;

const enriched = addSuggestions(response, [
  suggestIf(viewCount > 0, "Tag all rooms in the current view", "Complete room documentation"),
  suggestIf(viewCount > 0, "Create a color legend by department", "Visual room categorization"),
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 3: Add suggestions to batch_create_sheets.ts**

```typescript
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

// After const response = ...:
const data = typeof response === 'object' ? response : {};
const created = data.sheetsCreated ?? data.created ?? 0;

const enriched = addSuggestions(response, [
  suggestIf(created > 0, "Place views on the new sheets", "Sheets are empty — add viewports"),
  suggestIf(created > 0, "Align viewports across all sheets", "Consistent viewport positioning"),
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 4: Add suggestions to audit_families.ts**

```typescript
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

// After const response = ...:
const data = typeof response === 'object' ? response : {};
const unusedCount = data.unusedCount ?? data.unused?.length ?? 0;
const issueCount = data.issueCount ?? data.issues?.length ?? 0;

const enriched = addSuggestions(response, [
  suggestIf(unusedCount > 0, `Purge ${unusedCount} unused families and types`, `${unusedCount} unused families found`),
  suggestIf(issueCount > 0, "Rename problematic families for consistency", `${issueCount} family issues detected`),
]);

return {
  content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }],
};
```

- [ ] **Step 5: Build and verify**

Run: `cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add server/src/tools/clash_detection.ts server/src/tools/create_views_from_rooms.ts \
        server/src/tools/batch_create_sheets.ts server/src/tools/audit_families.ts
git commit -m "feat: add next-step suggestions to clash, views, sheets, audit tools"
```

---

## Task 3: Contextual Prompt Chips (Revit Chat Panel)

Add a horizontal bar of clickable prompt chips above the input area. Chips change based on the active Revit view and selection state.

**Files:**
- Modify: `plugin/UI/MCPDockablePanel.xaml`
- Modify: `plugin/UI/MCPDockablePanel.xaml.cs`

- [ ] **Step 1: Add chips XAML to MCPDockablePanel.xaml**

The panel has 4 rows: header (Row 0), messages (Row 1), typing (Row 2), input (Row 3). Insert a new Row between typing and input for the chips. Update RowDefinitions to add Row 4 and shift input to Row 4.

Replace the Grid.RowDefinitions and add the chips row:

```xml
<Grid.RowDefinitions>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="*"/>
    <RowDefinition Height="Auto"/>
    <RowDefinition Height="Auto"/>  <!-- NEW: prompt chips -->
    <RowDefinition Height="Auto"/>
</Grid.RowDefinitions>
```

Change the input Border from `Grid.Row="3"` to `Grid.Row="4"`.

Add the chips bar as new Row 3 (between typing indicator and input):

```xml
<!-- Prompt Chips - contextual suggestions -->
<Border Grid.Row="3" x:Name="ChipsBar" Background="#FAF9F7" Padding="12,6"
        BorderBrush="#F0EEEB" BorderThickness="0,1,0,0"
        Visibility="Collapsed">
    <ScrollViewer HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Disabled">
        <ItemsControl x:Name="PromptChips">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <WrapPanel Orientation="Horizontal"/>
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border CornerRadius="12" Background="#E3F2FD" Margin="0,0,6,4"
                            Padding="10,5" Cursor="Hand"
                            MouseLeftButtonDown="Chip_Click">
                        <Border.Style>
                            <Style TargetType="Border">
                                <Style.Triggers>
                                    <Trigger Property="IsMouseOver" Value="True">
                                        <Setter Property="Background" Value="#BBDEFB"/>
                                    </Trigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <TextBlock Text="{Binding Text}" FontSize="12" Foreground="#1565C0"/>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </ScrollViewer>
</Border>
```

- [ ] **Step 2: Add PromptChip model and context logic to code-behind**

In `plugin/UI/MCPDockablePanel.xaml.cs`, add the PromptChip class at the bottom (after ChatMessage class):

```csharp
public class PromptChip
{
    public string Text { get; set; }
    public string Prompt { get; set; }

    public PromptChip(string text, string prompt = null)
    {
        Text = text;
        Prompt = prompt ?? text;
    }
}
```

Add a field and chips collection in MCPDockablePanel class:

```csharp
private readonly ObservableCollection<PromptChip> _chips = new ObservableCollection<PromptChip>();
private readonly DispatcherTimer _chipsTimer;
```

In the constructor, after the existing `_statusTimer` setup, add:

```csharp
PromptChips.ItemsSource = _chips;
_chipsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
_chipsTimer.Tick += (s, e) => UpdateChips();
```

In `Page_Loaded`, after `_statusTimer.Start()`, add:

```csharp
_chipsTimer.Start();
UpdateChips();
```

Add the `UpdateChips()` method:

```csharp
private void UpdateChips()
{
    try
    {
        if (!Core.SocketService.Instance.IsRunning)
        {
            _chips.Clear();
            ChipsBar.Visibility = Visibility.Collapsed;
            return;
        }

        var uiApp = Core.SocketService.Instance.UiApplication;
        if (uiApp?.ActiveUIDocument == null)
        {
            SetChips(new[] { new PromptChip("Open a project to get started") });
            return;
        }

        var doc = uiApp.ActiveUIDocument.Document;
        var activeView = doc.ActiveView;
        var selection = uiApp.ActiveUIDocument.Selection;
        int selectedCount = selection.GetElementIds().Count;

        var chips = new List<PromptChip>();

        // Selection-based chips (highest priority)
        if (selectedCount > 0)
        {
            chips.Add(new PromptChip($"Show parameters ({selectedCount} selected)", "Show me the parameters of the selected elements"));
            chips.Add(new PromptChip("Isolate in view", "Isolate the selected elements in the current view"));
            chips.Add(new PromptChip("Measure distance", "Measure the distance between the selected elements"));
        }

        // View-type chips
        if (activeView is Autodesk.Revit.DB.ViewPlan)
        {
            bool hasRooms = new Autodesk.Revit.DB.FilteredElementCollector(doc, activeView.Id)
                .OfCategory(Autodesk.Revit.DB.BuiltInCategory.OST_Rooms)
                .GetElementCount() > 0;

            if (hasRooms)
            {
                chips.Add(new PromptChip("Tag all rooms", "Tag all rooms in the current view"));
                chips.Add(new PromptChip("Color rooms by department", "Create a color legend for rooms by department"));
                chips.Add(new PromptChip("Export room data", "Export all room data"));
                chips.Add(new PromptChip("Create callouts for rooms", "Create callout views for all rooms on this level"));
            }
            else
            {
                chips.Add(new PromptChip("Check model health", "Check the health of this model"));
                chips.Add(new PromptChip("Show warnings", "Show me all model warnings"));
                chips.Add(new PromptChip("Export to Excel", "Export all elements in this view to Excel"));
            }
        }
        else if (activeView is Autodesk.Revit.DB.View3D)
        {
            chips.Add(new PromptChip("Check model health", "Check the health of this model"));
            chips.Add(new PromptChip("Detect clashes", "Check for clashes between structural elements and MEP"));
            chips.Add(new PromptChip("Section box from selection", "Create a section box around the selected elements"));
            chips.Add(new PromptChip("Audit families", "Audit all families in this project"));
        }
        else if (activeView is Autodesk.Revit.DB.ViewSheet)
        {
            chips.Add(new PromptChip("Align viewports", "Align all viewports on this sheet"));
            chips.Add(new PromptChip("Add revision", "Add a revision to this sheet"));
            chips.Add(new PromptChip("Export to PDF", "Export all sheets to PDF"));
            chips.Add(new PromptChip("Duplicate sheet", "Duplicate this sheet with all content"));
        }
        else if (activeView is Autodesk.Revit.DB.ViewSchedule)
        {
            chips.Add(new PromptChip("Export schedule to CSV", "Export this schedule to CSV"));
            chips.Add(new PromptChip("Export to Excel", "Export this schedule to Excel"));
        }
        else if (activeView is Autodesk.Revit.DB.ViewSection || activeView is Autodesk.Revit.DB.ViewDrafting)
        {
            chips.Add(new PromptChip("Add dimensions", "Create dimensions in this view"));
            chips.Add(new PromptChip("Add text note", "Add a text note in this view"));
            chips.Add(new PromptChip("Export to PDF", "Export all sheets to PDF"));
        }

        // Fallback if no view-specific chips were added
        if (chips.Count == 0 || (selectedCount == 0 && chips.Count < 3))
        {
            chips.Add(new PromptChip("Model statistics", "How many elements are in this model? Give me statistics by category"));
            chips.Add(new PromptChip("Check model health", "Check the health of this model"));
            chips.Add(new PromptChip("Export to Excel", "Export all elements to Excel"));
            chips.Add(new PromptChip("List warnings", "Show me all model warnings"));
        }

        // Limit to 6
        SetChips(chips.Take(6));
    }
    catch
    {
        // Never crash the panel due to chip updates
        _chips.Clear();
        ChipsBar.Visibility = Visibility.Collapsed;
    }
}

private void SetChips(IEnumerable<PromptChip> chips)
{
    _chips.Clear();
    foreach (var chip in chips)
        _chips.Add(chip);
    ChipsBar.Visibility = _chips.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
```

Add the chip click handler:

```csharp
private void Chip_Click(object sender, MouseButtonEventArgs e)
{
    if (sender is FrameworkElement fe && fe.DataContext is PromptChip chip && !_isProcessing)
    {
        ChatInput.Text = chip.Prompt;
        Send_Click(sender, e);
    }
}
```

- [ ] **Step 3: Verify UiApplication is accessible**

The `UpdateChips` method accesses `Core.SocketService.Instance.UiApplication`. Read `plugin/Core/SocketService.cs` to verify this property exists. If it doesn't, the property needs to be added — it should store the UIApplication from when the service is initialized. Check and add if missing:

```csharp
// In SocketService.cs, add property:
public UIApplication UiApplication { get; private set; }

// In the Initialize or Start method, store the reference:
UiApplication = uiApp; // wherever UIApplication is available
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25"`
Expected: 0 errors

- [ ] **Step 5: Commit**

```bash
git add plugin/UI/MCPDockablePanel.xaml plugin/UI/MCPDockablePanel.xaml.cs plugin/Core/SocketService.cs
git commit -m "feat: add contextual prompt chips to chat panel"
```

---

## Task 4: workflow_model_audit Tool

Comprehensive model audit in one call: health score + warnings + families + CAD + purgeable items.

**Files:**
- Create: `commandset/Commands/Workflow/WorkflowModelAuditCommand.cs`
- Create: `commandset/Services/Workflow/WorkflowModelAuditEventHandler.cs`
- Create: `server/src/tools/workflow_model_audit.ts`
- Modify: `command.json`

- [ ] **Step 1: Create EventHandler**

Create `commandset/Services/Workflow/WorkflowModelAuditEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowModelAuditEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public bool IncludeWarnings { get; set; } = true;
        public bool IncludeFamilies { get; set; } = true;
        public int MaxWarnings { get; set; } = 50;
        public object Result { get; private set; }

        public void SetParameters(bool includeWarnings, bool includeFamilies, int maxWarnings)
        {
            IncludeWarnings = includeWarnings;
            IncludeFamilies = includeFamilies;
            MaxWarnings = maxWarnings;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                var results = new Dictionary<string, object>();

                // 1. Health Score
                int score = 100;
                var deductions = new List<string>();

                // 2. Warnings
                var warnings = doc.GetWarnings();
                int warningCount = warnings.Count;
                results["warningCount"] = warningCount;

                if (warningCount > 50) { score -= 20; deductions.Add($"-20: {warningCount} warnings (>50)"); }
                else if (warningCount > 20) { score -= 10; deductions.Add($"-10: {warningCount} warnings (>20)"); }
                else if (warningCount > 0) { score -= 5; deductions.Add($"-5: {warningCount} warnings"); }

                if (IncludeWarnings)
                {
                    var topWarnings = warnings
                        .Take(MaxWarnings)
                        .Select(w => new
                        {
                            description = w.GetDescriptionText(),
#if REVIT2024_OR_GREATER
                            elementIds = w.GetFailingElements().Select(id => id.Value).ToList()
#else
                            elementIds = w.GetFailingElements().Select(id => id.IntegerValue).ToList()
#endif
                        }).ToList();
                    results["warnings"] = topWarnings;
                }

                // 3. In-place families
                var inPlaceFamilies = new FilteredElementCollector(doc)
                    .OfClass(typeof(FamilyInstance))
                    .Cast<FamilyInstance>()
                    .Where(fi => fi.Symbol?.Family?.IsInPlace == true)
                    .Select(fi => fi.Symbol.Family.Name)
                    .Distinct().ToList();
                results["inPlaceFamilyCount"] = inPlaceFamilies.Count;
                results["inPlaceFamilies"] = inPlaceFamilies;
                if (inPlaceFamilies.Count > 10) { score -= 15; deductions.Add($"-15: {inPlaceFamilies.Count} in-place families"); }
                else if (inPlaceFamilies.Count > 0) { score -= 5; deductions.Add($"-5: {inPlaceFamilies.Count} in-place families"); }

                // 4. CAD imports
                var cadImports = new FilteredElementCollector(doc)
                    .OfClass(typeof(ImportInstance))
                    .GetElementCount();
                results["cadImportCount"] = cadImports;
                if (cadImports > 5) { score -= 15; deductions.Add($"-15: {cadImports} CAD imports (>5)"); }
                else if (cadImports > 0) { score -= 5; deductions.Add($"-5: {cadImports} CAD imports"); }

                // 5. Unused families (count only)
                if (IncludeFamilies)
                {
                    var allFamilySymbols = new FilteredElementCollector(doc)
                        .OfClass(typeof(FamilySymbol))
                        .Cast<FamilySymbol>().ToList();

                    int unusedCount = 0;
                    foreach (var fs in allFamilySymbols)
                    {
                        var instances = new FilteredElementCollector(doc)
                            .OfClass(typeof(FamilyInstance))
                            .Cast<FamilyInstance>()
                            .Where(fi => fi.GetTypeId() == fs.Id);
                        if (!instances.Any()) unusedCount++;
                    }
                    results["unusedFamilyTypeCount"] = unusedCount;
                    results["totalFamilyTypeCount"] = allFamilySymbols.Count;
                    if (unusedCount > 50) { score -= 10; deductions.Add($"-10: {unusedCount} unused family types"); }
                }

                // 6. Unplaced rooms
                var unplacedRooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Autodesk.Revit.DB.Architecture.Room>()
                    .Where(r => r.Area == 0).Count();
                results["unplacedRoomCount"] = unplacedRooms;
                if (unplacedRooms > 0) { score -= 5; deductions.Add($"-5: {unplacedRooms} unplaced rooms"); }

                // Final score
                score = Math.Max(0, score);
                string grade = score >= 90 ? "A" : score >= 75 ? "B" : score >= 60 ? "C" : score >= 40 ? "D" : "F";

                results["healthScore"] = score;
                results["grade"] = grade;
                results["deductions"] = deductions;
                results["summary"] = $"Model health: {grade} ({score}/100). {warningCount} warnings, " +
                    $"{inPlaceFamilies.Count} in-place families, {cadImports} CAD imports, {unplacedRooms} unplaced rooms.";

                Result = new { success = true, data = results };
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

        public string GetName() => "Workflow Model Audit";
    }
}
```

- [ ] **Step 2: Create Command**

Create `commandset/Commands/Workflow/WorkflowModelAuditCommand.cs`:

```csharp
using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Workflow;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Workflow
{
    public class WorkflowModelAuditCommand : ExternalEventCommandBase
    {
        private WorkflowModelAuditEventHandler _handler => (WorkflowModelAuditEventHandler)Handler;
        public override string CommandName => "workflow_model_audit";

        public WorkflowModelAuditCommand(UIApplication uiApp)
            : base(new WorkflowModelAuditEventHandler(), uiApp) { }

        public override object Execute(JObject parameters, string requestId)
        {
            try
            {
                _handler.SetParameters(
                    includeWarnings: parameters?["includeWarnings"]?.Value<bool>() ?? true,
                    includeFamilies: parameters?["includeFamilies"]?.Value<bool>() ?? true,
                    maxWarnings: parameters?["maxWarnings"]?.Value<int>() ?? 50
                );
                if (RaiseAndWaitForCompletion(120000))
                    return _handler.Result;
                throw new TimeoutException("Workflow model audit timed out");
            }
            catch (Exception ex)
            {
                throw new Exception($"Workflow model audit failed: {ex.Message}");
            }
        }
    }
}
```

- [ ] **Step 3: Create TypeScript tool**

Create `server/src/tools/workflow_model_audit.ts`:

```typescript
import { errorMessage } from "../utils/errorUtils.js";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { addSuggestions, suggestIf } from "../utils/suggestions.js";

export function registerWorkflowModelAuditTool(server: McpServer) {
  server.tool(
    "workflow_model_audit",
    `Complete model health audit in one call. Checks: health score (A-F), warnings, in-place families, CAD imports, unused family types, unplaced rooms. Returns a comprehensive report with actionable recommendations.

GUIDANCE:
- "Audit this model completely": call with defaults
- "Quick audit without family check": includeFamilies=false
- Use this instead of calling check_model_health + get_warnings + audit_families separately`,
    {
      includeWarnings: z.boolean().optional().default(true)
        .describe("Include warning details in the report."),
      includeFamilies: z.boolean().optional().default(true)
        .describe("Include unused family type detection (slower on large models)."),
      maxWarnings: z.number().optional().default(50)
        .describe("Maximum warnings to include in detail."),
    },
    async (args) => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("workflow_model_audit", {
            includeWarnings: args.includeWarnings ?? true,
            includeFamilies: args.includeFamilies ?? true,
            maxWarnings: args.maxWarnings ?? 50,
          });
        });

        const data = typeof response === 'object' ? (response as any)?.data ?? response : response;
        const enriched = addSuggestions(response, [
          suggestIf((data?.unusedFamilyTypeCount ?? 0) > 0, "Purge unused families and types", "Clean up detected unused items"),
          suggestIf((data?.cadImportCount ?? 0) > 0, "Clean up CAD imports", "Remove unnecessary CAD files"),
          suggestIf((data?.healthScore ?? 100) < 70, "Export model warnings to review offline", "Score below 70 needs detailed attention"),
        ]);

        return { content: [{ type: "text" as const, text: JSON.stringify(enriched, null, 2) }] };
      } catch (error) {
        return { content: [{ type: "text" as const, text: `Workflow model audit failed: ${errorMessage(error)}` }], isError: true };
      }
    }
  );
}
```

- [ ] **Step 4: Register in command.json**

```json
{
  "commandName": "workflow_model_audit",
  "description": "Complete model health audit: score, warnings, families, CAD, purge candidates",
  "assemblyPath": "RevitMCPCommandSet/{VERSION}/RevitMCPCommandSet.dll"
}
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`
Expected: 0 errors

- [ ] **Step 6: Commit**

```bash
git add commandset/Commands/Workflow/ commandset/Services/Workflow/ \
        server/src/tools/workflow_model_audit.ts command.json
git commit -m "feat: add workflow_model_audit tool — comprehensive model audit in one call"
```

---

## Task 5: workflow_data_roundtrip Tool

Export to Excel with clear instructions for the roundtrip workflow.

**Files:**
- Create: `commandset/Commands/Workflow/WorkflowDataRoundtripCommand.cs`
- Create: `commandset/Services/Workflow/WorkflowDataRoundtripEventHandler.cs`
- Create: `server/src/tools/workflow_data_roundtrip.ts`
- Modify: `command.json`

- [ ] **Step 1: Create EventHandler**

Create `commandset/Services/Workflow/WorkflowDataRoundtripEventHandler.cs`. This is a thin wrapper around export_to_excel logic. Read the existing `ExportToExcelEventHandler.cs` and replicate the export logic, adding `instructions` to the result:

The handler should:
1. Accept categories, parameterNames, includeTypeParameters (same as export_to_excel)
2. Run the same ClosedXML export logic
3. Return: filePath, elementCount, parameterCount, plus `instructions` string

Since this duplicates export_to_excel, the simplest approach is to have the handler do the same ClosedXML work. Copy the core logic from `ExportToExcelEventHandler.Execute()` but add an instructions field to the result. The event handler should be self-contained.

- [ ] **Step 2: Create Command**

Standard pattern — `CommandName => "workflow_data_roundtrip"`, timeout 120s.

- [ ] **Step 3: Create TypeScript tool**

Create `server/src/tools/workflow_data_roundtrip.ts` with suggestion: "When done editing, ask me to import the file back".

- [ ] **Step 4: Register, build, commit**

```bash
git commit -m "feat: add workflow_data_roundtrip tool — Excel export with roundtrip instructions"
```

---

## Task 6: workflow_clash_review Tool

Find clashes, isolate elements, create section box — all in one call.

**Files:**
- Create: `commandset/Commands/Workflow/WorkflowClashReviewCommand.cs`
- Create: `commandset/Services/Workflow/WorkflowClashReviewEventHandler.cs`
- Create: `server/src/tools/workflow_clash_review.ts`
- Modify: `command.json`

- [ ] **Step 1: Create EventHandler**

Create `commandset/Services/Workflow/WorkflowClashReviewEventHandler.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Workflow
{
    public class WorkflowClashReviewEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        public string CategoryA { get; set; } = "";
        public string CategoryB { get; set; } = "";
        public double Tolerance { get; set; } = 0;
        public bool CreateSectionBox { get; set; } = true;
        public object Result { get; private set; }

        public void SetParameters(string categoryA, string categoryB, double tolerance, bool createSectionBox)
        {
            CategoryA = categoryA;
            CategoryB = categoryB;
            Tolerance = tolerance;
            CreateSectionBox = createSectionBox;
            _resetEvent.Reset();
        }

        public bool WaitForCompletion(int timeoutMilliseconds = 120000)
        {
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                double tolFt = Tolerance / 304.8;

                // Find categories
                var catA = FindCategory(doc, CategoryA);
                var catB = FindCategory(doc, CategoryB);
                if (catA == BuiltInCategory.INVALID || catB == BuiltInCategory.INVALID)
                {
                    Result = new { success = false, error = $"Category not found: {(catA == BuiltInCategory.INVALID ? CategoryA : CategoryB)}" };
                    return;
                }

                // Collect elements
                var elementsA = new FilteredElementCollector(doc)
                    .OfCategory(catA).WhereElementIsNotElementType().ToList();
                var elementsB = new FilteredElementCollector(doc)
                    .OfCategory(catB).WhereElementIsNotElementType().ToList();

                // Detect intersections via bounding box
                var clashes = new List<object>();
                var clashingIds = new HashSet<ElementId>();

                foreach (var a in elementsA)
                {
                    var bbA = a.get_BoundingBox(null);
                    if (bbA == null) continue;

                    foreach (var b in elementsB)
                    {
                        var bbB = b.get_BoundingBox(null);
                        if (bbB == null) continue;

                        if (BoundingBoxesIntersect(bbA, bbB, tolFt))
                        {
                            clashes.Add(new
                            {
#if REVIT2024_OR_GREATER
                                elementA = a.Id.Value,
                                elementB = b.Id.Value,
#else
                                elementA = a.Id.IntegerValue,
                                elementB = b.Id.IntegerValue,
#endif
                                nameA = a.Name,
                                nameB = b.Name
                            });
                            clashingIds.Add(a.Id);
                            clashingIds.Add(b.Id);
                        }
                    }
                }

                // Isolate clashing elements in active view
                var activeView = doc.ActiveView;
                long? sectionBoxViewId = null;

                using (var tx = new Transaction(doc, "Workflow Clash Review"))
                {
                    tx.Start();

                    if (clashingIds.Count > 0)
                    {
                        // Isolate
                        try { activeView.IsolateElementsTemporary(clashingIds.ToList()); } catch { }

                        // Section box on 3D view
                        if (CreateSectionBox && activeView is View3D view3D)
                        {
                            var allBB = clashingIds
                                .Select(id => doc.GetElement(id)?.get_BoundingBox(null))
                                .Where(bb => bb != null).ToList();

                            if (allBB.Count > 0)
                            {
                                double minX = allBB.Min(b => b.Min.X) - 3;
                                double minY = allBB.Min(b => b.Min.Y) - 3;
                                double minZ = allBB.Min(b => b.Min.Z) - 3;
                                double maxX = allBB.Max(b => b.Max.X) + 3;
                                double maxY = allBB.Max(b => b.Max.Y) + 3;
                                double maxZ = allBB.Max(b => b.Max.Z) + 3;

                                var sectionBox = new BoundingBoxXYZ
                                {
                                    Min = new XYZ(minX, minY, minZ),
                                    Max = new XYZ(maxX, maxY, maxZ)
                                };
                                view3D.SetSectionBox(sectionBox);
#if REVIT2024_OR_GREATER
                                sectionBoxViewId = view3D.Id.Value;
#else
                                sectionBoxViewId = view3D.Id.IntegerValue;
#endif
                            }
                        }
                    }
                    tx.Commit();
                }

                Result = new
                {
                    success = true,
                    clashCount = clashes.Count,
                    clashes = clashes.Take(100).ToList(),
                    isolatedElementCount = clashingIds.Count,
                    sectionBoxViewId,
                    summary = $"Found {clashes.Count} clashes between {CategoryA} and {CategoryB}. " +
                        $"{clashingIds.Count} elements isolated in view."
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

        private bool BoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b, double tolerance)
        {
            return a.Min.X - tolerance <= b.Max.X && a.Max.X + tolerance >= b.Min.X
                && a.Min.Y - tolerance <= b.Max.Y && a.Max.Y + tolerance >= b.Min.Y
                && a.Min.Z - tolerance <= b.Max.Z && a.Max.Z + tolerance >= b.Min.Z;
        }

        private BuiltInCategory FindCategory(Document doc, string name)
        {
            foreach (Category cat in doc.Settings.Categories)
            {
                if (cat.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (BuiltInCategory)cat.Id.IntegerValue;
            }
            return BuiltInCategory.INVALID;
        }

        public string GetName() => "Workflow Clash Review";
    }
}
```

- [ ] **Step 2: Create Command**

Standard pattern — `CommandName => "workflow_clash_review"`, timeout 120s, parse categoryA, categoryB, tolerance, createSectionBox.

- [ ] **Step 3: Create TypeScript tool**

`server/src/tools/workflow_clash_review.ts` — Zod schema with categoryA (required string), categoryB (required string), tolerance (optional number default 0), createSectionBox (optional bool default true). Add next-step suggestions for "Export clashing elements to Excel".

- [ ] **Step 4: Register in command.json, build, verify, commit**

```bash
git commit -m "feat: add workflow_clash_review tool — detect, isolate, section box in one call"
```

---

## Task 7: workflow_room_documentation + workflow_sheet_set Tools

The remaining 2 workflows. Each follows the same pattern as Tasks 4-6.

**Files:**
- Create: `commandset/Commands/Workflow/WorkflowRoomDocumentationCommand.cs`
- Create: `commandset/Services/Workflow/WorkflowRoomDocumentationEventHandler.cs`
- Create: `server/src/tools/workflow_room_documentation.ts`
- Create: `commandset/Commands/Workflow/WorkflowSheetSetCommand.cs`
- Create: `commandset/Services/Workflow/WorkflowSheetSetEventHandler.cs`
- Create: `server/src/tools/workflow_sheet_set.ts`
- Modify: `command.json`

- [ ] **Step 1: workflow_room_documentation EventHandler**

The handler should:
1. Export room data (FilteredElementCollector for rooms, collect Number, Name, Area, Volume, Level, Department)
2. Create section views from rooms (ViewSection.CreateSection for each room's bounding box, on the room's floor plan)
3. Tag all rooms in the active view (using IndependentTag.Create on each room in the active view)
4. Return: roomCount, viewsCreated, tagsPlaced, rooms data array

Parameters: levelName (optional filter), createSections (bool, default true), colorByParameter (string, default "Department")

- [ ] **Step 2: workflow_room_documentation Command + TypeScript tool**

Standard patterns. TypeScript adds suggestions: "Create a schedule for these rooms", "Export room data to Excel".

- [ ] **Step 3: workflow_sheet_set EventHandler**

The handler should:
1. Create sheets using ViewSheet.Create(doc, titleBlockId) for each sheet definition
2. If viewPlacements provided, place viewports using Viewport.Create(doc, sheetId, viewId, center)
3. If alignToSheet provided, read reference viewport position and apply to all other sheets

Parameters: sheets (array of {number, name}), titleBlockName (string), viewPlacements (optional), alignToSheet (optional string)

- [ ] **Step 4: workflow_sheet_set Command + TypeScript tool**

Standard patterns. TypeScript adds suggestions: "Export all sheets to PDF", "Add revision to sheets".

- [ ] **Step 5: Register both in command.json, build, verify**

Run: `dotnet build mcp-servers-for-revit.sln -c "Release R25" && cd server && npx tsc --noEmit`

- [ ] **Step 6: Commit**

```bash
git commit -m "feat: add workflow_room_documentation and workflow_sheet_set tools"
```

---

## Summary

| Task | Feature | Files |
|------|---------|-------|
| 1 | Next-step suggestions (batch 1) | 1 new + 4 modified TS |
| 2 | Next-step suggestions (batch 2) | 4 modified TS |
| 3 | Contextual prompt chips | 2 modified C# (XAML + code-behind) |
| 4 | workflow_model_audit | 2 new C# + 1 new TS |
| 5 | workflow_data_roundtrip | 2 new C# + 1 new TS |
| 6 | workflow_clash_review | 2 new C# + 1 new TS |
| 7 | workflow_room_documentation + workflow_sheet_set | 4 new C# + 2 new TS |

**Total: ~16 new files, ~10 modified files. 7 commits.**
