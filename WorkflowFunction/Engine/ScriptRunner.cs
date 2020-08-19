using Core.Objects;
using Core.Objects.Dtos.Workflow;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading.Tasks;

namespace Workflow
{
    internal class ScriptRunner : IScriptRunner
    {
        internal static Assembly[] references;

        public ScriptRunner(LogEvent log)
        {
            try
            {
                if (references == null)
                {
                    var loadCtx = AssemblyLoadContext.GetLoadContext(typeof(Execute).Assembly);

                    // some of the stack might not have been loaded, lets make sure we load everything so we can give a complete response
                    // first grab what's loaded
                    var loadedAlready = loadCtx.Assemblies
                            .Where(a => !a.IsDynamic)
                            .ToList();

                    // then grab the bin directory
                    var thisAssembly = typeof(Execute).Assembly;
                    var binDir = thisAssembly.Location
                        .Replace(thisAssembly.ManifestModule.Name, "");
                    log(WorkflowLogLevel.Debug, $"Bin Directory: {binDir}");

                    // from the bin, grab our core dll files
                    var stackDlls = Directory.GetFiles(binDir)
                        //.Select(i => i.ToLowerInvariant())
                        .Where(f => f.EndsWith("dll"))
                        .ToList();

                    loadedAlready.ForEach(a => log(WorkflowLogLevel.Info, $"Loaded: {a.FullName} "));

                    // load the missing ones
                    var toLoad = stackDlls
                        .Where(assemblyPath => loadedAlready.All(a => a.CodeBase.ToLowerInvariant() != assemblyPath.ToLowerInvariant()))
                        .Where(a => !a.Contains("api-ms-win"))
                        .ToArray();
                    foreach (var assemblyPath in toLoad)
                    {
                        try 
                        {
                            var a = loadCtx.LoadFromAssemblyPath(assemblyPath);
                            loadedAlready.Add(a);
                            log(WorkflowLogLevel.Info, $"Loaded: {a.FullName} ");
                        } 
                        catch (Exception ex) 
                        {
                            log(WorkflowLogLevel.Warning, $"Unable to load assembly {assemblyPath} because: " + ex.Message); 
                        }
                    }

                    references = loadedAlready.ToArray();
                }
            }
            catch (Exception ex)
            {
                log(WorkflowLogLevel.Warning, "Script Runner may not have everything it needs, continuing anyway despite exception:\n" + ex.Message);
            }
        }

        public async Task<T> BuildScript<T>(string code, string[] imports, Action<WorkflowLogLevel, string> log)
        {
            try
            {
                var referencesNeeded = references.Where(r => r.GetExportedTypes().Any(t => imports.Contains(t.Namespace)));
                var options = ScriptOptions.Default
                    .AddReferences(referencesNeeded)
                    .WithImports(imports);

                if (log != null)
                {
                    var message = $"\nImports\n  {string.Join("\n  ", imports)}\n\nReferences Needed\n  {string.Join("\n  ", referencesNeeded.Select(r => r.FullName))}";
                    log(WorkflowLogLevel.Debug, message);
                }

                return await CSharpScript.EvaluateAsync<T>(code, options);
            }
            catch (Exception ex)
            {
                log(WorkflowLogLevel.Error, "Script failed to compile.");
                log(WorkflowLogLevel.Error, ex.Message);

                if (ex is CompilationErrorException cEx)
                    log(WorkflowLogLevel.Error, $"Source of the problem:\n{cEx.Source}");

                return default;
            }
        }

        public async Task<T> Run<T>(string code, string[] imports, object args, Action<WorkflowLogLevel, string> log)
        {
            try
            {
                var referencesNeeded = references.Where(r => r.GetExportedTypes().Any(t => imports.Contains(t.Namespace)));
                var options = ScriptOptions.Default
                    .AddReferences(referencesNeeded)
                    .WithImports(imports);

                if (log != null)
                {
                    var message = $"\nImports\n  {string.Join("\n  ", imports)}\n\nReferences Needed\n  {string.Join("\n  ", referencesNeeded.Select(r => r.FullName))}";
                    log(WorkflowLogLevel.Debug, message);
                }

                return await CSharpScript.EvaluateAsync<T>(code, options, args, args.GetType());
            }
            catch (NullReferenceException ex)
            {
                var typeAndCall = $"(({ex.TargetSite.DeclaringType.Name})object).{ex.TargetSite.Name}";
                var data = new List<string>();

                foreach (var k in ex.Data.Keys) data.Add($"{k}: {ex.Data[k]}");

                throw new Exception(ex.Message + $"\nContext: {ex.Source}\nTarget: {typeAndCall}\n{string.Join("\n", data)}");
            }
            catch (CompilationErrorException ex)
            {
                throw new Exception($"Compilation failed:\n{ex.Message}\n{string.Join(Environment.NewLine, ex.Diagnostics)}");
            }
        }

        public Task Run(string code, string[] imports, object args, Action<WorkflowLogLevel, string> log)
            => Run<bool>(code + ";return true;", imports, args, log);
    }
}