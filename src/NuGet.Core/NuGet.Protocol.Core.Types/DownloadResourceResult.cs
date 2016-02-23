﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using NuGet.Packaging;

namespace NuGet.Protocol.Core.Types
{
    public enum DownloadResourceResultType
    {
        Available,
        NotFound,
        Cancelled
    }

    /// <summary>
    /// The result of <see cref="DownloadResource.DownloadResource"/>.
    /// </summary>
    public class DownloadResourceResult : IDisposable
    {
        private readonly Stream _stream;
        private readonly PackageReaderBase _packageReader;

        public DownloadResourceResult(DownloadResourceResultType type)
        {
            if (type == DownloadResourceResultType.Available)
            {
                throw new ArgumentException("A stream should be provided when the result is available.");
            }

            Type = type;
        }

        public DownloadResourceResult(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            Type = DownloadResourceResultType.Available;
            _stream = stream;
        }

        public DownloadResourceResult(Stream stream, PackageReaderBase packageReader)
            : this(stream)
        {
            _packageReader = packageReader;
        }

        public DownloadResourceResultType Type { get; }

        /// <summary>
        /// Gets the package <see cref="PackageStream"/>.
        /// </summary>
        public Stream PackageStream => _stream;

        /// <summary>
        /// Gets the <see cref="PackageReaderBase"/> for the package.
        /// </summary>
        /// <remarks>This property can be null.</remarks>
        public PackageReaderBase PackageReader => _packageReader;

        public void Dispose()
        {
            _stream?.Dispose();
            _packageReader?.Dispose();
        }
    }
}
