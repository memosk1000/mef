﻿// -----------------------------------------------------------------------
// Copyright © Microsoft Corporation.  All rights reserved.
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Composition.Runtime;
using System.Linq;
using System.Text;
using Microsoft.Internal;

namespace System.Composition.Hosting.Core
{
    using System.Composition.Hosting.Properties;

    class ExportDescriptorRegistryUpdate : DependencyAccessor
    {
        readonly IDictionary<CompositionContract, ExportDescriptor[]> _partDefinitions;
        readonly ExportDescriptorProvider[] _exportDescriptorProviders;
        readonly IDictionary<CompositionContract, UpdateResult> _updateResults = new Dictionary<CompositionContract, UpdateResult>();

        static readonly CompositionDependency[] NoDependenciesValue = new CompositionDependency[0];
        static readonly Func<CompositionDependency[]> NoDependencies = () => NoDependenciesValue;

        bool _updateFinished;

        public ExportDescriptorRegistryUpdate(
            IDictionary<CompositionContract, ExportDescriptor[]> partDefinitions,
            ExportDescriptorProvider[] exportDescriptorProviders)
        {
            _partDefinitions = partDefinitions;
            _exportDescriptorProviders = exportDescriptorProviders;
        }

        public void Execute(CompositionContract contract)
        {
            // Opportunism - we'll miss recursive calls to Execute(), but this will catch some problems
            // and the _updateFinished flag is required for other purposes anyway.
            if (_updateFinished) throw new InvalidOperationException("The update has already executed.");

            CompositionDependency initial;
            if (TryResolveOptionalDependency("initial request", contract, true, out initial))
            {
                var @checked = new HashSet<ExportDescriptorPromise>();
                var checking = new Stack<CompositionDependency>();
                CheckTarget(initial, @checked, checking);
            }

            _updateFinished = true;

            foreach (var result in _updateResults)
            {
                var resultContract = result.Key;
                var descriptors = result.Value.GetResults().Select(cb => cb.GetDescriptor()).ToArray();
                _partDefinitions.Add(resultContract, descriptors);
            }
        }

        void CheckTarget(CompositionDependency dependency, HashSet<ExportDescriptorPromise> @checked, Stack<CompositionDependency> checking)
        {
            if (dependency.IsError)
            {
                var message = new StringBuilder();
                dependency.DescribeError(message);
                message.AppendLine();
                message.Append(DescribeCompositionStack(dependency, checking));

                throw ThrowHelper.CompositionException(message.ToString());
            }

            if (@checked.Contains(dependency.Target))
                return;

            @checked.Add(dependency.Target);

            checking.Push(dependency);
            foreach (var dep in dependency.Target.Dependencies)
                CheckDependency(dep, @checked, checking);
            checking.Pop();
        }

        void CheckDependency(CompositionDependency dependency, HashSet<ExportDescriptorPromise> @checked, Stack<CompositionDependency> checking)
        {
            if (@checked.Contains(dependency.Target))
            {
                var sharedSeen = false;
                var nonPrereqSeen = !dependency.IsPrerequisite;

                foreach (var step in checking)
                {
                    if (step.Target.IsShared)
                        sharedSeen = true;

                    if (sharedSeen && nonPrereqSeen)
                        break;

                    if (step.Target.Equals(dependency.Target))
                    {
                        var message = new StringBuilder();
                        message.AppendFormat(Resources.ExportDescriptor_UnsupportedCycle, dependency.Target.Origin);
                        message.AppendLine();
                        message.Append(DescribeCompositionStack(dependency, checking));
                        throw ThrowHelper.CompositionException(message.ToString());
                    }

                    if (!step.IsPrerequisite)
                        nonPrereqSeen = true;
                }
            }

            CheckTarget(dependency, @checked, checking);
        }

        StringBuilder DescribeCompositionStack(CompositionDependency import, IEnumerable<CompositionDependency> dependencies)
        {
            var result = new StringBuilder();
            if (dependencies.FirstOrDefault() == null)
            {
                return result;
            }

            foreach (var step in dependencies)
            {
                result.AppendFormat(Resources.ExportDescriptor_DependencyErrorLine, import.Site, step.Target.Origin);
                result.AppendLine();
                import = step;
            }

            result.AppendFormat(Resources.ExportDescriptor_DependencyErrorContract, import.Contract);
            return result;
        }

        protected override IEnumerable<ExportDescriptorPromise> GetPromises(CompositionContract contract)
        {
            Assumes.IsTrue(!_updateFinished, "Update is finished - dependencies should have been requested earlier.");

            ExportDescriptor[] definitions;
            if (_partDefinitions.TryGetValue(contract, out definitions))
                return definitions.Select(d => new ExportDescriptorPromise(contract, "Preexisting", false, NoDependencies, _ => d)).ToArray();

            UpdateResult updateResult;
            if (!_updateResults.TryGetValue(contract, out updateResult))
            {
                updateResult = new UpdateResult(_exportDescriptorProviders);
                _updateResults.Add(contract, updateResult);
            }

            ExportDescriptorProvider nextProvider;
            while (updateResult.TryDequeueNextProvider(out nextProvider))
            {
                var newDefinitions = nextProvider.GetExportDescriptors(contract, this);
                foreach (var definition in newDefinitions)
                    updateResult.AddPromise(definition);
            }

            return updateResult.GetResults();
        }
    }
}
