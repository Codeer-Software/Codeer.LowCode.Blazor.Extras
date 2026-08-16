using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 単発メール送信フィールド (UI を持たない設定運搬 + スクリプト API)。
    /// 宛先・件名・本文テンプレートをデザインで宣言し、スクリプトの Send() が自レコードの値で
    /// テンプレートの {変数} (リンクパス可) を解決して送信する。ボタンは ButtonField + スクリプトで置く。
    /// 名簿への一斉送信は BulkMailField、完全に動的な送信は Mail スクリプトオブジェクト。
    /// このフィールドを使うアプリはサーバー側のメール送信対応 (MailController) が必要。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "EmailOutline")]
    [Designer(DisplayName = "$MailField")]
    public class MailFieldDesign : FieldDesignBase
    {
        public MailFieldDesign() : base(typeof(MailFieldDesign).FullName!) { }

        /// <summary>宛先アドレスの変数 ("Email.Value"。リンクパス可)。空なら To の固定アドレスを使う。</summary>
        [Designer(Index = 1, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldToVariable")]
        public string ToVariable { get; set; } = string.Empty;

        /// <summary>宛先アドレス (固定。カンマ / セミコロン区切りで複数可)。ToVariable が空のときに使う。</summary>
        [Designer(Index = 2, DisplayName = "$MailFieldTo")]
        public string To { get; set; } = string.Empty;

        /// <summary>Cc アドレスの変数 (リンクパス可)。空なら Cc の固定アドレスを使う。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldCcVariable")]
        public string CcVariable { get; set; } = string.Empty;

        /// <summary>Cc アドレス (固定。カンマ / セミコロン区切りで複数可)。</summary>
        [Designer(Index = 4, DisplayName = "$MailFieldCc")]
        public string Cc { get; set; } = string.Empty;

        /// <summary>件名テンプレートを持つ自モジュールの変数 ("Title.Value")。空なら Subject の固定文字列を使う。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldSubjectVariable")]
        public string SubjectVariable { get; set; } = string.Empty;

        /// <summary>件名テンプレート (固定)。{変数} は自レコードで解決される。</summary>
        [Designer(Index = 6, DisplayName = "$MailFieldSubject")]
        public string Subject { get; set; } = string.Empty;

        /// <summary>本文テンプレートを持つ自モジュールの変数 ("Body.Value")。空なら Body の固定文字列を使う。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldBodyVariable")]
        public string BodyVariable { get; set; } = string.Empty;

        /// <summary>本文テンプレート (固定)。{変数} は自レコードで解決される。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.MultilineString, DisplayName = "$MailFieldBody")]
        public string Body { get; set; } = string.Empty;

        /// <summary>メールインフラ名 (appsettings の Mail.Infras の名前)。空なら既定 (Mail.DefaultInfraName → 先頭)。</summary>
        [Designer(Index = 9, DisplayName = "$MailInfraName")]
        public string MailInfraName { get; set; } = string.Empty;

        /// <summary>本文を HTML として送るか。</summary>
        [Designer(Index = 10, DisplayName = "$MailFieldIsBodyHtml")]
        public bool IsBodyHtml { get; set; }

        /// <summary>返信先アドレス。</summary>
        [Designer(Index = 11, DisplayName = "$MailFieldReplyTo")]
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>差出人アドレスの変数 (任意・リンクパス可)。空 = 送信者設定の差出人。許可ドメインはサーバー設定 (AllowedFromDomains)。</summary>
        [Designer(Index = 12, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldFromVariable")]
        public string FromVariable { get; set; } = string.Empty;

        /// <summary>差出人表示名の変数 (任意・FromVariable 指定時のみ使われる)。</summary>
        [Designer(Index = 13, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldFromDisplayNameVariable")]
        public string FromDisplayNameVariable { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => string.Empty;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new MailField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //宛先は必須 (変数か固定のどちらか)
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
            context.CheckFieldVariableExistence(Name, nameof(SubjectVariable), SubjectVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(BodyVariable), BodyVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(FromVariable), FromVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(FromDisplayNameVariable), FromDisplayNameVariable).AddTo(result);

            if (string.IsNullOrEmpty(SubjectVariable) && string.IsNullOrEmpty(Subject) &&
                string.IsNullOrEmpty(BodyVariable) && string.IsNullOrEmpty(Body))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new FieldDesignDataLocation
                    { Module = context.OwnerModule, Field = Name, Member = nameof(Subject) },
                    Message = Properties.Resources.BulkMailSubjectOrBodyRequired,
                });
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context) => context.Builder(base.ChangeName(context))
            .AddVariable(ToVariable, x => ToVariable = x)
            .AddVariable(CcVariable, x => CcVariable = x)
            .AddVariable(SubjectVariable, x => SubjectVariable = x)
            .AddVariable(BodyVariable, x => BodyVariable = x)
            .AddVariable(FromVariable, x => FromVariable = x)
            .AddVariable(FromDisplayNameVariable, x => FromDisplayNameVariable = x)
            .Build();
    }
}
