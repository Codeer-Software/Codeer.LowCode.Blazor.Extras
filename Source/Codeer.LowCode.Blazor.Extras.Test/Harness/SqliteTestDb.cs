using Microsoft.Data.Sqlite;

namespace Codeer.LowCode.Blazor.Extras.Test.Harness
{
    /// <summary>テストで作った SQLite の DB ファイルの後始末。</summary>
    internal static class SqliteTestDb
    {
        /// <summary>
        /// 接続のプールを空にして DB ファイルを消す。
        /// 接続を全部閉じていても、Windows では閉じた直後のファイルを別のプロセス (ウイルス対策・インデクサ) が一瞬開いていることがあり、
        /// File.Delete が「使用中」で失敗する。短く待って繰り返す (繰り返しても消せなければ例外のまま = 本当に掴んでいるものがある)。
        /// </summary>
        public static void Delete(string path)
        {
            SqliteConnection.ClearAllPools();
            for (var i = 0; ; i++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch (IOException) when (i < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
