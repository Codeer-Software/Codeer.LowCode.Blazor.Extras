namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    /// <summary>
    /// 「位置を指定して末尾まで読む」しかできない置き場所 (S3 の GetObject の ByteRange 等) を、シーク可能な読み取り専用の Stream に見せる。
    /// 応答の Range 処理 (ASP.NET Core の FileStreamResult) は Seek してから連続して Read するので、Seek で位置が変わった次の Read だけ開き直し、
    /// 連続する Read は開いたままの本体を読み進める (Read ごとに要求を出さない)。
    /// </summary>
    public sealed class RangeReadStream : Stream
    {
        readonly Func<long, Task<Stream>> _openFrom;
        readonly IDisposable? _owner;
        readonly long _length;
        long _position;
        Stream? _body;
        long _bodyPosition;

        /// <param name="length">全体の長さ (Range の計算に要る)。</param>
        /// <param name="openFrom">その位置から末尾までを読む Stream を開く。</param>
        /// <param name="owner">この Stream と同じ寿命で閉じるもの (接続のクライアント等)。</param>
        public RangeReadStream(long length, Func<long, Task<Stream>> openFrom, IDisposable? owner = null)
        {
            _length = length;
            _openFrom = openFrom;
            _owner = owner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                _position = value;
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0 || _position >= _length) return 0;
            if (_body == null || _bodyPosition != _position)
            {
                await CloseBodyAsync();
                _body = await _openFrom(_position);
                _bodyPosition = _position;
            }
            var read = await _body.ReadAsync(buffer, cancellationToken);
            _position += read;
            _bodyPosition += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            return _position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        async ValueTask CloseBodyAsync()
        {
            if (_body == null) return;
            await _body.DisposeAsync();
            _body = null;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _body?.Dispose();
                _body = null;
                _owner?.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await CloseBodyAsync();
            _owner?.Dispose();
            await base.DisposeAsync();
        }
    }
}
