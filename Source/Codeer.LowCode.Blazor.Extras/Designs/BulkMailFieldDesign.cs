using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 詳細レイアウトに配置できる一斉メール送信ボタン (Salesforce のキャンペーン+リストメールと同型)。
    /// 同一モジュール上のリストフィールド (List/DetailList/TileList) を宛先リストとして参照し、
    /// そのリストの検索条件に合致する全行 (ページング無視) へサーバー解決で一斉送信する
    /// (アドレスはクライアントに渡らず、読み取り権限・行条件が効く)。
    /// 宛先リストの行 = 送る対象そのもの (精査は行の追加/削除で行う)。
    /// **宛先アドレス・配信停止は行モジュール側の BulkMailRecipientContractField で宣言する**
    /// (このフィールドには持たない = 基準モジュールの違う設定が混ざらない)。
    /// 件名・本文は自モジュールのフィールドをテンプレートとして参照できるため、配信レコードごとに文面を変えられる。
    /// テンプレートの {変数} は宛先行で解決される ({Name.Value} / {Contact.Email.Value} などのリンクパス可)。
    /// このフィールドを使うアプリはサーバー側のメール送信対応 (MailController) が必要。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "EmailOutline")]
    [Designer(DisplayName = "$BulkMailField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class BulkMailFieldDesign : FieldDesignBase
    {
        public BulkMailFieldDesign() : base(typeof(BulkMailFieldDesign).FullName!) { }

        /// <summary>宛先名簿のリストフィールド名 (List/DetailList/TileList)。そのリストの検索条件に合致する全行が送信対象。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Field, DisplayName = "$BulkMailRecipientListFieldName")]
        [TargetFieldType(Types = [typeof(ListFieldDesignBase)])]
        public string RecipientListFieldName { get; set; } = string.Empty;

        /// <summary>件名テンプレートを持つ自モジュールの変数 ("Title.Value")。Subject (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldSubjectVariable")]
        public string SubjectVariable { get; set; } = string.Empty;

        /// <summary>件名テンプレート (値)。入っていれば SubjectVariable より優先。{変数} は宛先行で解決される。</summary>
        [Designer(Index = 6, DisplayName = "$MailFieldSubject")]
        public string Subject { get; set; } = string.Empty;

        /// <summary>本文テンプレートを持つ自モジュールの変数 ("Body.Value")。Body (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldBodyVariable")]
        public string BodyVariable { get; set; } = string.Empty;

        /// <summary>本文テンプレート (値)。入っていれば BodyVariable より優先。{変数} は宛先行で解決される。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.MultilineString, DisplayName = "$MailFieldBody")]
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// 送信先の呼び名 (テンプレートの MailController の対応表が解釈する)。
        /// 省略可 = 空なら appsettings の Mail.DefaultBulkInfraName → DefaultInfraName、それも空なら対応表の既定。
        /// </summary>
        [Designer(Index = 1, DisplayName = "$MailInfraName")]
        public string MailInfraName { get; set; } = string.Empty;

        /// <summary>本文を HTML として送るか。</summary>
        [Designer(Index = 12, DisplayName = "$MailFieldIsBodyHtml")]
        public bool IsBodyHtml { get; set; }

        /// <summary>返信先アドレスの変数 (自モジュールの変数・リンクパス可)。ReplyTo (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldReplyToVariable")]
        public string ReplyToVariable { get; set; } = string.Empty;

        /// <summary>返信先アドレス (値)。入っていれば ReplyToVariable より優先。</summary>
        [Designer(Index = 11, DisplayName = "$MailFieldReplyTo")]
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>送信ボタンの横にプレビューボタン (解決後の文面を HTML でダウンロード) を出すか。</summary>
        [Designer(Index = 14, DisplayName = "$MailFieldShowPreviewButton")]
        public bool ShowPreviewButton { get; set; } = true;

        public override string GetWebComponentTypeFullName() => typeof(BulkMailFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new BulkMailField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //名簿リストは必須
            if (string.IsNullOrEmpty(RecipientListFieldName))
            {
                result.Add(CreateCheckInfo(context, nameof(RecipientListFieldName), Properties.Resources.BulkMailRecipientListFieldRequired));
            }
            context.CheckFieldFieldExistence(Name, nameof(RecipientListFieldName), RecipientListFieldName).AddTo(result);
            context.CheckFieldFieldInstanceType(Name, nameof(RecipientListFieldName), RecipientListFieldName, typeof(ListFieldDesignBase)).AddTo(result);

            //宛先アドレス・配信停止は行モジュールの宛先契約が宣言する。契約が無ければエラー
            ContractFieldChecks.CheckListImplementsContract<BulkMailRecipientContractFieldDesign>(context, result,
                Name, nameof(RecipientListFieldName), RecipientListFieldName);

            //テンプレートは変数参照(自モジュール)を検証。固定文字列とどちらも空なら知らせる
            context.CheckFieldVariableExistence(Name, nameof(SubjectVariable), SubjectVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(BodyVariable), BodyVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(ReplyToVariable), ReplyToVariable).AddTo(result);

            if (string.IsNullOrEmpty(SubjectVariable) && string.IsNullOrEmpty(Subject) &&
                string.IsNullOrEmpty(BodyVariable) && string.IsNullOrEmpty(Body))
            {
                result.Add(CreateCheckInfo(context, nameof(Subject), Properties.Resources.MailSubjectOrBodyRequired));
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context)
        {
            return context.Builder(base.ChangeName(context))
                .AddField(RecipientListFieldName, x => RecipientListFieldName = x)
                .AddVariable(SubjectVariable, x => SubjectVariable = x)
                .AddVariable(BodyVariable, x => BodyVariable = x)
                .AddVariable(ReplyToVariable, x => ReplyToVariable = x)
                .Build();
        }

        FieldDesignCheckInfo CreateCheckInfo(DesignCheckContext context, string memberName, string message)
            => new()
            {
                Location = new() { Module = context.OwnerModule, Field = Name, Member = memberName },
                Message = message,
            };
    }
}
