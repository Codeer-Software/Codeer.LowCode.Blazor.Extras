using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Extras.WebApi;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Script;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// ボタン (またはスクリプトの Execute()) で、サーバーに外部の Web API の呼び出しを頼むフィールド。
    /// 接続先の URL と認証のキーはサーバーの設定 (接続先の名前 = ConnectionName) だけが持つ。メソッド・パスもサーバーがこのデザインから読む (クライアントからは送らない)。
    /// 保存はしない。データは持たない。実行の可否はサーバーがこのフィールドの書き込み権限で判定する (行を持たないので、ユーザーだけで確定する条件しか適用されない)。
    /// </summary>
    [Designer(DisplayName = "$WebApiField")]
    [ToolboxIcon(PackIconMaterialKind = "Api")]
    [AllowColorInheritance(false)]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class WebApiFieldDesign() : FieldDesignBase(typeof(WebApiFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeConnectionNameRequired = 1;
        private const int CodePathInvalid = 2;
        private const int CodeDataConditionsNotAppliedToWebApi = 3;
        private const int CodePermissionRowConditionsNotAppliedToWebApi = 4;
        private const int CodeJsonFieldNameInvalid = 5;

        /// <summary>接続先の名前 (サーバーの設定 WebApi.Connections の Name)。必須。</summary>
        [Designer(Index = 3, DisplayName = "$WebApiConnectionName")]
        public string ConnectionName { get; set; } = string.Empty;

        /// <summary>HTTP メソッド。</summary>
        [Designer(Index = 4, DisplayName = "$WebApiMethod")]
        public WebApiMethod Method { get; set; } = WebApiMethod.Get;

        /// <summary>接続先の BaseUrl からの相対パス。{名前} で引数の同じ名前の値を差し込める (例 users/{id}/orders)。</summary>
        [Designer(Index = 5, DisplayName = "$WebApiPath")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// スクリプトの Execute(args) で渡した JsonObject を送るとき、キー (入れ子・配列の中のオブジェクトも) の先頭 1 文字を大文字にする。
        /// false (既定) なら先頭 1 文字を小文字にする。2 文字目以降は変えない。JsonField から送る引数 (ArgumentsField) と応答は変えない。
        /// </summary>
        [Designer(Index = 8, DisplayName = "$WebApiUseUpperCamelCaseNames")]
        public bool UseUpperCamelCaseNames { get; set; }

        /// <summary>
        /// 引数なしで呼ぶとき (ボタン・スクリプトの Execute()) の引数にする、同じモジュールの JsonField の名前。
        /// その JsonField の GetJson() をキーの名前を変えずにそのまま送る (null なら {})。空なら {}。
        /// </summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$WebApiArgumentsField")]
        [TargetFieldType(Types = [typeof(JsonFieldDesign)])]
        public string ArgumentsField { get; set; } = string.Empty;

        /// <summary>成功した応答の本文 (空でなければ) をそのまま SetJson で入れる同じモジュールの JsonField の名前。形が合うかは JsonField が決める。空なら入れない。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$WebApiResultField")]
        [TargetFieldType(Types = [typeof(JsonFieldDesign)])]
        public string ResultField { get; set; } = string.Empty;

        /// <summary>ボタンを出す。false なら画面には何も出ず、スクリプトの Execute() からだけ呼ぶ。</summary>
        [Designer(Index = 9, DisplayName = "$WebApiShowButton")]
        public bool ShowButton { get; set; } = true;

        /// <summary>ボタンの文言。空なら既定 (「実行」)。</summary>
        [Designer(Index = 10, DisplayName = "$WebApiButtonText")]
        public string ButtonText { get; set; } = string.Empty;

        /// <summary>ボタンで呼んだ後に呼ぶスクリプト。引数は結果 (WebApiResult)。</summary>
        [Designer(Index = 11, DisplayName = "$WebApiOnCompleted", CandidateType = CandidateType.ScriptEvent),
         ScriptMethod(ArgumentTypes = ["WebApiResult"], ArgumentNames = ["result"])]
        public string OnCompleted { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(WebApiFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new WebApiField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            if (string.IsNullOrWhiteSpace(ConnectionName))
            {
                result.Add(CreateCheckInfo(context, CodeConnectionNameRequired, nameof(ConnectionName), Resources.DesignCheck_WebApiConnectionNameRequired));
            }
            if (!IsValidPath(Path))
            {
                result.Add(CreateCheckInfo(context, CodePathInvalid, nameof(Path), string.Format(Resources.DesignCheck_WebApiPathInvalid, Path)));
            }

            //呼び出しは対象行が無く (JobTriggerField と同じ)、モジュールの行の条件はサーバーの実行可否に合成できない。画面で押せなくなるだけなので守られていると誤解しやすい
            var moduleDesign = context.GetModuleDesign();
            if (moduleDesign?.DataReadCondition.HasEffectiveCondition() == true || moduleDesign?.DataWriteCondition.HasEffectiveCondition() == true)
            {
                result.Add(CreateCheckInfo(context, CodeDataConditionsNotAppliedToWebApi, nameof(ConnectionName),
                    string.Format(Resources.DesignCheck_Data_conditions_are_not_applied_to_web_api_, Name)));
            }
            foreach (var permission in moduleDesign?.Fields.OfType<PermissionFieldDesign>() ?? [])
            {
                if (!permission.Targets(Name)) continue;
                if (!permission.ReadCondition.Condition.ReferencesRowData() && !permission.WriteCondition.Condition.ReferencesRowData()) continue;
                result.Add(CreateCheckInfo(context, CodePermissionRowConditionsNotAppliedToWebApi, nameof(ConnectionName),
                    string.Format(Resources.DesignCheck_Permission_row_conditions_are_not_applied_to_web_api_, Name, permission.Name)));
            }
            CheckJsonFieldName(context, moduleDesign, nameof(ArgumentsField), ArgumentsField, result);
            CheckJsonFieldName(context, moduleDesign, nameof(ResultField), ResultField, result);
            return result;
        }

        void CheckJsonFieldName(DesignCheckContext context, ModuleDesign? moduleDesign, string member, string fieldName, List<DesignCheckInfo> result)
        {
            if (string.IsNullOrEmpty(fieldName)) return;
            if (moduleDesign?.Fields.Any(e => e.Name == fieldName && e is JsonFieldDesign) == true) return;
            result.Add(CreateCheckInfo(context, CodeJsonFieldNameInvalid, member,
                string.Format(Resources.DesignCheck_WebApiJsonFieldNameInvalid, member, fieldName)));
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddField(ArgumentsField, x => ArgumentsField = x)
            .AddField(ResultField, x => ResultField = x)
            .Build();

        FieldDesignCheckInfo CreateCheckInfo(DesignCheckContext context, int code, string member, string message) => new()
        {
            Code = DesignCheckCode.Create(typeof(WebApiFieldDesign), code),
            Location = new FieldDesignDataLocation
            {
                Module = context.OwnerModule,
                Field = Name,
                Member = member,
            },
            Message = message,
        };

        //{ と } が対になり、{} の中に名前があり入れ子でない (サーバーの WebApiUrlBuilder と同じ規則)
        internal static bool IsValidPath(string path)
        {
            var open = -1;
            for (var i = 0; i < path.Length; i++)
            {
                if (path[i] == '{')
                {
                    if (open != -1) return false;
                    open = i;
                }
                else if (path[i] == '}')
                {
                    if (open == -1 || i == open + 1 || string.IsNullOrWhiteSpace(path.Substring(open + 1, i - open - 1))) return false;
                    open = -1;
                }
            }
            return open == -1;
        }
    }
}
