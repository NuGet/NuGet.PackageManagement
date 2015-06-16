// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;

namespace NuGet.PackageManagement
{
    /// <summary>
    /// Event arguments for <see cref="IDeleteOnRestartManager.PackagesMarkedForDeletionFound"/> event.
    /// </summary>
    public class PackagesMarkedForDeletionEventArgs : EventArgs
    {
        /// <summary>
        /// Gets the directories which are marked for deletion.
        /// </summary>
        public IReadOnlyList<string> MarkedForDeletion { get; }

        /// <summary>
        /// Creates a new instance of <see cref="PackagesMarkedForDeletionEventArgs"/>.
        /// </summary>
        /// <param name="markedForDeletion">The directory paths that are marked for deletion.</param>
        public PackagesMarkedForDeletionEventArgs(IReadOnlyList<string> markedForDeletion)
        {
            MarkedForDeletion = markedForDeletion;
        }
    }
}