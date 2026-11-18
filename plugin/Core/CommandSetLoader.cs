using RevitMCPSDK.API.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace revit_mcp_plugin.Core
{
    /// <summary>
    /// Live-reload capable command-assembly loader. Loads command set DLLs
    /// from BYTES (not from the file path) so the file on disk is never
    /// locked by Revit: the copy step of a new build can overwrite the DLL
    /// while Revit runs. The cache keys each assembly path by SHA-256 of its
    /// contents — Resolve returns the cached Assembly when the file hash is
    /// unchanged, and loads the new bytes into a fresh assembly object when
    /// the file changed. The stale loaded assembly stays resident until GC
    /// (acceptable for a dev workflow); the registry keeps using the newest
    /// instances because RegisterCommand overwrites by CommandName.
    /// </summary>
    public static class CommandSetLoader
    {
        private static readonly Dictionary<string, Tuple<Assembly, string>> Cache =
            new Dictionary<string, Tuple<Assembly, string>>(StringComparer.OrdinalIgnoreCase);

        private static bool _resolveHookInstalled;

        /// <summary>
        /// Installs an AppDomain.AssemblyResolve hook (once) that probes all
        /// directories containing known command assemblies + their sibling folders.
        /// Required because byte-loaded assemblies have no CodeBase for the
        /// default loader to probe dependencies (e.g. Microsoft.CodeAnalysis).
        /// Each probe dir may also carry a "legacy\" subfolder holding
        /// exact-identity copies of dependencies requested with older assembly
        /// versions (e.g. System.Runtime.CompilerServices.Unsafe 4.0.4.1
        /// alongside the folder's 6.0.0.0 copy). The legacy folder is
        /// intentionally NOT staged by the build/installer - it is hand-placed
        /// in a deployed plugin folder only when a concrete binding needs it;
        /// the hook simply honors it when present.
        /// </summary>
        public static void EnsureResolveHook(params string[] probeDirectories)
        {
            if (_resolveHookInstalled) return;
            _resolveHookInstalled = true;
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                // Debug trace: append every resolver invocation to a log.
                try
                {
                    string reqName = "?";
                    string reqLoc = "?";
                    try { if (args.RequestingAssembly != null) reqName = args.RequestingAssembly.GetName().Name; } catch { }
                    try { reqLoc = (args.RequestingAssembly != null && !args.RequestingAssembly.IsDynamic) ? args.RequestingAssembly.Location : "(byte/codebase)"; } catch { }
                    File.AppendAllText(
                        Path.Combine(Path.GetTempPath(), "mcp_resolver_debug.log"),
                        string.Format("[{0:HH:mm:ss.fff}] REQ \"{1}\" full=\"{2}\" asking={3} @ {4}\n",
                            DateTime.Now, new AssemblyName(args.Name).Name, args.Name, reqName, reqLoc));
                }
                catch { }

                var requested = new AssemblyName(args.Name);
                var name = requested.Name;
                if (string.IsNullOrEmpty(name)) return null;

                // Strip the RETARGETED/redirected version for logging clarity —
                // then look for a candidate whose assembly identity matches the
                // requested version when possible. A version-mismatched
                // assembly returned from AssemblyResolve is NOT reliably
                // discarded by the binder (the CLR frequently accepts it as
                //-is), so a name-match fallback for a VERSIONED request would
                // poison other add-ins' late resolutions with our older
                // copies - e.g. substituting our System.Memory 4.0.1.2 for
                // another add-in's requested 4.0.5.0. Name-match is therefore
                // allowed only for simple-name requests (no version).
                // See AGENTS.md (Roslyn conflicts / resolver hook).
                string exactMatch = null;
                string nameMatch = null;
                bool allowNameMatch = requested.Version == null;
                foreach (var dir in unionOfProbeDirs(probeDirectories))
                {
                    // Main folder first: candidate file may satisfy exact or name match.
                    // Then dir\legacy\ — legacy copies exist ONLY to satisfy exact
                    // version identities of dependencies (e.g. netfx SpanHelpers
                    // binding System.Runtime.CompilerServices.Unsafe 4.0.4.1 while
                    // the folder's main copy serves the modern 6.0.0.0 identity).
                    // A legacy file is never returned as a mere name-match because
                    // that would bind an old identity over a newer request.
                    string legacyDir = Path.Combine(dir, "legacy");
                    string[] candidates =
                    {
                        Path.Combine(dir, name + ".dll"),
                        Path.Combine(legacyDir, name + ".dll")
                    };
                    foreach (string candidate in candidates)
                    {
                        if (!File.Exists(candidate)) continue;
                        try
                        {
                            var identity = System.Reflection.AssemblyName.GetAssemblyName(candidate);
                            if (requested.Version != null && identity.Version == requested.Version)
                            {
                                exactMatch = candidate;
                                break;
                            }
                            if (allowNameMatch && nameMatch == null && !candidate.Contains("\\legacy\\"))
                            {
                                nameMatch = candidate;
                            }
                        }
                        catch { }
                    }
                    if (exactMatch != null) break;
                }

                string chosen = exactMatch ?? nameMatch;
                try
                {
                    File.AppendAllText(
                        Path.Combine(Path.GetTempPath(), "mcp_resolver_debug.log"),
                        string.Format("[{0:HH:mm:ss.fff}]   chose: {1}\n", DateTime.Now, chosen ?? "(none)"));
                }
                catch { }
                if (chosen == null) return null;
                try
                {
                    return Assembly.Load(File.ReadAllBytes(chosen));
                }
                catch
                {
                    return null;
                }
            };
        }

        private static IEnumerable<string> unionOfProbeDirs(string[] configured)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in configured)
            {
                if (d != null && seen.Add(d)) yield return d;
            }
            foreach (var key in Cache.Keys)
            {
                string dir = Path.GetDirectoryName(key);
                if (dir != null && seen.Add(dir)) yield return dir;
            }
        }

        public static Assembly Resolve(string assemblyPath, ILogger logger)
        {
            string hash = ComputeHash(assemblyPath);
            Tuple<Assembly, string> cached;
            if (Cache.TryGetValue(assemblyPath, out cached) && cached.Item2 == hash)
            {
                return cached.Item1;
            }

            var bytes = File.ReadAllBytes(assemblyPath);
            var assembly = Assembly.Load(bytes);
            Cache[assemblyPath] = Tuple.Create(assembly, hash);
            logger?.Info("Assembly byte-loaded (hot reload): {0} [{1}]",
                Path.GetFileName(assemblyPath), hash.Substring(0, 8));
            return assembly;
        }

        /// <summary>Expels the cached assembly for this path (forces reload next time even if bytes unchanged).</summary>
        public static void Invalidate(string assemblyPath)
        {
            if (assemblyPath != null) Cache.Remove(assemblyPath);
        }

        public static string ComputeHash(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var sb2 = new System.Text.StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb2.Append(b.ToString("x2"));
                return sb2.ToString();
            }
        }
    }
}
