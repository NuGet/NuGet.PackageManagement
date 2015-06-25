﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectManagement.Projects;
using NuGet.ProjectModel;

namespace NuGet.PackageManagement
{
    /// <summary>
    /// Helper class for calling the RestoreCommand
    /// </summary>
    public static class BuildIntegratedRestoreUtility
    {
        /// <summary>
        /// Restore a build integrated project and update the lock file
        /// </summary>
        public static async Task<RestoreResult> RestoreAsync(
            BuildIntegratedNuGetProject project,
            INuGetProjectContext projectContext,
            IEnumerable<string> sources,
            Configuration.ISettings settings,
            CancellationToken token)
        {
            // Restore
            var result = await RestoreAsync(project, project.PackageSpec, projectContext, sources, settings, token);

            // Throw before writing if this has been canceled
            token.ThrowIfCancellationRequested();

            var logger = new ProjectContextLogger(projectContext);

            // Write out the lock file and msbuild files
            result.Commit(logger);

            return result;
        }

        /// <summary>
        /// Restore without writing the lock file
        /// </summary>
        internal static async Task<RestoreResult> RestoreAsync(
            BuildIntegratedNuGetProject project,
            PackageSpec packageSpec,
            INuGetProjectContext projectContext,
            IEnumerable<string> sources,
            Configuration.ISettings settings,
            CancellationToken token)
        {
            // Restoring packages
            projectContext.Log(ProjectManagement.MessageLevel.Info, Strings.BuildIntegratedPackageRestoreStarted, project.ProjectName);

            var packageSources = sources.Select(source => new Configuration.PackageSource(source));
            var request = new RestoreRequest(packageSpec, packageSources, SettingsUtility.GetGlobalPackagesFolder(settings));
            request.MaxDegreeOfConcurrency = PackageManagementConstants.DefaultMaxDegreeOfParallelism;

            // Find the full closure of project.json files and referenced projects
            var projectReferences = await project.GetProjectReferenceClosureAsync();
            request.ExternalProjects = projectReferences.Select(reference => BuildIntegratedProjectUtility.ConvertProjectReference(reference)).ToList();

            token.ThrowIfCancellationRequested();

            var command = new RestoreCommand(new ProjectContextLogger(projectContext), request);

            // Execute the restore
            var result = await command.ExecuteAsync(token);

            // Report a final message with the Success result
            if (result.Success)
            {
                projectContext.Log(ProjectManagement.MessageLevel.Info, Strings.BuildIntegratedPackageRestoreSucceeded, project.ProjectName);
            }
            else
            {
                projectContext.Log(ProjectManagement.MessageLevel.Info, Strings.BuildIntegratedPackageRestoreFailed, project.ProjectName);
            }

            return result;
        }

        /// <summary>
        /// Find all packages added to <paramref name="updatedLockFile"/>.
        /// </summary>
        public static IReadOnlyList<PackageIdentity> GetAddedPackages(LockFile originalLockFile, LockFile updatedLockFile)
        {
            var updatedPackages = updatedLockFile.Targets.SelectMany(target => target.Libraries)
                .Select(library => new PackageIdentity(library.Name, library.Version));

            var originalPackages = originalLockFile.Targets.SelectMany(target => target.Libraries)
                .Select(library => new PackageIdentity(library.Name, library.Version));

            var results = updatedPackages.Except(originalPackages, PackageIdentity.Comparer).ToList();

            return results;
        }

        /// <summary>
        /// Find all packages removed from <paramref name="updatedLockFile"/>.
        /// </summary>
        public static IReadOnlyList<PackageIdentity> GetRemovedPackages(LockFile originalLockFile, LockFile updatedLockFile)
        {
            return GetAddedPackages(updatedLockFile, originalLockFile);
        }
    }
}
