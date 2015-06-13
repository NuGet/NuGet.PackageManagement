using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;

namespace NuGet.PackageManagement
{
    public interface IDeleteOnRestartManager
    {
        /// <summary>
        /// Gets the list of package directories that are still need to be deleted in the
        /// local package repository.
        /// </summary>
        IList<string> GetPackageDirectoriesMarkedForDeletion();

        /// <summary>
        /// Marks package directory for future removal if it was not fully deleted during the normal uninstall process
        /// if the directory does not contain any added or modified files.
        /// </summary>
        void MarkPackageDirectoryForDeletion(PackageIdentity package, string packageRoot, INuGetProjectContext projectContext);

        /// <summary>
        /// Attempts to remove marked package directories that were unable to be fully deleted during the original uninstall.
        /// </summary>
        Task DeleteMarkedPackageDirectories(INuGetProjectContext projectContext);
    }
}