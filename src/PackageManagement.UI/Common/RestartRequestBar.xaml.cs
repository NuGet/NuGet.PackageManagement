﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell.Interop;
using NuGet.Packaging;
using NuGet.ProjectManagement;
using VsBrushes = Microsoft.VisualStudio.Shell.VsBrushes;

namespace NuGet.PackageManagement.UI
{
    /// <summary>
    /// Interaction logic for RestartRequestBar.xaml
    /// </summary>
    public partial class RestartRequestBar : UserControl, INuGetProjectContext
    {
        private readonly IDeleteOnRestartManager _deleteOnRestartManager;

        private readonly IVsShell4 _vsRestarter;

        public PackageExtractionContext PackageExtractionContext { get; set; }

        public ISourceControlManagerProvider SourceControlManagerProvider { get; }

        public ProjectManagement.ExecutionContext ExecutionContext { get; }

        public RestartRequestBar(IDeleteOnRestartManager deleteOnRestartManager, IVsShell4 vsRestarter)
        {
            InitializeComponent();
            _deleteOnRestartManager = deleteOnRestartManager;
            _vsRestarter = vsRestarter;

            // Since the DeleteonRestartManager is guranteed to be a singleton, we can rely on it for firing the events
            // both in package management ui and the powershell console.
            _deleteOnRestartManager.PackagesMarkedForDeletionFound += OnPackagesMarkedForDeletionFound;

            // Since Loaded event is not reliable, we do it at construction time initially, this is only for
            // the case when this needs to show up in package manager window (since package manager ui gets recreated,
            // the check can happen here). For powershell, it depends on the event handlers firigng up either via
            // package manager ui or the powershell commands like uninstall package.
            _deleteOnRestartManager.CheckAndRaisePackageDirectoriesMarkedForDeletion();

            // Set DynamicResource binding in code
            // The reason we can't set it in XAML is that the VsBrushes class come from either
            // Microsoft.VisualStudio.Shell.12 or Microsoft.VisualStudio.Shell.14 assembly,
            // depending on whether NuGet runs inside VS12 or VS14.
            RestartBar.SetResourceReference(Border.BackgroundProperty, VsBrushes.InfoBackgroundKey);
            RestartBar.SetResourceReference(Border.BorderBrushProperty, VsBrushes.ActiveBorderKey);
        }

        private void OnPackagesMarkedForDeletionFound(
            object source,
            PackagesMarkedForDeletionEventArgs eventArgs)
        {
            var packageDirectoriesMarkedForDeletion = eventArgs.MarkedForDeletion;
            UpdateRestartBar(packageDirectoriesMarkedForDeletion);
        }

        private void UpdateRestartBar(IReadOnlyList<string> packagesMarkedForDeletion)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if (packagesMarkedForDeletion.Any())
                {
                    var message = string.Format(
                       CultureInfo.CurrentCulture,
                       UI.Resources.RequestRestartToCompleteUninstall,
                       string.Join(", ", packagesMarkedForDeletion));
                    RequestRestartMessage.Text = message;
                    RestartBar.Visibility = Visibility.Visible;
                }
                else
                {
                    RestartBar.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void ExecuteRestart(object sender, EventArgs e)
        {
            _vsRestarter.Restart((uint)__VSRESTARTTYPE.RESTART_Normal);
        }

        public void Log(MessageLevel level, string message, params object[] args)
        {
            ShowMessage(String.Format(CultureInfo.CurrentCulture, message, args));
        }

        public void ReportError(string message)
        {
            ShowMessage(message);
        }

        public void CleanUp()
        {
            if (_deleteOnRestartManager != null)
            {
                _deleteOnRestartManager.PackagesMarkedForDeletionFound -= OnPackagesMarkedForDeletionFound;
            }
        }

        public FileConflictAction ResolveFileConflict(string message)
        {
            return FileConflictAction.IgnoreAll;
        }

        private void ShowMessage(string message)
        {
            NuGetUIThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await NuGetUIThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RequestRestartMessage.Text = message;
            });
        }
    }
}
