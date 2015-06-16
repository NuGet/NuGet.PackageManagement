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

        string _packagesFolderPath = null;

        private ISolutionManager SolutionManager { get; }

        private ISettings Settings { get; }

        public DeleteOnRestartManager(string folderPath)
        {
            _packagesFolderPath = folderPath;
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

            if (SolutionManager.SolutionDirectory != null)
            {
                _packagesFolderPath = PackagesFolderPathUtility.GetPackagesFolderPath(SolutionManager, Settings);
            }
        }

        public IReadOnlyList<string> GetPackageDirectoriesMarkedForDeletion()
        {
            if (_packagesFolderPath == null)
            {
                return new List<string>();
            }

            var candidates = FileSystemUtility.GetFiles(_packagesFolderPath, path: "", filter: DeletionMarkerFilter, recursive: false)
                // strip the DeletionMarkerFilter at the end of the path to get the package name.
                .Select(path => Path.Combine(_packagesFolderPath, Path.ChangeExtension(path, null))).ToList();

            var filesWithoutFolders = candidates.Where(path => !Directory.Exists(path));
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
            if (_packagesFolderPath == null)
            {
                return;
            }

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
        public void DeleteMarkedPackageDirectories(INuGetProjectContext projectContext)
        {
            if (_packagesFolderPath == null)
            {
                return;
            }

            try
            {
                var packages = GetPackageDirectoriesMarkedForDeletion();
                foreach (var package in packages)
                {
                    try
                    {
                        FileSystemUtility.DeleteDirectorySafe(package, true, projectContext);
                    }
                    finally
                    {
                        if (!Directory.Exists(package))
                        {
                            var deleteMeFilePath = Path.Combine(package, DeletionMarkerSuffix);
                            FileSystemUtility.DeleteFile(deleteMeFilePath, projectContext);
                        }
                        else
                        {
                            projectContext.Log(
                                MessageLevel.Warning,
                                string.Format(CultureInfo.CurrentCulture, Strings.Warning_FailedToDeleteMarkedPackageDirectory, package));
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