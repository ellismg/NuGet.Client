using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Protocol.Core.v3.Utility
{
    public static class DownloadUtility
    {
        private const string DownloadTimeoutKey = "nuget_download_timeout";

        private static TimeSpan? downloadTimeout;

        private static TimeSpan DownloadTimeout
        {
            get
            {
                if (!downloadTimeout.HasValue)
                {
                    var unparsedTimeout = Environment.GetEnvironmentVariable(DownloadTimeoutKey);
                    uint timeoutSeconds;
                    if (!uint.TryParse(unparsedTimeout, out timeoutSeconds))
                    {
                        downloadTimeout = TimeSpan.FromMinutes(5);
                    }
                    else
                    {
                        downloadTimeout = TimeSpan.FromSeconds(timeoutSeconds);
                    }
                }

                return downloadTimeout.Value;
            }
        }

        public static async Task DownloadAsync(Stream source, Stream destination, string downloadName, CancellationToken token)
        {
            var timeoutMessage = string.Format(
                CultureInfo.CurrentCulture,
                Strings.DownloadTimeout,
                downloadName,
                (int) DownloadTimeout.TotalSeconds);

            await TimeoutUtility.StartWithTimeout(
                timeoutToken => source.CopyToAsync(destination, bufferSize: 8192, cancellationToken: timeoutToken),
                DownloadTimeout,
                timeoutMessage,
                token);
        }
    }
}