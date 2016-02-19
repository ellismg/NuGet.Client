using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using NuGet.Packaging.Core;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.Protocol.Core.v3.Tests
{
    public class HttpStreamValidationTests
    {
        private const string Uri = "http://example/foo/bar";

        [Fact]
        public void HttpStreamValidation_ValidateJObject_RejectsBrokenJsonObjects()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"
                {
                    ""foo"": 1,
                    ""bar"": 2"));

            // Act & Assert
            var actual = Assert.Throws<InvalidDataException>(() =>
            {
                HttpStreamValidation.ValidateJObject(Uri, stream, o => { });
            });

            Assert.IsType<JsonReaderException>(actual.InnerException);
        }

        [Fact]
        public void HttpStreamValidation_ValidateJObject_RejectsJsonArray()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes("[1, 2]"));

            // Act & Assert
            var actual = Assert.Throws<InvalidDataException>(() =>
            {
                HttpStreamValidation.ValidateJObject(Uri, stream, o => { });
            });

            Assert.IsType<JsonReaderException>(actual.InnerException);
        }

        [Fact]
        public void HttpStreamValidation_ValidateJObject_RejectsFailedTest()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"
                {
                    ""foo"": 1,
                    ""bar"": 2
                }"));
            var innerException = new FormatException("bad!");

            // Act & Assert
            var actual = Assert.Throws<InvalidDataException>(() =>
            {
                HttpStreamValidation.ValidateJObject(
                    Uri,
                    stream,
                    o =>
                    {
                        if (o["foo"].ToObject<int>() != 9001)
                        {
                            throw innerException;
                        }
                    });
            });

            Assert.Same(innerException, actual.InnerException);
        }

        [Fact]
        public void HttpStreamValidation_ValidateJObject_AcceptsMinimal()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"
                {
                    ""foo"": 1,
                    ""bar"": 2
                }"));

            // Act & Assert
            HttpStreamValidation.ValidateJObject(Uri, stream, o => { });
        }

        [Fact]
        public void HttpStreamValidation_ValidateFlatContainerIndex_RejectsInvalidVersions()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"
                {
                    ""versions"": false
                }"));

            // Act & Assert
            var actual = Assert.Throws<InvalidDataException>(() =>
            {
                HttpStreamValidation.ValidateFlatContainerIndex(Uri, stream);
            });

            Assert.Null(actual.InnerException);
        }

        [Fact]
        public void HttpStreamValidation_ValidateFlatContainerIndex_AcceptsMinimal()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"
                {
                    ""versions"": []
                }"));

            // Act & Assert
            HttpStreamValidation.ValidateFlatContainerIndex(Uri, stream);
        }

        [Fact]
        public void HttpStreamValidation_ValidateFlatContainerIndex_AcceptsMissingVersions()
        {
            // Arrange
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"
                {
                }"));

            // Act & Assert
            HttpStreamValidation.ValidateFlatContainerIndex(Uri, stream);
        }

        [Fact]
        public void HttpStreamValidation_ValidateNupkg_RejectsInvalidZipNupkg()
        {
            // Arrange
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes("not a zip!")))
            {
                // Act & Assert
                var actual = Assert.Throws<InvalidDataException>(() =>
                {
                    HttpStreamValidation.ValidateNupkg(
                        Uri,
                        stream);
                });

                Assert.IsType<InvalidDataException>(actual.InnerException);
            }
        }

        [Fact]
        public void HttpStreamValidation_ValidateNupkg_RejectsMissingNuspecNupkg()
        {
            // Arrange
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    zip.AddEntry("assembly.dll", new byte[0]);
                }

                stream.Seek(0, SeekOrigin.Begin);

                // Act & Assert
                var actual = Assert.Throws<InvalidDataException>(() =>
                {
                    HttpStreamValidation.ValidateNupkg(
                        Uri,
                        stream);
                });

                Assert.IsType<PackagingException>(actual.InnerException);
            }
        }

        [Fact]
        public void HttpStreamValidation_ValidateNupkg_AcceptsMinimalNupkg()
        {
            // Arrange
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    zip.AddEntry("package.nuspec", new byte[0]);
                }

                stream.Seek(0, SeekOrigin.Begin);

                // Act & Assert
                HttpStreamValidation.ValidateNupkg(
                    Uri,
                    stream);
            }
        }

        [Fact]
        public void HttpStreamValidation_ValidateXml_RejectsBroken()
        {
            // Arrange
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""utf-8""?>
                <entry>
                </ent
                ")))
            {
                // Act & Assert
                var actual = Assert.Throws<InvalidDataException>(() =>
                {
                    HttpStreamValidation.ValidateXml(
                        Uri,
                        stream);
                });

                Assert.IsType<XmlException>(actual.InnerException);
            }
        }

        [Fact]
        public void HttpStreamValidation_ValidateXml_AcceptsMinimal()
        {
            // Arrange
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes(@"<?xml version=""1.0"" encoding=""utf-8""?>
                <entry>
                </entry>
                ")))
            {
                // Act & Assert
                HttpStreamValidation.ValidateXml(
                    Uri,
                    stream);
            }
        }
    }
}
