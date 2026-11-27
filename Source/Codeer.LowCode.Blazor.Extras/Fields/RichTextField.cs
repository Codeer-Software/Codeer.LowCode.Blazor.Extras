using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataLogic;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
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

        //候補を前回読んだときの一覧の検索条件 (編集中のレコードの値・CurrentUser.〜 を入れたもの)。変わっていれば読み直す
        string? _mentionCondition;
        //読んだ候補 (一覧の並びのまま。絞り込む前の全員)。一覧フィールドには読ませず、ここに持つ (行ごとの Module は作らない)
        List<MentionCandidate> _mentionPeople = new();
        //候補を読んでいる最中の処理 (読んでいる間に来た問い合わせは、これが終わるのを待つ)
        Task? _mentionLoading;
        //読んでも行が無かった・読み込みが例外になったときの検索条件。同じ条件のままなら打つたびには読み直さず、@ を打ち直したとき (打った文字が空) にだけ読み直す。
        //行が無いときも含めるのは、Web のホストでは通信の失敗が例外にならず空の結果で返るため (失敗と「該当者なし」を見分けられない)
        string? _mentionFailedCondition;

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
        /// 打った文字に合うメンションの候補。候補は <see cref="RichTextFieldDesign.MentionListField"/> の一覧が読む行で、サーバーの専用の処理は無い。
        /// 一覧の検索条件 (と、一覧が読むモジュールの権限) だけを借りて、行のデータをここで読む。一覧フィールドには読ませない (行ごとの Module を作らない。一覧の行は空のまま)。
        /// 読むのは最初の問い合わせと、一覧の検索条件が前回と変わったときだけ (打つたびには読まない)。絞り込みは手元の候補に当てる。
        /// 読み込みに失敗したとき・読んでも行が無かったときは候補なし (例外は出さない)。同じ条件のままなら、次に読み直すのは @ を打ち直したとき。
        /// メンションを使わない (候補の一覧を指定していない)・デザインモード・一覧が無い・一覧が読み込みを止めている (AllowLoad が false) ときは候補なし。
        /// </summary>
        [ScriptHide]
        public async Task<MentionCandidates> GetMentionCandidatesAsync(string text)
        {
            var result = new MentionCandidates();
            if (!Design.UsesMention || Services.AppInfoService.IsDesignMode) return result;
            text ??= string.Empty;
            if (text.Length > MaxMentionTextLength) return result;

            var people = await LoadMentionPeopleAsync(retryFailed: text.Length == 0);
            result.HasPeople = people.Count > 0;
            foreach (var candidate in people)
            {
                if (text.Length > 0 && !Contains(candidate.DisplayName, text) && !Contains(candidate.LoginName, text)) continue;
                result.Items.Add(candidate);
                if (result.Items.Count == MentionCandidateLimit) break;
            }
            return result;

            static bool Contains(string value, string text) => value.Contains(text, StringComparison.OrdinalIgnoreCase);
        }

        //候補にする全員。読むかどうかの判定は本体の ListField.ReloadAsync と同じ (読めない人・仮 Id の条件では問い合わせない)
        async Task<IReadOnlyList<MentionCandidate>> LoadMentionPeopleAsync(bool retryFailed)
        {
            //読んでいる間に来た問い合わせは、読み終わるのを待ってから条件を比べる (同じ条件を二重に読まない)
            while (_mentionLoading != null)
            {
                try { await _mentionLoading; } catch { }
            }

            var list = Module.GetField<ListField>(Design.MentionListField);
            if (list == null || list.Design is not ListFieldDesign) return [];

            //読み込みを止めた一覧 (AllowLoad が false) とデータソースの無いモジュールの一覧は、本体が検索条件を返さない。
            //行のレイアウトの項目だけを読む指定は、この条件に入っている
            var condition = list.GetSearchCondition();
            if (condition == null) return [];

            var key = JsonConverterEx.SerializeObject(condition, false);
            if (key == _mentionCondition) return _mentionPeople;
            //前回この条件で読めなかった (失敗か、行が無かった)。打つたびには読み直さない
            if (key == _mentionFailedCondition && !retryFailed) return [];

            _mentionCondition = null;
            _mentionFailedCondition = null;
            _mentionPeople = new();

            //一覧のモジュールを読めない人は問い合わせない (サーバーに投げるとエラーになる)
            var listModule = Services.AppInfoService.GetDesignData().Modules.Find(condition.ModuleName);
            if (listModule == null || !listModule.HasUserReadPermission(Services)) return [];
            //未保存のレコードの仮 Id が条件に入っているときは読まない
            if (condition.GetFieldValueConditions().Any(e => IdFieldData.IsTemporaryId(e.Value.GetValue()?.ToString()))) return [];

            var loading = ReadMentionPeopleAsync(condition, listModule);
            _mentionLoading = loading;
            try
            {
                var people = await loading;
                //行が無いときは「読んだ」と覚えない (通信の失敗かもしれない)。@ を打ち直したときにもう一度読む
                if (people == null)
                {
                    _mentionFailedCondition = key;
                    return [];
                }
                _mentionPeople = people;
                _mentionCondition = key;
                return people;
            }
            catch (Exception)
            {
                //読み込みの失敗 (通信の失敗など) では候補を出さない。打っている最中の画面を例外で止めない
                _mentionFailedCondition = key;
                return [];
            }
            finally
            {
                _mentionLoading = null;
            }
        }

        //一覧の検索条件で行のデータを読み、候補にする。行が 1 つも返らなければ null
        async Task<List<MentionCandidate>?> ReadMentionPeopleAsync(SearchCondition condition, ModuleDesign listModule)
        {
            var page = (await Services.ModuleDataService.GetListAsync([new GetListRequest { Condition = condition, PageIndex = 0 }])).FirstOrDefault();
            if (page == null || page.Items.Count == 0) return null;

            var idField = listModule.Fields.OfType<IdFieldDesign>().FirstOrDefault()?.Name ?? string.Empty;
            var people = new List<MentionCandidate>();
            foreach (var row in page.Items)
            {
                var candidate = ToCandidate(row, idField);
                if (candidate != null) people.Add(candidate);
            }
            return people;
        }

        //行のデータを候補にする。Id の無い行と、表示名もログイン ID も空の行は候補にしない
        MentionCandidate? ToCandidate(ModuleData row, string idField)
        {
            var id = row.Fields.TryGetValue(idField, out var idData) ? (idData as IdFieldData)?.Value : null;
            if (string.IsNullOrEmpty(id) || IdFieldData.IsTemporaryId(id)) return null;
            var loginName = Text(row, Design.MentionLoginNameField);
            var displayName = Text(row, Design.MentionDisplayNameField);
            if (displayName.Length == 0) displayName = loginName;
            if (displayName.Length == 0) return null;
            return new MentionCandidate { Id = id, DisplayName = displayName, LoginName = loginName };
        }

        //行の項目の値 (行のレイアウトに置いていない項目は読まれないので空)
        static string Text(ModuleData row, string fieldName)
        {
            if (string.IsNullOrEmpty(fieldName)) return string.Empty;
            if (!row.Fields.TryGetValue(fieldName, out var data) || data == null) return string.Empty;
            if (data is TextFieldData text) return text.Value ?? string.Empty;
            return row.TryGetVariableValue($"{fieldName}.Value", out var value) ? value?.ToString() ?? string.Empty : string.Empty;
        }
    }
}
