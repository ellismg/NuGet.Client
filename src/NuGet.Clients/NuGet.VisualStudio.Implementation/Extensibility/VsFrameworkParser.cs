// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Runtime.Versioning;
using NuGet.Frameworks;
using NuGet.VisualStudio.Implementation.Resources;
using NuGet.VisualStudio.Implementation.Utility;

namespace NuGet.VisualStudio
{
    [Export(typeof(IVsFrameworkParser))]
    public class VsFrameworkParser : IVsFrameworkParser
    {
        public FrameworkName ParseFrameworkName(string shortOrFullName)
        {
            if (shortOrFullName == null)
            {
                throw new ArgumentNullException(nameof(shortOrFullName));
            }

            try
            {
                var nugetFramework = NuGetFramework.Parse(shortOrFullName);
                return FrameworkNameUtility.GetFrameworkName(nugetFramework);
            }
            catch(Exception e)
            {
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    VsResources.InvalidFrameworkForParsing,
                    shortOrFullName);

                throw new ArgumentException(message, e);
            }
        }
    }
}
