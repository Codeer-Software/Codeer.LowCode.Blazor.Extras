using Codeer.LowCode.Blazor.DataLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class RichTextField(RichTextFieldDesign design)
        : ValueFieldBase<RichTextFieldDesign, RichTextFieldData, string>(design)
    {
        /// <summary>候補に出す数。</summary>
        internal const int MentionCandidateLimit = 10;

        /// <summary>打った文字の長さの上限 (画面の richtext-interop.js の MAX_MENTION_QUERY と同じ)。超えたら候補なし。</summary>
        internal const int MaxMentionTextLength = 50;

        //候補の一覧を前回読んだときの検索条件 (編集中のレコードの値・CurrentUser.〜 を入れたもの)。変わっていれば読み直す
        string? _mentionListCondition;
        //候補の一覧を読んでいる最中の処理 (読んでいる間に来た問い合わせは、これが終わるのを待つ)
        Task? _mentionListLoading;

        /// <summary>今の値に入っているメンションの人の Id (重複を除き、本文の順)。</summary>
        public string[] MentionedIds => MentionHtml.GetMentionedIds(Value);

        /// <summary>今の値に「全員」のメンションが入っているか。</summary>
        public bool MentionsAll => MentionHtml.MentionsAll(Value);

        [ScriptHide]
        public override async Task<bool> ValidateInput()
        {
            if (Design.IsRequired && string.IsNullOrWhiteSpace(Value))
            {
                SetError(Properties.Resources.InputError);
                return false;
            }
            return await base.ValidateInput();
        }

        /// <summary>
        /// 打った文字に合うメンションの候補。候補は <see cref="RichTextFieldDesign.MentionListField"/> の一覧の行で、サーバーの専用の処理は無い。
        /// 一覧は最初の問い合わせと、一覧の検索条件が前回と変わったときだけ読む (打つたびには読まない)。絞り込みは手元の行に当てる。
        /// メンションを使わない設定・デザインモード・一覧が無い・一覧が読み込みを止めている (AllowLoad が false) ときは候補なし。
        /// </summary>
        [ScriptHide]
        public async Task<MentionCandidates> GetMentionCandidatesAsync(string text)
        {
            var result = new MentionCandidates();
            if (!Design.EnableMention || Services.AppInfoService.IsDesignMode) return result;
            text ??= string.Empty;
            if (text.Length > MaxMentionTextLength) return result;

            var list = await LoadMentionListAsync();
            if (list == null) return result;

            foreach (var row in list.Rows)
            {
                var candidate = ToCandidate(row);
                if (candidate == null) continue;
                result.HasPeople = true;
                if (result.Items.Count == MentionCandidateLimit) continue;
                if (text.Length > 0 && !Contains(candidate.DisplayName, text) && !Contains(candidate.LoginName, text)) continue;
                result.Items.Add(candidate);
            }
            return result;

            static bool Contains(string value, string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        //候補の一覧。レイアウトに置いていない一覧は本体が自分では読まないので、ここで Reload を呼んで読ませる
        //(本体の従属レコードの読み込み IOwnedRecordsField.LoadOwnedRecordsAsync と同じ形)
        async Task<ListField?> LoadMentionListAsync()
        {
            //読んでいる間に来た問い合わせは、読み終わるのを待ってから条件を比べる (同じ条件を二重に読まない)
            while (_mentionListLoading != null)
            {
                try { await _mentionListLoading; } catch { }
            }

            var list = Module.GetField<ListField>(Design.MentionListField);
            if (list == null || list.Design is not ListFieldDesign) return null;

            //読み込みを止めた一覧 (AllowLoad が false) とデータソースの無いモジュールの一覧は、本体が検索条件を返さない
            var condition = list.GetSearchCondition();
            if (condition == null) return null;

            var key = JsonConverterEx.SerializeObject(condition, false);
            if (key == _mentionListCondition) return list;

            //未保存のレコードの仮 Id が条件に入っているとき・一覧のモジュールを読めない人のときは、本体が読まずに済ませる (行は空のまま)
            var loading = list.ReloadAsync();
            _mentionListLoading = loading;
            try
            {
                await loading;
                _mentionListCondition = key;
            }
            finally
            {
                _mentionListLoading = null;
            }
            return list;
        }

        //一覧の行を候補にする。Id の無い行と、表示名もログイン ID も空の行は候補にしない
        MentionCandidate? ToCandidate(Module row)
        {
            var id = (row.GetFields().FirstOrDefault(e => e.Design is IdFieldDesign)?.GetData() as IdFieldData)?.Value;
            if (string.IsNullOrEmpty(id) || IdFieldData.IsTemporaryId(id)) return null;
            var loginName = Text(row, Design.MentionLoginNameField);
            var displayName = Text(row, Design.MentionDisplayNameField);
            if (displayName.Length == 0) displayName = loginName;
            if (displayName.Length == 0) return null;
            return new MentionCandidate { Id = id, DisplayName = displayName, LoginName = loginName };
        }

        //行の項目の値 (行のレイアウトに置いていない項目は一覧が読まないので空)
        static string Text(Module row, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return string.Empty;
            var data = row.GetField(fieldName)?.GetData();
            if (data == null) return string.Empty;
            if (data is TextFieldData text) return text.Value ?? string.Empty;
            var holder = new ModuleData();
            holder.Fields[fieldName] = data;
            return holder.TryGetVariableValue($"{fieldName}.Value", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        }
    }
}
