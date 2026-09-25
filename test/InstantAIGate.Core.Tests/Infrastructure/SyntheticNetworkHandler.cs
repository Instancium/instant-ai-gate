namespace InstantAIGate.Core.Tests.Infrastructure;

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Simulates a remote HTTP server with Range-request support for isolated, high-speed unit testing.
/// </summary>
public class SyntheticNetworkHandler : HttpMessageHandler
{
    private readonly long _virtualFileSize;
    private readonly byte[] _magicHeader;
    private readonly bool _supportRanges;

    // 1. Constructor only initializes state. No 'request' here.
    public SyntheticNetworkHandler(long virtualFileSizeBytes = 1024 * 1024 * 50, bool supportRanges = true)
    {
        _virtualFileSize = virtualFileSizeBytes;
        _supportRanges = supportRanges;
        _magicHeader = new byte[] { 0x47, 0x47, 0x55, 0x46 }; // "GGUF"
    }

    // 2. request is passed here via HttpClient pipeline
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(5, cancellationToken);

        // 3. Handle HEAD requests (Pre-flight checks)
        if (request.Method == HttpMethod.Head)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new ByteArrayContent(Array.Empty<byte>());
            response.Content.Headers.ContentLength = _virtualFileSize;

            if (_supportRanges)
            {
                response.Headers.AcceptRanges.Add("bytes");
            }

            return response;
        }

        // 4. Handle GET requests (Actual data transfer)
        if (request.Method == HttpMethod.Get)
        {
            long start = 0;
            long end = _virtualFileSize - 1;
            bool isPartial = false;

            if (request.Headers.Range != null && request.Headers.Range.Ranges.Count > 0)
            {
                var range = request.Headers.Range.Ranges.First();
                start = range.From ?? 0;
                end = range.To ?? (_virtualFileSize - 1);
                isPartial = true;
            }

            long length = end - start + 1;
            var virtualStream = new VirtualZeroStream(length, start == 0 ? _magicHeader : null);

            var response = new HttpResponseMessage(isPartial ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            {
                Content = new StreamContent(virtualStream)
            };

            response.Content.Headers.ContentLength = length;
            if (isPartial)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(start, end, _virtualFileSize);
            }

            return response;
        }

        return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
    }

    private class VirtualZeroStream : Stream
    {
        private long _position;
        private readonly long _length;
        private readonly byte[]? _headerToInject;

        public VirtualZeroStream(long length, byte[]? headerToInject = null)
        {
            _length = length;
            _headerToInject = headerToInject;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _length) return 0;

            int bytesToRead = (int)Math.Min(count, _length - _position);
            Array.Clear(buffer, offset, bytesToRead);

            if (_position == 0 && _headerToInject != null && bytesToRead >= _headerToInject.Length)
            {
                Buffer.BlockCopy(_headerToInject, 0, buffer, offset, _headerToInject.Length);
            }

            _position += bytesToRead;
            return bytesToRead;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}