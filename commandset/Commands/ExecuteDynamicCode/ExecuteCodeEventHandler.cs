using System.IO;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Newtonsoft.Json;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Commands.ExecuteDynamicCode
{
    /// <summary>
    /// External event handler for code execution
    /// </summary>
    public class ExecuteCodeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public const string TransactionModeAuto = "auto";
        public const string TransactionModeNone = "none";

        // Code execution parameters
        private string _generatedCode;
        private object[] _executionParameters;
        private string _transactionMode = TransactionModeAuto;

        // Execution result information
        public ExecutionResultInfo ResultInfo { get; private set; }

        // State synchronization object
        public bool TaskCompleted { get; private set; }
        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Set the code to execute and its parameters
        public void SetExecutionParameters(string code, object[] parameters = null, string transactionMode = TransactionModeAuto)
        {
            _generatedCode = code;
            _executionParameters = parameters ?? Array.Empty<object>();
            _transactionMode = transactionMode == TransactionModeNone ? TransactionModeNone : TransactionModeAuto;
            TaskCompleted = false;
            _resetEvent.Reset();
        }

        // Wait for execution to complete - IWaitableExternalEventHandler interface implementation
        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            try
            {
                var doc = app.ActiveUIDocument.Document;
                ResultInfo = new ExecutionResultInfo();

                // Advisory conflict preflight: another add-in may have preloaded
                // NEWER copies of Roslyn's dependency chain (e.g. Ideate BIMLink
                // shipping System.Collections.Immutable 9.x) before our toggle.
                // Side-by-side loads normally succeed, so this is reported as a
                // warning rather than a hard refusal; hard failures are instead
                // classified in the catch below. See AGENTS.md (Roslyn conflicts).
                ResultInfo.Warnings = GetConflictWarning();

                object result;
                if (_transactionMode == TransactionModeNone)
                {
                    result = CompileAndExecuteCode(
                        code: _generatedCode,
                        doc: doc,
                        parameters: _executionParameters
                    );
                }
                else
                {
                    using (var transaction = new Transaction(doc, "Execute AI Code"))
                    {
                        transaction.Start();

                        result = CompileAndExecuteCode(
                            code: _generatedCode,
                            doc: doc,
                            parameters: _executionParameters
                        );

                        transaction.Commit();
                    }
                }

                ResultInfo.Success = true;
                ResultInfo.Result = JsonConvert.SerializeObject(result);
            }
            catch (Exception ex)
            {
                ResultInfo.Success = false;
                // Bind-type failures (FileLoad/MissingMethod/TypeLoad/BadImageFormat)
                // mean the Roslyn chain collided with a preloaded copy - surface an
                // explicit do-not-retry signal so agents stop hammering the tool.
                var conflictMsg = ClassifyCompileFailure(ex);
                ResultInfo.ErrorMessage = conflictMsg ?? $"Execution failed: {ex.Message}";
            }
            finally
            {
                TaskCompleted = true;
                _resetEvent.Set();
            }
        }

        private object CompileAndExecuteCode(string code, Document doc, object[] parameters)
        {
            // Wrap the code to standardize the entry point
            var wrappedCode = $@"
using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace AIGeneratedCode
{{
    public static class CodeExecutor
    {{
        public static object Execute(Document document, object[] parameters)
        {{
            // user code entrypoint
            {code}
        }}
    }}
}}";

            var syntaxTree = CSharpSyntaxTree.ParseText(wrappedCode);

            // Add required assembly references (reference all loaded assemblies)
            var references = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => MetadataReference.CreateFromFile(a.Location))
                .Cast<MetadataReference>()
                .ToList();

            // Compile the code
            var compilation = CSharpCompilation.Create(
                "AIGeneratedCode",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using (var ms = new MemoryStream())
            {
                var result = compilation.Emit(ms);

                // Process the compilation result
                if (!result.Success)
                {
                    // Number of wrapper header lines preceding the first user-code line
                    // (usings + namespace/class/Execute-sig braces + entry comment).
                    const int wrapperHeaderLineCount = 13;

                    var errors = string.Join("\n", result.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d =>
                        {
                            int wrappedLine = d.Location.GetLineSpan().StartLinePosition.Line;
                            int snippetLine = wrappedLine - wrapperHeaderLineCount + 1;
                            string loc = snippetLine >= 1
                                ? $"Line {snippetLine}"
                                : $"Wrapper line {wrappedLine + 1}";
                            return $"{loc}: {d.GetMessage()}";
                        }));
                    throw new Exception($"Code compilation error (line numbers refer to your submitted code, 1-based):\n{errors}");
                }

                // Invoke the execute method via reflection
                ms.Seek(0, SeekOrigin.Begin);
                var assembly = Assembly.Load(ms.ToArray());
                var executorType = assembly.GetType("AIGeneratedCode.CodeExecutor");
                var executeMethod = executorType.GetMethod("Execute");

                return executeMethod.Invoke(null, new object[] { doc, parameters });
            }
        }

        // Roslyn dependency chain shipped beside this assembly. Only these are
        // scanned for version conflicts; unrelated add-in assemblies are not.
        private static readonly string[] RoslynDepNames = new[]
        {
            "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp",
            "System.Collections.Immutable", "System.Reflection.Metadata",
            "System.Memory", "System.Runtime.CompilerServices.Unsafe",
            "System.Threading.Tasks.Extensions", "System.Text.Encoding.CodePages"
        };

        private static string _conflictWarning;
        private static bool _conflictChecked;

        /// <summary>
        /// Detects higher-version preloaded copies of our Roslyn dependency
        /// chain (computed once per session). A preloaded copy LOWER than ours
        /// is the proven-benign case (Revit 2024 ships System.Memory 4.0.1.1;
        /// our request for the higher bundled version side-by-side loads) and
        /// is not flagged; a HIGHER preloaded copy is the conflict shape seen
        /// in the Revit 2024.3.6 crash-session journal.
        /// </summary>
        private static string GetConflictWarning()
        {
            if (_conflictChecked) return _conflictWarning;
            _conflictChecked = true;
            try
            {
                string baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(baseDir)) return null;

                var loaded = AppDomain.CurrentDomain.GetAssemblies();
                var conflicts = new List<string>();
                foreach (var name in RoslynDepNames)
                {
                    string localPath = Path.Combine(baseDir, name + ".dll");
                    if (!File.Exists(localPath)) continue;
                    Version bundled;
                    try { bundled = AssemblyName.GetAssemblyName(localPath).Version; }
                    catch { continue; }

                    foreach (var asm in loaded)
                    {
                        if (asm.IsDynamic) continue;
                        AssemblyName an;
                        try { an = asm.GetName(); } catch { continue; }
                        if (!string.Equals(an.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (an.Version != null && an.Version > bundled)
                            conflicts.Add(string.Format("{0}: bundled {1} vs preloaded {2} ({3})",
                                name, bundled, an.Version, SafeFileName(asm)));
                    }
                }

                if (conflicts.Count > 0)
                {
                    _conflictWarning =
                        "Assembly version conflicts in the dynamic-code compile chain " +
                        "(newer copies preloaded by another add-in): " + string.Join("; ", conflicts) +
                        ". Compilation still runs (side-by-side load); report this to the plugin maintainer if execution fails.";
                    try
                    {
                        File.AppendAllText(
                            Path.Combine(Path.GetTempPath(), "mcp_compile_conflicts.log"),
                            string.Format("[{0:HH:mm:ss}] {1}\n", DateTime.Now, _conflictWarning));
                    }
                    catch { }
                }
            }
            catch { }
            return _conflictWarning;
        }

        private static string SafeFileName(Assembly asm)
        {
            try { return Path.GetFileName(asm.Location); } catch { return "?"; }
        }

        /// <summary>
        /// Walks the exception chain looking for binder-class failures that
        /// indicate the Roslyn chain collided with a preloaded assembly.
        /// </summary>
        private static string ClassifyCompileFailure(Exception ex)
        {
            for (var e = ex; e != null; e = e.InnerException)
            {
                if (e is FileLoadException || e is MissingMethodException ||
                    e is TypeLoadException || e is BadImageFormatException)
                {
                    return string.Format(
                        "Compile subsystem unavailable - assembly conflict detected ({0}: {1}). " +
                        "Do NOT retry send_code_to_revit in this session; restart Revit " +
                        "(without the conflicting add-in if possible) and report this failure.",
                        e.GetType().Name, e.Message);
                }
            }
            return null;
        }

        public string GetName()
        {
            return "Execute AI Code";
        }
    }

    // Execution result data structure
    public class ExecutionResultInfo
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("result")]
        public string Result { get; set; }

        [JsonProperty("errorMessage")]
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>Advisory assembly-conflict warning (null = none; omitted
        /// from the serialized envelope when null).</summary>
        [JsonProperty("warnings")]
        public string Warnings { get; set; }
    }
}
