﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NuGet.Resolver;

namespace NuGet.PackageManagement
{
    /// <summary>
    /// Resolution context such as DependencyBehavior, IncludePrerelease and so on
    /// </summary>
    public class ResolutionContext
    {
        /// <summary>
        /// The only public constructor to create the resolution context
        /// </summary>
        public ResolutionContext(
            DependencyBehavior dependencyBehavior = DependencyBehavior.Lowest,
            bool includePrelease = false,
            bool includeUnlisted = true,
            VersionConstraints versionConstraints = VersionConstraints.None)
        {
            DependencyBehavior = dependencyBehavior;
            IncludePrerelease = includePrelease;
            IncludeUnlisted = includeUnlisted;
            VersionConstraints = versionConstraints;
        }

        /// <summary>
        /// Determines the dependency behavior
        /// </summary>
        public DependencyBehavior DependencyBehavior { get; }

        /// <summary>
        /// Determines if prerelease may be included in the installation
        /// </summary>
        public bool IncludePrerelease { get; }

        /// <summary>
        /// Determines if unlisted packages may be included in installation
        /// </summary>
        public bool IncludeUnlisted { get; }

        /// <summary>
        /// Determines the containts that are placed on package update selection with respect to the installed packages
        /// </summary>
        public VersionConstraints VersionConstraints { get; }
    }
}
