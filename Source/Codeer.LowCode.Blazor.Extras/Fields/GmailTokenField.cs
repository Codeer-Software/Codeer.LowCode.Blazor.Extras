using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// Gmail トークン保存フィールドのランタイム。列は書き込み専用なので**値は読み出せない**。
    /// 入力欄は毎回空から始まり、**空のまま保存すれば既存トークンは維持**される
    /// (パスワード変更欄と同じ挙動)。入力された平文の暗号化はサーバー側 (GmailTokenHelper)。
    /// </summary>
    public class GmailTokenField(GmailTokenFieldDesign design) : FieldBase<GmailTokenFieldDesign>(design)
    {
        string _input = string.Empty;
        bool _isCleared;

        /// <summary>入力中のトークン (未保存)。保存が通れば空に戻る。</summary>
        internal string Input => _input;

        /// <summary>「登録を解除」が選ばれているか (保存すると列が空になる)。</summary>
        internal bool IsCleared => _isCleared;

        internal void SetInput(string? token)
        {
            _input = token ?? string.Empty;
            if (_input.Length != 0) _isCleared = false;
            NotifyStateChanged();
        }

        internal void SetCleared(bool isCleared)
        {
            _isCleared = isCleared;
            if (isCleared) _input = string.Empty;
            NotifyStateChanged();
        }

        [ScriptHide]
        public override bool IsModified => !string.IsNullOrEmpty(_input) || _isCleared;

        //列は書き込み専用 = クライアントには値が来ない
        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? data)
        {
            _input = string.Empty;
            _isCleared = false;
            await Task.CompletedTask;
        }

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? data) => await Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData()
            => IsModified
                ? new FieldSubmitData { FieldData = new GmailTokenFieldData { RefreshToken = _isCleared ? string.Empty : _input } }
                : new();

        [ScriptHide]
        public override void AcceptChanges(SubmitAcceptInfo info)
        {
            if (!info.TryGetSubmittedData<GmailTokenFieldData>(this, out _)) return;
            _input = string.Empty;
            _isCleared = false;
        }
    }
}
