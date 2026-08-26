using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 単発メール送信フィールド。レイアウトに置くと送信ボタンとして表示され、押すと送信する
    /// (置かずにスクリプトの Send() からだけ使うこともできる)。
    /// 各項目は「値」と「変数」のペアで指定でき、**値が入っていれば値、空なら変数を解決**する。
    /// 値 (To/Subject 等) はスクリプトからも設定できる (ReceiptMail.To = "..." → Send())。
    /// 件名・本文の値はテンプレートで、{変数} (リンクパス可) が自レコードで解決される。
    /// 名簿への一斉送信は BulkMailField。
    /// このフィールドを使うアプリはサーバー側のメール送信対応 (MailController) が必要。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "EmailOutline")]
    [Designer(DisplayName = "$MailField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class MailFieldDesign : FieldDesignBase
    {
        public MailFieldDesign() : base(typeof(MailFieldDesign).FullName!) { }

        /// <summary>宛先アドレスの変数 ("Email.Value"。リンクパス可)。To (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldToVariable")]
        public string ToVariable { get; set; } = string.Empty;

        /// <summary>宛先アドレス (値。カンマ / セミコロン区切りで複数可)。スクリプトから設定可。入っていれば ToVariable より優先。</summary>
        [Designer(Index = 3, DisplayName = "$MailFieldTo")]
        public string To { get; set; } = string.Empty;

        /// <summary>Cc アドレスの変数 (リンクパス可)。Cc (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldCcVariable")]
        public string CcVariable { get; set; } = string.Empty;

        /// <summary>Cc アドレス (値)。スクリプトから設定可。入っていれば CcVariable より優先。</summary>
        [Designer(Index = 5, DisplayName = "$MailFieldCc")]
        public string Cc { get; set; } = string.Empty;

        /// <summary>Bcc アドレスの変数 (リンクパス可)。Bcc (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldBccVariable")]
        public string BccVariable { get; set; } = string.Empty;

        /// <summary>Bcc アドレス (値)。スクリプトから設定可。入っていれば BccVariable より優先。</summary>
        [Designer(Index = 7, DisplayName = "$MailFieldBcc")]
        public string Bcc { get; set; } = string.Empty;

        /// <summary>件名テンプレートを持つ自モジュールの変数 ("Title.Value")。Subject (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldSubjectVariable")]
        public string SubjectVariable { get; set; } = string.Empty;

        /// <summary>件名テンプレート (値)。スクリプトから設定可。{変数} は自レコードで解決される。入っていれば SubjectVariable より優先。</summary>
        [Designer(Index = 9, DisplayName = "$MailFieldSubject")]
        public string Subject { get; set; } = string.Empty;

        /// <summary>本文テンプレートを持つ自モジュールの変数 ("Body.Value")。Body (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldBodyVariable")]
        public string BodyVariable { get; set; } = string.Empty;

        /// <summary>本文テンプレート (値)。スクリプトから設定可。{変数} は自レコードで解決される。入っていれば BodyVariable より優先。</summary>
        [Designer(Index = 11, CandidateType = CandidateType.MultilineString, DisplayName = "$MailFieldBody")]
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// 送信先の呼び名 (テンプレートの MailController の対応表が解釈する。デザイン固定 = スクリプトからは変更不可)。
        /// 省略可 = 空なら appsettings の Mail.DefaultInfraName、それも空なら対応表の既定。
        /// </summary>
        [Designer(Index = 1, DisplayName = "$MailInfraName")]
        public string MailInfraName { get; set; } = string.Empty;

        /// <summary>本文を HTML として送るか。スクリプトから設定可。</summary>
        [Designer(Index = 15, DisplayName = "$MailFieldIsBodyHtml")]
        public bool IsBodyHtml { get; set; }

        /// <summary>レイアウトに置いたときの送信ボタンの表示テキスト。空なら既定の文言。</summary>
        [Designer(Index = 16, DisplayName = "$MailFieldButtonText")]
        public string ButtonText { get; set; } = string.Empty;

        /// <summary>返信先アドレスの変数 (リンクパス可)。ReplyTo (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 13, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldReplyToVariable")]
        public string ReplyToVariable { get; set; } = string.Empty;

        /// <summary>返信先アドレス (値)。スクリプトから設定可。入っていれば ReplyToVariable より優先。</summary>
        [Designer(Index = 14, DisplayName = "$MailFieldReplyTo")]
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>
        /// 自分 (操作ユーザー) を差出人にする。スクリプトから設定可。
        /// 差出人アドレスはサーバーが操作ユーザーから解決する (アドレス指定は不可 = なりすましの構造的排除)。
        /// false = 送信インフラ設定の差出人 (システムのアドレス)。要: デザインの CurrentUser モジュールに MailSenderContractField。
        /// </summary>
        [Designer(Index = 12, DisplayName = "$MailFieldIsFromCurrentUser")]
        public bool IsFromCurrentUser { get; set; }

        public override string GetWebComponentTypeFullName() => typeof(MailFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new MailField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //宛先は必須 (変数か値のどちらか。スクリプトで設定する場合でもどちらかの宣言を推奨)
            if (string.IsNullOrEmpty(ToVariable) && string.IsNullOrEmpty(To))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(To) },
                    Message = Properties.Resources.MailFieldToRequired,
                });
            }
            context.CheckFieldVariableExistence(Name, nameof(ToVariable), ToVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(CcVariable), CcVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(BccVariable), BccVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(SubjectVariable), SubjectVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(BodyVariable), BodyVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(ReplyToVariable), ReplyToVariable).AddTo(result);

            //「自分を差出人にする」は CurrentUser モジュールの差出人契約からアドレスを解決する
            if (IsFromCurrentUser)
            {
                ContractFieldChecks.CheckCurrentUserModuleImplementsContract<MailSenderContractFieldDesign>(
                    context, result, Name, nameof(IsFromCurrentUser));
            }

            if (string.IsNullOrEmpty(SubjectVariable) && string.IsNullOrEmpty(Subject) &&
                string.IsNullOrEmpty(BodyVariable) && string.IsNullOrEmpty(Body))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Subject) },
                    Message = Properties.Resources.MailSubjectOrBodyRequired,
                });
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddVariable(ToVariable, x => ToVariable = x)
            .AddVariable(CcVariable, x => CcVariable = x)
            .AddVariable(BccVariable, x => BccVariable = x)
            .AddVariable(SubjectVariable, x => SubjectVariable = x)
            .AddVariable(BodyVariable, x => BodyVariable = x)
            .AddVariable(ReplyToVariable, x => ReplyToVariable = x)
            .Build();
    }
}
