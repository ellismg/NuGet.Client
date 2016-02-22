// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Globalization;
using NuGet.Versioning;
using NuGet.VisualStudio.Implementation.Resources;

namespace NuGet.VisualStudio
{
    [Export(typeof(IVsSemanticVersionComparer))]
    public class VsSemanticVersionComparer : IVsSemanticVersionComparer
    {
        public int Compare(string versionA, string versionB)
        {
            if (versionA == null)
            {
                throw new ArgumentNullException(nameof(versionA));
            }

            if (versionB == null)
            {
                throw new ArgumentNullException(nameof(versionB));
            }

            NuGetVersion parsedVersionA;
            if (!NuGetVersion.TryParse(versionA, out parsedVersionA))
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    VsResources.InvalidSemanticVersionStringIncludingInput,
                    versionA);
                throw new ArgumentException(message, nameof(versionA));
            }

            NuGetVersion parsedVersionB;
            if (!NuGetVersion.TryParse(versionB, out parsedVersionB))
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    VsResources.InvalidSemanticVersionStringIncludingInput,
                    versionB);
                throw new ArgumentException(message, nameof(versionB));
            }

            return parsedVersionA.CompareTo(parsedVersionB);
        }
    }
}
