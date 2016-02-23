using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NuGet.Configuration;
using Xunit;

namespace NuGet.Protocol.Core.v3.Tests
{
    public class DownloadUtilityTests
    {
        [Fact]
        public void DownloadUtility_ReadsEnvironmentVariable()
        {
            // Arrange
            var mock = new Mock<IEnvironmentVariableReader>();
            mock.Setup(x => x.GetEnvironmentVariable(It.IsAny<string>())).Returns("1");

            var target = new DownloadUtility { EnvironmentVariableReader = mock.Object };

            // Act
            var actual = target.DownloadTimeout;

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(1), actual);
            mock.Verify(x => x.GetEnvironmentVariable("nuget_download_timeout"), Times.Exactly(1));
        }

        [Fact]
        public void DownloadUtility_DefaultTimeout()
        {
            // Arrange
            var mock = new Mock<IEnvironmentVariableReader>();
            mock.Setup(x => x.GetEnvironmentVariable(It.IsAny<string>())).Returns(string.Empty);

            var target = new DownloadUtility { EnvironmentVariableReader = mock.Object };

            // Act
            var actual = target.DownloadTimeout;

            // Assert
            Assert.Equal(TimeSpan.FromMinutes(5), actual);
            mock.Verify(x => x.GetEnvironmentVariable("nuget_download_timeout"), Times.Exactly(1));
        }

        [Fact]
        public async Task DownloadUtility_TimesOut()
        {
            // Arrange
            var mock = new Mock<IEnvironmentVariableReader>();
            mock.Setup(x => x.GetEnvironmentVariable(It.IsAny<string>())).Returns("1");

            var target = new DownloadUtility { EnvironmentVariableReader = mock.Object };
            var content = "test content";
            var source = new SlowStream(new MemoryStream(Encoding.UTF8.GetBytes(content)))
            {
                DelayPerByte = TimeSpan.FromMinutes(250)
            };
            var destination = new MemoryStream();

            // Act & Assert
            var actual = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                await target.DownloadAsync(source, destination, "test", CancellationToken.None);
            });

            Assert.Equal("The download of 'test' took more than 1 second(s) and therefore timed out.", actual.Message);
        }

        private class SlowStream : Stream
        {
            private readonly Stream _innerStream;

            public SlowStream(Stream innerStream)
            {
                _innerStream = innerStream;
            }

            public TimeSpan DelayPerByte { get; set; }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var read = _innerStream.Read(buffer, offset, count);
                Thread.Sleep(new TimeSpan(DelayPerByte.Ticks * read));
                return read;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length
            {
                get
                {
                    throw new NotSupportedException();
                }
            }

            public override long Position
            {
                get
                {
                    throw new NotSupportedException();
                }
                set
                {
                    throw new NotSupportedException();
                }
            }
        }
    }
}
