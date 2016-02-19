using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;

namespace NuGet.Protocol.Core.v3
{
    public static class HttpStreamValidation
    {
        public static void ValidateJObject(string uri, Stream stream, Action<JObject> validate)
        {
            try
            {
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, true))
                using (var jsonReader = new JsonTextReader(reader) { CloseInput = false })
                {
                    var jObject = JObject.Load(jsonReader);
                    validate(jObject);
                }
            }
            catch (Exception e) when (!(e is InvalidDataException))
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Protocol_InvalidJsonObject,
                    uri);

                throw new InvalidDataException(message, e);
            }
        }
        public static void ValidateJObject(string uri, Stream stream)
        {
            ValidateJObject(uri, stream, o => { });
        }

        public static void ValidateNupkg(string uri, Stream stream)
        {
            try
            {
                using (var reader = new PackageArchiveReader(stream, leaveStreamOpen: true))
                using (reader.GetNuspec())
                {
                }
            }
            catch (Exception e)
            {
                string message = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Log_InvalidNupkgFromUrl,
                    uri);

                throw new InvalidDataException(message, e);
            }
        }

        public static void ValidateServiceIndex(string uri, Stream stream)
        {
            ValidateJObject(
                uri,
                stream,
                jObject =>
                {
                    var resources = jObject["resources"];
                    if (resources == null)
                    {
                        return;
                    }

                    if (!(resources is JArray))
                    {
                        string message = string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Protocol_FlatContainerIndexVersionsNotArray,
                            uri);

                        throw new InvalidDataException(message);
                    }
                });
        }

        public static void ValidateFlatContainerIndex(string uri, Stream stream)
        {
            ValidateJObject(
                uri,
                stream,
                jObject =>
                {
                    var versions = jObject["versions"];
                    if (versions == null)
                    {
                        return;
                    }

                    if (!(versions is JArray))
                    {
                        string message = string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Protocol_FlatContainerIndexVersionsNotArray,
                            uri);

                        throw new InvalidDataException(message);
                    }
                });
        }

        public static void ValidateXml(string uri, Stream stream)
        {
            try
            {
                XDocument.Load(stream);
            }
            catch (Exception e)
            {
                var message = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Protocol_InvalidXml,
                    uri);

                throw new InvalidDataException(message, e);
            }
        }
    }
}
