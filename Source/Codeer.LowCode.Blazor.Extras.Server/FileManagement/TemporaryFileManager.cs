using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.SystemSettings;

namespace Codeer.LowCode.Blazor.Extras.Server.FileManagement
{
    public class TemporaryFileManager : ITemporaryFileManager
    {
        readonly IDbAccessor _dbAccessor;
        readonly TemporaryFileTableInfo[] _temporaryFilesManagements;
        readonly IReadOnlyList<IFileStorage> _fileStorages;
        readonly IAuthenticationContext? _authentication;

        class SqlParts
        {
            DataSourceType _type;
            public SqlParts(DataSourceType type) => _type = type;
            public string Blanket(string x) => _type == DataSourceType.SQLServer ? $"[{x}]" :
                                                _type == DataSourceType.MySQL ? $"`{x}`" : $"\"{x}\"";
        }

        public TemporaryFileManager(IDbAccessor db, TemporaryFileTableInfo[] temporaryFilesManagements, IEnumerable<IFileStorage> fileStorages)
            : this(db, temporaryFilesManagements, fileStorages, null) { }

        public TemporaryFileManager(IDbAccessor db, TemporaryFileTableInfo[] temporaryFilesManagements, FileStorage[] fileStorages)
            : this(db, temporaryFilesManagements, fileStorages.ToFileStorages(), null) { }

        //authentication: 所有者列 (TemporaryFileTableInfo.OwnerColumn) に記録する今のユーザー
        public TemporaryFileManager(IDbAccessor db, TemporaryFileTableInfo[] temporaryFilesManagements, IEnumerable<IFileStorage> fileStorages, IAuthenticationContext? authentication)
        {
            _dbAccessor = db;
            _temporaryFilesManagements = temporaryFilesManagements;
            _fileStorages = fileStorages.ToList();
            _authentication = authentication;
        }

        public TemporaryFileManager(IDbAccessor db, TemporaryFileTableInfo[] temporaryFilesManagements, FileStorage[] fileStorages, IAuthenticationContext? authentication)
            : this(db, temporaryFilesManagements, fileStorages.ToFileStorages(), authentication) { }

        async Task<string?> GetOwnerAsync()
        {
            if (_authentication == null) return null;
            var id = await _authentication.GetCurrentUserIdAsync();
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public async Task ToTemporaryFile(string dataSourceName, Guid guid)
        {
            var dataSource = _dbAccessor.GetDataSource(dataSourceName);
            if (dataSource == null) return;
            var parts = new SqlParts(dataSource.DataSourceType);
            var parameterPrefix = dataSource.DataSourceType == DataSourceType.Oracle ? ":p" : "@p";

            var dateTime = DateTime.UtcNow;
            dateTime = new DateTime(dateTime.Year, dateTime.Month, dateTime.Day, dateTime.Hour, dateTime.Minute, dateTime.Second, dateTime.Millisecond);
            var mgr = _temporaryFilesManagements.FirstOrDefault(e => e.DataSourceName == dataSourceName);
            if (mgr == null) throw LowCodeException.Create($"{dataSourceName} No file management settings");

            //Make it a temporary file and wait for it to be deleted
            var p1 = parameterPrefix + 1;
            var p2 = parameterPrefix + 2;
            var parameters = new Dictionary<string, object?> { { p1, guid }, { p2, dateTime } };
            var sql = $"insert into {parts.Blanket(mgr.Table)}({parts.Blanket(mgr.GuidColumn)},{parts.Blanket(mgr.CreatedDateTimeColumn)}) values({p1}, {p2})";
            if (!string.IsNullOrEmpty(mgr.OwnerColumn))
            {
                var p3 = parameterPrefix + 3;
                parameters[p3] = await GetOwnerAsync();
                sql = $"insert into {parts.Blanket(mgr.Table)}({parts.Blanket(mgr.GuidColumn)},{parts.Blanket(mgr.CreatedDateTimeColumn)},{parts.Blanket(mgr.OwnerColumn)}) values({p1}, {p2}, {p3})";
            }
            await _dbAccessor.ExecuteAsync(dataSourceName, sql, parameters);
        }

        public async Task FixFile(string dataSourceName, Guid? guid)
        {
            var dataSource = _dbAccessor.GetDataSource(dataSourceName);
            if (dataSource == null) return;
            var parts = new SqlParts(dataSource.DataSourceType);
            var parameterPrefix = dataSource.DataSourceType == DataSourceType.Oracle ? ":p" : "@p";

            var mgr = _temporaryFilesManagements.FirstOrDefault(e => e.DataSourceName == dataSourceName);
            if (mgr == null) throw LowCodeException.Create($"{dataSourceName} No file management settings");
            var p1 = parameterPrefix + 1;
            var sql = $"delete from {parts.Blanket(mgr.Table)} where {parts.Blanket(mgr.GuidColumn)}={p1}";
            await _dbAccessor.ExecuteAsync(dataSourceName, sql, new Dictionary<string, object?> { { p1, guid } });
        }

        public async Task<bool> IsTemporaryFileAsync(string dataSourceName, Guid guid)
        {
            var dataSource = _dbAccessor.GetDataSource(dataSourceName);
            if (dataSource == null) return false;
            var parts = new SqlParts(dataSource.DataSourceType);
            var parameterPrefix = dataSource.DataSourceType == DataSourceType.Oracle ? ":p" : "@p";

            var mgr = _temporaryFilesManagements.FirstOrDefault(e => e.DataSourceName == dataSourceName);
            if (mgr == null) throw LowCodeException.Create($"{dataSourceName} No file management settings");
            var p1 = parameterPrefix + 1;
            var sql = $"select count(*) from {parts.Blanket(mgr.Table)} where {parts.Blanket(mgr.GuidColumn)}={p1}";
            var parameters = new Dictionary<string, ParamAndRawDbTypeName> { { p1, new ParamAndRawDbTypeName { Value = guid } } };
            if (!string.IsNullOrEmpty(mgr.OwnerColumn))
            {
                //アップロードした人だけが使える (ユーザーの無いホストでは所有者 null 同士で一致)
                var owner = await GetOwnerAsync();
                if (owner == null)
                {
                    sql += $" and ({parts.Blanket(mgr.OwnerColumn)} is null)";
                }
                else
                {
                    var p2 = parameterPrefix + 2;
                    sql += $" and ({parts.Blanket(mgr.OwnerColumn)}={p2})";
                    parameters[p2] = new ParamAndRawDbTypeName { Value = owner };
                }
            }
            var rows = await _dbAccessor.QueryAsync(dataSourceName, sql, parameters);
            return rows.Count != 0 && Convert.ToInt64(rows[0].Values.First()) != 0;
        }

        public async Task<Guid> CopyFileAsync(FileSaveInfo info, Guid source)
        {
            var storage = _fileStorages.Find(info.StorageName);
            var guid = Guid.NewGuid();
            await ToTemporaryFile(info.DataSourceName, guid);
            try
            {
                using var content = await storage.OpenReadAsync(source);
                await storage.WriteAsync(guid, content);
            }
            catch
            {
                //途中で失敗: 途中までの実体と一時行を残さない (AddFileAsync と同じ)
                var deleted = false;
                try { await storage.DeleteAsync(guid); deleted = true; } catch { }
                if (deleted) try { await RemoveTmpFiles(info.DataSourceName, [guid]); } catch { }
                throw;
            }
            return guid;
        }

        public async Task<DataIO.FileInfo> AddFileAsync(FileSaveInfo info, string? fileName, Stream stream)
        {
            var data = new DataIO.FileInfo
            {
                FileName = fileName,
                FileGuid = Guid.NewGuid(),
            };

            //本文を読みながら置き場所へ書く (全部をメモリに置かない。大きさは流しながら数える)
            var storage = _fileStorages.Find(info.StorageName);
            var counting = new CountingReadStream(stream);
            await ToTemporaryFile(info.DataSourceName, data.FileGuid.Value);
            try
            {
                await storage.WriteAsync(data.FileGuid.Value, counting);
            }
            catch
            {
                //途中で切れた・上限超え・置き場所の障害: 途中までの実体と一時行を残さない。
                //行は実体を消せたときだけ消す (実体が残るなら行も残し、1 日後の掃除に拾わせる。行の無い実体は掃除されない)
                var deleted = false;
                try { await storage.DeleteAsync(data.FileGuid.Value); deleted = true; } catch { }
                if (deleted) try { await RemoveTmpFiles(info.DataSourceName, [data.FileGuid.Value]); } catch { }
                throw;
            }
            data.FileSize = counting.BytesRead;
            await DeleteTmpFiles(info.DataSourceName, info.StorageName);
            return data;
        }

        async Task DeleteTmpFiles(string dataSourceName, string storageName)
        {
            //You can delete up to 10 items at a time.
            var oldFiles = (await GetOldTemporaryFiles(dataSourceName)).Take(10).ToArray();
            await StorageAccess.DeleteFiles(_fileStorages, storageName, oldFiles);
            await RemoveTmpFiles(dataSourceName, oldFiles);
        }

        async Task<Guid[]> GetOldTemporaryFiles(string dataSourceName)
        {
            var dataSource = _dbAccessor.GetDataSource(dataSourceName);
            if (dataSource == null) return [];
            var parts = new SqlParts(dataSource.DataSourceType);
            var parameterPrefix = dataSource.DataSourceType == DataSourceType.Oracle ? ":p" : "@p";

            var mgr = _temporaryFilesManagements.FirstOrDefault(e => e.DataSourceName == dataSourceName);
            if (mgr == null) throw LowCodeException.Create($"{dataSourceName} No file management settings");

            var old = DateTime.UtcNow.AddDays(-1);
            old = new DateTime(old.Year, old.Month, old.Day, old.Hour, old.Minute, old.Second, old.Millisecond);

            var p1 = parameterPrefix + 1;
            var sql = $"select {parts.Blanket(mgr.GuidColumn)} from {parts.Blanket(mgr.Table)} where {parts.Blanket(mgr.CreatedDateTimeColumn)} < {p1}";
            var list = new List<Guid>();
            foreach (var e in await _dbAccessor.QueryAsync(dataSourceName, sql, new() { { p1, new ParamAndRawDbTypeName { Value = old } } }))
            {
                if (Guid.TryParse(e[mgr.GuidColumn].ToString(), out var guid)) list.Add(guid);
            }
            return list.ToArray();
        }

        async Task RemoveTmpFiles(string dataSourceName, Guid[] oldFiles)
        {
            var dataSource = _dbAccessor.GetDataSource(dataSourceName);
            if (dataSource == null) return;
            var parts = new SqlParts(dataSource.DataSourceType);
            var parameterPrefix = dataSource.DataSourceType == DataSourceType.Oracle ? ":p" : "@p";

            if (!oldFiles.Any()) return;

            var mgr = _temporaryFilesManagements.FirstOrDefault(e => e.DataSourceName == dataSourceName);
            if (mgr == null) throw LowCodeException.Create($"{dataSourceName} No file management settings");

            var dic = oldFiles.Select((e, i) => new { key = $"{parameterPrefix}{i}", value = e }).ToDictionary(e => e.key, e => (object?)e.value);
            var sql = $"delete from {parts.Blanket(mgr.Table)} where {parts.Blanket(mgr.GuidColumn)} in({string.Join(",", dic.Keys)})";
            await _dbAccessor.ExecuteAsync(dataSourceName, sql, dic);
        }
    }
}
