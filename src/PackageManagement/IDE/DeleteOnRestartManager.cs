using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.Protocol.Core.Types;

namespace NuGet.PackageManagement
{
    public sealed class DeleteOnRestartManager : IDeleteOnRestartManager
    {
        // The file extension to add to the empty files which will be placed adjacent to partially uninstalled package
        // directories marking them for removal the next time the solution is opened.
        private const string DeletionMarkerSuffix = ".deleteme";
        private const string DeletionMarkerFilter = "*" + DeletionMarkerSuffix;

        private readonly FolderNuGetProject _folderNugetProject;

        private ISolutionManager SolutionManager { get; }

        private ISettings Settings { get; }

        /// <summary>
        /// Create a new instance of <see cref="DeleteOnRestartManager"/>.
        /// </summary>
        /// <param name="project"></param>
        public DeleteOnRestartManager(NuGetProject project)
        {
            _folderNugetProject = project as FolderNuGetProject;
            if (_folderNugetProject == null)
            {
                _folderNugetProject = (project as MSBuildNuGetProject)?.FolderNuGetProject;
            }

            if (_folderNugetProject == null)
            {
                // TODO: should this throw? Replace with a proper message.
                throw new ArgumentException("Unsupported projectType");
            }
        }

        public DeleteOnRestartManager(
            ISettings settings,
            ISolutionManager solutionManager)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (solutionManager == null)
            {
                throw new ArgumentNullException(nameof(solutionManager));
            }

            Settings = settings;
            SolutionManager = solutionManager;
            var packagesFolderPath = PackagesFolderPathUtility.GetPackagesFolderPath(SolutionManager, Settings);
            _folderNugetProject = new FolderNuGetProject(packagesFolderPath);
        }

        public IList<string> GetPackageDirectoriesMarkedForDeletion()
        {
            var candidates = FileSystemUtility.GetFiles(_folderNugetProject.Root, path: "", filter: DeletionMarkerFilter, recursive: false)
                // strip the DeletionMarkerFilter at the end of the path to get the package name.
                .Select(path => Path.ChangeExtension(path, null)).ToList();

            var filesWithoutFolders = candidates.Where(path => !File.Exists(path));
            foreach (var directory in filesWithoutFolders)
            {
                File.Delete(directory + DeletionMarkerSuffix);
            }

            return candidates.Where(path => Directory.Exists(path)).ToList();
        }

        /// <summary>
        /// Marks package directory for future removal if it was not fully deleted during the normal uninstall process
        /// if the directory does not contain any added or modified files.
        /// The package directory will be marked by an adjacent *directory name*.deleteme file.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to log an exception as a warning and move on")]
        public void MarkPackageDirectoryForDeletion(PackageIdentity package, string packageRoot, INuGetProjectContext projectContext)
        {
            try
            {
                using (FileSystemUtility.CreateFile(packageRoot + DeletionMarkerSuffix, projectContext))
                {
                }
            }
            catch (Exception e)
            {
                projectContext.Log(
                    MessageLevel.Warning,
                    string.Format(CultureInfo.CurrentCulture, Strings.Warning_FailedToMarkPackageDirectoryForDeletion, packageRoot, e.Message));
            }
        }

        /// <summary>
        /// Attempts to remove package directories that were unable to be fully deleted during the original uninstall.
        /// These directories will be marked by an adjacent *directory name*.deleteme files in the local package repository.
        /// If the directory removal is successful, the .deleteme file will also be removed.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes", Justification = "We want to log an exception as a warning and move on")]
        public async Task DeleteMarkedPackageDirectories(INuGetProjectContext projectContext)
        {
            try
            {
                var packages = await _folderNugetProject.GetInstalledPackagesAsync(CancellationToken.None);
                foreach (var package in packages)
                {
                    var path = _folderNugetProject.GetInstalledPath(package.PackageIdentity);
                    if (!FileSystemUtility.FileExists(path, DeletionMarkerSuffix))
                    {
                        continue;
                    }

                    try
                    {
                        await _folderNugetProject.DeletePackage(package.PackageIdentity, projectContext, CancellationToken.None);
                    }
                    finally
                    {
                        if (!Directory.Exists(path))
                        {
                            var deleteMeFilePath = Path.Combine(Path.GetDirectoryName(path), DeletionMarkerSuffix);
                            FileSystemUtility.DeleteFile(deleteMeFilePath, projectContext);
                        }
                        else
                        {
                            projectContext.Log(
                                MessageLevel.Warning,
                                string.Format(CultureInfo.CurrentCulture, Strings.Warning_FailedToDeleteMarkedPackageDirectory, path));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                projectContext.Log(
                               MessageLevel.Warning,
                               string.Format(CultureInfo.CurrentCulture, Strings.Warning_FailedToDeleteMarkedPackageDirectory, e.Message));
            }
        }
    }
}