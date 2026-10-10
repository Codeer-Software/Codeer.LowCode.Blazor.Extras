using Codeer.LowCode.Blazor.Extras.Server.FileManagement;

namespace Codeer.LowCode.Blazor.Extras.Test.FileStorage
{
    /// <summary>
    /// RangeReadStream: 位置指定でしか読めない置き場所 (S3) をシーク可能に見せる。
    /// 応答の Range 処理が行う「Seek → 連続 Read」で、開き直しが Seek の後の 1 回だけになることを固定する。
    /// </summary>
    public class RangeReadStreamTest
    {
        static readonly byte[] _source = Enumerable.Range(0, 100).Select(e => (byte)e).ToArray();

        class Opener : IDisposable
        {
            public readonly List<long> Opened = new();
            public bool Disposed;
            public Task<Stream> OpenFrom(long position)
            {
                Opened.Add(position);
                return Task.FromResult<Stream>(new MemoryStream(_source, (int)position, _source.Length - (int)position, false));
            }
            public void Dispose() => Disposed = true;
        }

        static byte[] Read(Stream s, int count)
        {
            var buffer = new byte[count];
            var total = 0;
            while (total < count)
            {
                var n = s.Read(buffer, total, count - total);
                if (n == 0) break;
                total += n;
            }
            return buffer.Take(total).ToArray();
        }

        [Test]
        public void 先頭から連続して読む間は1回しか開かない()
        {
            var opener = new Opener();
            using var s = new RangeReadStream(_source.Length, opener.OpenFrom);
            Assert.That(s.CanSeek, Is.True);
            Assert.That(s.Length, Is.EqualTo(100));
            Assert.That(Read(s, 10), Is.EqualTo(_source.Take(10)));
            Assert.That(Read(s, 20), Is.EqualTo(_source.Skip(10).Take(20)));
            Assert.That(s.Position, Is.EqualTo(30));
            Assert.That(opener.Opened, Is.EqualTo(new long[] { 0 }));
        }

        [Test]
        public void Seekした次のReadだけ開き直す()
        {
            var opener = new Opener();
            using var s = new RangeReadStream(_source.Length, opener.OpenFrom);
            s.Seek(40, SeekOrigin.Begin);
            Assert.That(Read(s, 5), Is.EqualTo(_source.Skip(40).Take(5)));
            Assert.That(Read(s, 5), Is.EqualTo(_source.Skip(45).Take(5)));
            s.Position = 90;
            Assert.That(Read(s, 20), Is.EqualTo(_source.Skip(90)), "末尾を越える分は読める所まで");
            Assert.That(opener.Opened, Is.EqualTo(new long[] { 40, 90 }));
        }

        [Test]
        public void 同じ位置へのSeekは開き直さない()
        {
            var opener = new Opener();
            using var s = new RangeReadStream(_source.Length, opener.OpenFrom);
            Read(s, 10);
            s.Seek(10, SeekOrigin.Begin);
            Read(s, 10);
            Assert.That(opener.Opened, Is.EqualTo(new long[] { 0 }));
        }

        [Test]
        public async Task 末尾以降は開かずに0を返す()
        {
            var opener = new Opener();
            await using var s = new RangeReadStream(_source.Length, opener.OpenFrom);
            s.Seek(0, SeekOrigin.End);
            Assert.That(await s.ReadAsync(new byte[10]), Is.EqualTo(0));
            Assert.That(opener.Opened, Is.Empty);
        }

        [Test]
        public void 閉じると所有物も閉じる()
        {
            var opener = new Opener();
            var s = new RangeReadStream(_source.Length, opener.OpenFrom, opener);
            Read(s, 1);
            s.Dispose();
            Assert.That(opener.Disposed, Is.True);
        }

        [Test]
        public void 書き込みはできない()
        {
            using var s = new RangeReadStream(_source.Length, new Opener().OpenFrom);
            Assert.That(s.CanWrite, Is.False);
            Assert.Throws<NotSupportedException>(() => s.Write(new byte[1], 0, 1));
            Assert.Throws<NotSupportedException>(() => s.SetLength(1));
        }
    }
}
