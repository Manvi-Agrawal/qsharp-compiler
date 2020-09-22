﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Quantum.QsCompiler.CompilationBuilder;
using Microsoft.Quantum.QsCompiler.Diagnostics;
using Microsoft.VisualStudio.LanguageServer.Protocol;

namespace Microsoft.Quantum.QsCompiler
{
    internal class AssemblyRewriteStepsLoader : AbstractRewriteStepsLoader
    {
        public override ImmutableArray<LoadedStep> GetLoadedSteps(CompilationLoader.Configuration config, Action<Diagnostic> onDiagnostic = null, Action<Exception> onException = null)
        {
            if (config.RewriteSteps == null)
            {
                return ImmutableArray<LoadedStep>.Empty;
            }

            static Assembly LoadAssembly(string path) => CompilationLoader.LoadAssembly?.Invoke(path) ?? Assembly.LoadFrom(path);
            Uri WithFullPath(string file)
            {
                try
                {
                    return string.IsNullOrWhiteSpace(file) ? null : new Uri(Path.GetFullPath(file));
                }
                catch (Exception ex)
                {
                    onDiagnostic?.Invoke(Errors.LoadError(ErrorCode.InvalidFilePath, new[] { file }, file));
                    onException?.Invoke(ex);
                    return null;
                }
            }

            var specifiedPluginDlls = config.RewriteSteps.Select(step => (WithFullPath(step.Item1), step.Item2)).Where(step => step.Item1 != null).ToList();
            var (foundDlls, notFoundDlls) = specifiedPluginDlls.Partition(step => File.Exists(step.Item1.LocalPath));
            foreach (var file in notFoundDlls.Select(step => step.Item1).Distinct())
            {
                onDiagnostic?.Invoke(Errors.LoadError(ErrorCode.UnknownCompilerPlugin, new[] { file.LocalPath }, file.LocalPath));
            }

            var rewriteSteps = ImmutableArray.CreateBuilder<LoadedStep>();
            foreach (var (target, outputFolder) in foundDlls)
            {
                var relevantTypes = new List<Type>();

                try
                {
                    var typesInAssembly = LoadAssembly(target.LocalPath).GetTypes();
                    var exactInterfaceMatches = typesInAssembly.Where(t => typeof(IRewriteStep).IsAssignableFrom(t)); // inherited interface is defined in this exact dll
                    if (exactInterfaceMatches.Any())
                    {
                        relevantTypes.AddRange(exactInterfaceMatches);
                    }
                    else
                    {
                        // If the inherited interface is defined in older compiler version, then we can attempt to load the step anyway via reflection.
                        // However, in this case we have to load the corresponding assembly into the current context, which can have its own issues.
                        // We hence first check if this may be the case, and if so we proceed to attempt the loading via reflection.
                        static bool IsPossibleMatch(Type t) => t.GetInterfaces().Any(t => t.FullName == typeof(IRewriteStep).FullName);
                        var possibleInterfaceMatches = typesInAssembly.Where(IsPossibleMatch);
                        if (possibleInterfaceMatches.Any())
                        {
                            var reloadedTypes = Assembly.LoadFrom(target.LocalPath).GetTypes();
                            relevantTypes.AddRange(reloadedTypes.Where(IsPossibleMatch));
                        }
                    }
                }
                catch (BadImageFormatException ex)
                {
                    onDiagnostic?.Invoke(this.LoadError(target, ErrorCode.FileIsNotAnAssembly, target.LocalPath));
                    onException?.Invoke(ex);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    var sb = new System.Text.StringBuilder();
                    foreach (var exSub in ex.LoaderExceptions)
                    {
                        var msg = exSub.ToString();
                        if (msg != null)
                        {
                            sb.AppendLine(msg);
                        }
                        if (exSub is FileNotFoundException exFileNotFound && !string.IsNullOrEmpty(exFileNotFound.FusionLog))
                        {
                            sb.AppendLine("Fusion Log:");
                            sb.AppendLine(exFileNotFound.FusionLog);
                            sb.AppendLine();
                        }
                    }

                    onDiagnostic?.Invoke(this.LoadError(target, ErrorCode.TypeLoadExceptionInCompilerPlugin, target.LocalPath));
                    onException?.Invoke(new TypeLoadException(sb.ToString(), ex.InnerException));
                }
                catch (Exception ex)
                {
                    onDiagnostic?.Invoke(this.LoadError(target, ErrorCode.CouldNotLoadCompilerPlugin, target.LocalPath));
                    onException?.Invoke(ex);
                }

                var loadedSteps = new List<LoadedStep>();
                foreach (var type in relevantTypes)
                {
                    var initializedStep = this.CreateStep(type, target, outputFolder, onDiagnostic, onException);
                    if (initializedStep != null)
                    {
                        loadedSteps.Add(initializedStep);
                    }
                }


                rewriteSteps.AddRange(loadedSteps);
            }

            return rewriteSteps.ToImmutableArray();
        }
    }
}
