using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Tag;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>
    /// タグを入力するだけの欄 (保存しない。タグ名のリストをメモリに持つ)。取り込み画面・一括でタグを付ける画面の「付けるタグ」など。
    /// 選んだタグはスクリプトで TagField に足す (TagField.AddTag / SetTags)。候補の出し方は TagField と同じ。
    /// </summary>
    public class TagInputField : FieldBase<TagInputFieldDesign>
    {
        static readonly StringComparer _tagComparer = StringComparer.OrdinalIgnoreCase;

        readonly List<string> _tags = new();
        bool _modified;
        readonly TagCandidateProvider _candidates;

        public TagInputField(TagInputFieldDesign design) : base(design)
        {
            _candidates = TagCandidateProvider.Create(() => Services, design, () => null);
        }

        /// <summary>入力されたタグ (入れた順)。</summary>
        public List<string> Tags => _tags.ToList();

        /// <summary>そのタグが入っているか (大文字小文字は区別しない)。</summary>
        public bool HasTag(string tag) => _tags.Contains(tag.Trim(), _tagComparer);

        /// <summary>タグを足す (同じタグは重ねない。区切りを含めば分けて足す)。</summary>
        [ScriptName("AddTag")]
        public async Task AddTagAsync(string tag)
        {
            var changed = false;
            foreach (var name in TagField.Normalize(new[] { tag })) changed |= await AddOneAsync(name);
            if (changed) await AfterTagsChangedAsync();
        }

        /// <summary>タグを外す。</summary>
        [ScriptName("RemoveTag")]
        public async Task RemoveTagAsync(string tag)
        {
            if (_tags.RemoveAll(e => _tagComparer.Equals(e, tag.Trim())) > 0) await AfterTagsChangedAsync();
        }

        /// <summary>タグを置き換える (入力欄・スクリプトから)。</summary>
        [ScriptName("SetTags")]
        public async Task SetTagsAsync(List<string> tags)
        {
            var wanted = TagField.Normalize(tags);
            var changed = _tags.RemoveAll(e => !wanted.Contains(e, _tagComparer)) > 0;
            foreach (var name in wanted) changed |= await AddOneAsync(name);
            if (changed) await AfterTagsChangedAsync();
        }

        /// <summary>タグを全部外す。</summary>
        [ScriptName("Clear")]
        public async Task ClearAsync()
        {
            if (_tags.Count == 0) return;
            _tags.Clear();
            await AfterTagsChangedAsync();
        }

        async Task<bool> AddOneAsync(string name)
        {
            if (name.Length == 0 || HasTag(name)) return false;
            //表記は読んである候補から寄せる (新しいタグを入れてよいなら問い合わせない。保存しないので、保存する TagField の側で寄せる)。
            //決まったタグだけなら、候補に無いタグは入れない (問い合わせて確かめる)
            var spelling = await _candidates.FindSpellingAsync(name, query: !Design.AllowNewTags);
            if (spelling == null && !Design.AllowNewTags)
            {
                SetError(string.Format(Properties.Resources.TagFieldUnknownTagFormat, name));
                NotifyStateChanged();
                return false;
            }
            _tags.Add(spelling ?? name);
            _modified = true;
            return true;
        }

        async Task AfterTagsChangedAsync()
        {
            ClearError();
            await Module.ExecuteScriptAsync(Design.OnDataChanged);
            NotifyStateChanged();
        }

        /// <summary>打った文字を含む候補 (よく使われている順)。</summary>
        [ScriptHide]
        public Task<List<string>> GetCandidatesAsync(string text) => _candidates.SuggestAsync(text);

        [ScriptHide]
        public override bool IsModified => _modified;

        [ScriptHide]
        public override Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            _tags.Clear();
            _modified = false;
            return Task.CompletedTask;
        }

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override void AcceptChanges(SubmitAcceptInfo info) => _modified = false;

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && _tags.Count == 0)
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return await base.ValidateInput();
        }
    }
}
