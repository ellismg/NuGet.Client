using System;
using System.Globalization;
using System.Runtime.Caching;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace NuGet.PackageManagement.UI
{
    internal class UriToImageCacheValueConverter : IValueConverter
    {
        // same URIs can reuse the bitmapImage that we've already used.
        //private static readonly Dictionary<Uri, BitmapImage> _bitmapImageCache = new Dictionary<Uri, BitmapImage>();
        private static readonly ObjectCache _bitmapImageCache = System.Runtime.Caching.MemoryCache.Default;

        public static readonly BitmapImage DefaultPackageIcon;

        static UriToImageCacheValueConverter()
        {
            DefaultPackageIcon = new BitmapImage();
            DefaultPackageIcon.BeginInit();

            // If the DLL name changes, this URI would need to change to match.
            DefaultPackageIcon.UriSource = new Uri("pack://application:,,,/NuGet.PackageManagement.UI;component/Resources/packageicon.png");

            // Instead of scaling larger images and keeping larger image in memory, this makes it so we scale it down, and throw away the bigger image.
            // Only need to set this on one dimension, to preserve aspect ratio
            DefaultPackageIcon.DecodePixelWidth = 32;

            DefaultPackageIcon.EndInit();
        }

        // this is called when users move away from "Browse" tab.
        public static void ClearBitmapImageCache()
        {
            //_bitmapImageCache.Clear();
        }

        private static long iconLoadAttempts = 0;
        private static int iconFailures = 0;

        // If we fail at least this high (failures/attempts), we'll shut off image loads.
        // TODO: Should we allow this to be overridden in nuget.config.
        private const double stopLoadingImageThreshold = 0.50;

        private static System.Net.Cache.RequestCachePolicy requestCacheIfAvailable = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.CacheIfAvailable);

        // We bind to a BitmapImage instead of a Uri so that we can control the decode size, since we are displaying 32x32 images, while many of the images are 128x128 or larger.
        // This leads to a memory savings.
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var IconUrl = (Uri)value;
            if (IconUrl == null)
            {
                return null;
            }

            BitmapImage iconBitmapImage = null;
            if ((iconBitmapImage != DefaultPackageIcon) && (iconBitmapImage == null || iconBitmapImage.UriSource != IconUrl))
            {
                iconBitmapImage = _bitmapImageCache.Get(IconUrl.ToString()) as BitmapImage;
                if (iconBitmapImage != null)
                {
                    // protect against failure if we are still downloading this bitmapImage from the cache.
                    if (iconBitmapImage.IsDownloading)
                    {
                        iconBitmapImage.DecodeFailed += IconBitmapImage_DownloadOrDecodeFailed;
                        iconBitmapImage.DownloadFailed += IconBitmapImage_DownloadOrDecodeFailed;
                        iconBitmapImage.DownloadCompleted += IconBitmapImage_DownloadCompleted;
                    }
                }
                else
                {
                    // Some people run on networks with internal NuGet feeds, but no access to the package images on the internet.
                    // This is meant to detect that kind of case, and stop spamming the network, so the app remains responsive.
                    if (iconFailures < 5 || ((double)iconFailures / iconLoadAttempts) < stopLoadingImageThreshold)
                    {
                        iconBitmapImage = new BitmapImage();
                        iconBitmapImage.BeginInit();
                        iconBitmapImage.UriSource = IconUrl;

                        // Default cache policy: Per MSDN, satisfies a request for a resource either by using the cached copy of the resource or by sending a request
                        // for the resource to the server. The action taken is determined by the current cache policy and the age of the content in the cache.
                        // This is the cache level that should be used by most applications.
                        iconBitmapImage.UriCachePolicy = requestCacheIfAvailable;

                        // Instead of scaling larger images and keeping larger image in memory, this makes it so we scale it down, and throw away the bigger image.
                        // Only need to set this on one dimension, to preserve aspect ratio
                        iconBitmapImage.DecodePixelWidth = 32;

                        iconBitmapImage.DecodeFailed += IconBitmapImage_DownloadOrDecodeFailed;
                        iconBitmapImage.DownloadFailed += IconBitmapImage_DownloadOrDecodeFailed;
                        iconBitmapImage.DownloadCompleted += IconBitmapImage_DownloadCompleted;

                        try
                        {
                            iconBitmapImage.EndInit();
                        }
                        // if the URL is a file: URI (which actually happened!), we'll get an exception.
                        // if the URL is a file: URI which is in an existing directory, but the file doesn't exist, we'll fail silently.
                        catch (Exception e) when (e is System.IO.DirectoryNotFoundException || e is System.Net.WebException)
                        {
                            iconBitmapImage.DecodeFailed -= IconBitmapImage_DownloadOrDecodeFailed;
                            iconBitmapImage.DownloadFailed -= IconBitmapImage_DownloadOrDecodeFailed;
                            iconBitmapImage.DownloadCompleted -= IconBitmapImage_DownloadCompleted;

                            iconBitmapImage = DefaultPackageIcon;
                        }
                        finally
                        {
                            // store this bitmapImage in the bitmap image cache, so that other occurances can reuse the BitmapImage
                            var policy = new CacheItemPolicy
                            {
                                AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10)
                            };
                            _bitmapImageCache.Set(IconUrl.ToString(), iconBitmapImage, policy);

                            // if we hit maxValue, reset both failures and loadattempts.
                            if (int.MaxValue > iconLoadAttempts)
                            {
                                iconLoadAttempts++;
                            }
                            else
                            {
                                iconLoadAttempts = 0;
                                iconFailures = 0;
                            }
                        }
                    }
                    else
                    {
                        var policy = new CacheItemPolicy
                        {
                            AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10)
                        };
                        _bitmapImageCache.Set(IconUrl.ToString(), DefaultPackageIcon, policy);
                    }
                }
            }

            return iconBitmapImage;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private void IconBitmapImage_DownloadCompleted(object sender, EventArgs e)
        {
            var bitmapImage = sender as BitmapImage;
            // unwire events
            bitmapImage.DecodeFailed -= IconBitmapImage_DownloadOrDecodeFailed;
            bitmapImage.DownloadFailed -= IconBitmapImage_DownloadOrDecodeFailed;
            bitmapImage.DownloadCompleted -= IconBitmapImage_DownloadCompleted;
        }

        private void IconBitmapImage_DownloadOrDecodeFailed(object sender, System.Windows.Media.ExceptionEventArgs e)
        {
            var bitmapImage = sender as BitmapImage;
            // unwire events
            bitmapImage.DecodeFailed -= IconBitmapImage_DownloadOrDecodeFailed;
            bitmapImage.DownloadFailed -= IconBitmapImage_DownloadOrDecodeFailed;
            bitmapImage.DownloadCompleted -= IconBitmapImage_DownloadCompleted;

            // show default package icon
            //IconBitmapImage = DefaultPackageIcon;

            // Fix the bitmap image cache to have default package icon, if some other failure didn't already do that.
            var iconBitmapImage = _bitmapImageCache.Get(bitmapImage.UriSource.ToString()) as BitmapImage;
            if (iconBitmapImage != DefaultPackageIcon)
            {
                var policy = new CacheItemPolicy
                {
                    AbsoluteExpiration = DateTimeOffset.Now.AddMinutes(10)
                };
                _bitmapImageCache.Set(bitmapImage.UriSource.ToString(), DefaultPackageIcon, policy);

                iconFailures++;
            }
        }
    }
}
