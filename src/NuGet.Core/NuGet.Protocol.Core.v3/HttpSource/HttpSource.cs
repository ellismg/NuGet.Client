﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Logging;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol.Core.v3;
using NuGet.Protocol.Core.v3.Data;

namespace NuGet.Protocol
{
    public class HttpSource : IDisposable
    {
        private const int BufferSize = 8192;
        private readonly Func<Task<HttpHandlerResource>> _messageHandlerFactory;
        private readonly Uri _baseUri;
        private HttpClient _httpClient;
        private bool _disposed;
        private int _authRetries;

        // Only one thread may re-create the http client at a time.
        private readonly SemaphoreSlim _httpClientLock = new SemaphoreSlim(1, 1);

        // In order to avoid too many open files error, set concurrent requests number to 16 on Mac
        private readonly static int ConcurrencyLimit = RuntimeEnvironmentHelper.IsMacOSX ? 16 : 128;

        // Limiting concurrent requests to limit the amount of files open at a time on Mac OSX
        // the default is 256 which is easy to hit if we don't limit concurrency
        private readonly static SemaphoreSlim _throttle = new SemaphoreSlim(ConcurrencyLimit, ConcurrencyLimit);

        public HttpSource(string sourceUrl, Func<Task<HttpHandlerResource>> messageHandlerFactory)
        {
            if (sourceUrl == null)
            {
                throw new ArgumentNullException(nameof(sourceUrl));
            }

            if (messageHandlerFactory == null)
            {
                throw new ArgumentNullException(nameof(messageHandlerFactory));
            }

            _baseUri = new Uri(sourceUrl);
            _messageHandlerFactory = messageHandlerFactory;
        }

        public ILogger Logger { get; set; }

        internal Task<HttpSourceResult> GetAsync(string uri,
            string cacheKey,
            HttpSourceCacheContext context,
            CancellationToken cancellationToken)
        {
            return GetAsync(uri, cacheKey, context, ignoreNotFounds: false, cancellationToken: cancellationToken);
        }

        internal async Task<HttpSourceResult> GetAsync(string uri,
            string cacheKey,
            HttpSourceCacheContext context,
            bool ignoreNotFounds,
            CancellationToken cancellationToken)
        {
            var sw = new Stopwatch();
            sw.Start();

            var result = await TryCache(uri, cacheKey, context, cancellationToken);
            if (result.Stream != null)
            {
                Logger.LogInformation(string.Format(CultureInfo.InvariantCulture, "  {0} {1}", "CACHE", uri));
                return result;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            STSAuthHelper.PrepareSTSRequest(_baseUri, CredentialStore.Instance, request);

            await _throttle.WaitAsync();

            try
            {
                Logger.LogVerbose($"Current http requests queued: {_throttle.CurrentCount + 1}");

                Logger.LogInformation(string.Format(CultureInfo.InvariantCulture, "  {0} {1}.", "GET", uri));

                // Read the response headers before reading the entire stream to avoid timeouts from large packages.
                using (var response = await SendWithCredentialSupportAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken))
                {
                    if (ignoreNotFounds && response.StatusCode == HttpStatusCode.NotFound)
                    {
                        Logger.LogInformation(string.Format(CultureInfo.InvariantCulture,
                            "  {1} {0} {2}ms", uri, response.StatusCode.ToString(), sw.ElapsedMilliseconds.ToString()));
                        return new HttpSourceResult();
                    }

                    response.EnsureSuccessStatusCode();

                    await CreateCacheFile(result, response, context, cancellationToken);

                    Logger.LogInformation(string.Format(CultureInfo.InvariantCulture,
                        "  {1} {0} {2}ms", uri, response.StatusCode.ToString(), sw.ElapsedMilliseconds.ToString()));

                    return result;
                }
            }
            finally
            {
                _throttle.Release();
            }
        }

        private async Task<HttpResponseMessage> SendWithCredentialSupportAsync(
            HttpRequestMessage request,
            HttpCompletionOption completionOption,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            ICredentials promptCredentials = null;

            // Keep a local copy of the http client, this allows the thread to check if another thread has
            // already added new credentials.
            var httpClient = _httpClient;

            // Authorizing may take multiple attempts
            while (true)
            {
                // Clean up any previous responses
                if (response != null)
                {
                    response.Dispose();
                }

                // Read the response headers before reading the entire stream to avoid timeouts from large packages.
                response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    try
                    {
                        // Only one request may prompt and attempt to auth at a time
                        await _httpClientLock.WaitAsync();

                        // Auth may have happened on another thread, if so just continue
                        if (!object.ReferenceEquals(httpClient, _httpClient))
                        {
                            continue;
                        }

                        // Give up after 10 tries.
                        _authRetries++;
                        if (_authRetries >= 10)
                        {
                            return response;
                        }

                        // Windows auth
                        if (STSAuthHelper.TryRetrieveSTSToken(_baseUri, CredentialStore.Instance, response))
                        {
                            // Auth token found, create a new message handler and retry.
                            _httpClient = await CreateHttpClient();
                            continue;
                        }

                        // Prompt the user
                        promptCredentials = await PromptForCredentials(cancellationToken);

                        if (promptCredentials != null)
                        {
                            // The user entered credentials, create a new message handler that includes
                            // these and retry.
                            _httpClient = await CreateHttpClient();
                            continue;
                        }
                    }
                    finally
                    {
                        _httpClientLock.Release();
                    }
                }

                if (promptCredentials != null && HttpHandlerResourceV3.CredentialsSuccessfullyUsed != null)
                {
                    HttpHandlerResourceV3.CredentialsSuccessfullyUsed(_baseUri, promptCredentials);
                }

                return response;
            }
        }

        private async Task<ICredentials> PromptForCredentials(CancellationToken cancellationToken)
        {
            ICredentials promptCredentials = null;

            if (HttpHandlerResourceV3.PromptForCredentials != null)
            {
                try
                {
                    // Only one prompt may display at a time.
                    await HttpHandlerResourceV3Provider.CredentialPromptLock.WaitAsync();

                    promptCredentials =
                        await HttpHandlerResourceV3.PromptForCredentials(_baseUri, cancellationToken);
                }
                finally
                {
                    HttpHandlerResourceV3Provider.CredentialPromptLock.Release();
                }
            }

            return promptCredentials;
        }

        private async Task<HttpClient> CreateHttpClient()
        {
            var credentials = CredentialStore.Instance.GetCredentials(_baseUri);
            var handlerResource = await _messageHandlerFactory();

            if (credentials != null)
            {
                handlerResource.ClientHandler.Credentials = credentials;
            }
            else
            {
                handlerResource.ClientHandler.UseDefaultCredentials = true;
            }

            return new HttpClient(handlerResource.MessageHandler);
        }

        private static Task CreateCacheFile(
            HttpSourceResult result,
            HttpResponseMessage response,
            HttpSourceCacheContext context,
            CancellationToken cancellationToken)
        {
            var newFile = result.CacheFileName + "-new";

            // Zero value of TTL means we always download the latest package
            // So we write to a temp file instead of cache
            if (context.MaxAge.Equals(TimeSpan.Zero))
            {
                var newCacheFile = Path.Combine(context.RootTempFolder, Path.GetRandomFileName());

                result.CacheFileName = newCacheFile;

                newFile = Path.Combine(context.RootTempFolder, Path.GetRandomFileName());
            }

            // The update of a cached file is divided into two steps:
            // 1) Delete the old file. 2) Create a new file with the same name.
            // To prevent race condition among multiple processes, here we use a lock to make the update atomic.
            return ConcurrencyUtilities.ExecuteWithFileLockedAsync(result.CacheFileName,
                action: async token =>
                {
                    using (var stream = new FileStream(
                        newFile,
                        FileMode.Create,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite | FileShare.Delete,
                        BufferSize,
                        useAsync: true))
                    {
                        await response.Content.CopyToAsync(stream);
                        await stream.FlushAsync(cancellationToken);
                    }

                    if (File.Exists(result.CacheFileName))
                    {
                        // Process B can perform deletion on an opened file if the file is opened by process A
                        // with FileShare.Delete flag. However, the file won't be actually deleted until A close it.
                        // This special feature can cause race condition, so we never delete an opened file.
                        if (!IsFileAlreadyOpen(result.CacheFileName))
                        {
                            File.Delete(result.CacheFileName);
                        }
                    }

                    // If the destination file doesn't exist, we can safely perform moving operation.
                    // Otherwise, moving operation will fail.
                    if (!File.Exists(result.CacheFileName))
                    {
                        File.Move(
                                newFile,
                                result.CacheFileName);
                    }

                    // Even the file deletion operation above succeeds but the file is not actually deleted,
                    // we can still safely read it because it means that some other process just updated it
                    // and we don't need to update it with the same content again.
                    result.Stream = new FileStream(
                            result.CacheFileName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read | FileShare.Delete,
                            BufferSize,
                            useAsync: true);

                    return 0;
                },
                token: cancellationToken);
        }

        private async Task<HttpSourceResult> TryCache(string uri,
            string cacheKey,
            HttpSourceCacheContext context,
            CancellationToken token)
        {
            var baseFolderName = RemoveInvalidFileNameChars(ComputeHash(_baseUri.OriginalString));
            var baseFileName = RemoveInvalidFileNameChars(cacheKey) + ".dat";
            var cacheAgeLimit = context.MaxAge;
            var cacheFolder = Path.Combine(NuGetEnvironment.GetFolderPath(NuGetFolderPath.HttpCacheDirectory), baseFolderName);
            var cacheFile = Path.Combine(cacheFolder, baseFileName);

            if (!Directory.Exists(cacheFolder)
                && !cacheAgeLimit.Equals(TimeSpan.Zero))
            {
                Directory.CreateDirectory(cacheFolder);
            }

            // Acquire the lock on a file before we open it to prevent this process
            // from opening a file deleted by the logic in HttpSource.GetAsync() in another process
            return await ConcurrencyUtilities.ExecuteWithFileLockedAsync(cacheFile,
                action: cancellationToken =>
                {
                    if (File.Exists(cacheFile))
                    {
                        var fileInfo = new FileInfo(cacheFile);
                        var age = DateTime.UtcNow.Subtract(fileInfo.LastWriteTimeUtc);
                        if (age < cacheAgeLimit)
                        {
                            var stream = new FileStream(
                                cacheFile,
                                FileMode.Open,
                                FileAccess.Read,
                                FileShare.Read | FileShare.Delete,
                                BufferSize,
                                useAsync: true);

                            return Task.FromResult(new HttpSourceResult
                            {
                                CacheFileName = cacheFile,
                                Stream = stream,
                            });
                        }
                    }

                    return Task.FromResult(new HttpSourceResult
                    {
                        CacheFileName = cacheFile,
                    });
                },
                token: token);
        }

        private static string ComputeHash(string value)
        {
            var trailing = value.Length > 32 ? value.Substring(value.Length - 32) : value;
            byte[] hash;
            using (var sha = SHA1.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            }

            const string hex = "0123456789abcdef";
            return hash.Aggregate("$" + trailing, (result, ch) => "" + hex[ch / 0x10] + hex[ch % 0x10] + result);
        }

        private static string RemoveInvalidFileNameChars(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(
                value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()
                )
                .Replace("__", "_")
                .Replace("__", "_");
        }

        private static bool IsFileAlreadyOpen(string filePath)
        {
            FileStream stream = null;

            try
            {
                stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
            catch
            {
                return true;
            }
            finally
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
            }

            return false;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;

                Dispose();
            }
        }

        public void Dispose()
        {
            if (_httpClient != null)
            {
                _httpClient.Dispose();
            }

            _httpClientLock.Dispose();
        }
    }
}