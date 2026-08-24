using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Mail;
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
    /// 宛先リストの行 = 送る対象そのもの (精査は行の追加/削除で行う)。配信停止は OptOutVariable (人側の属性が典型)。
    /// 件名・本文は自モジュールのフィールドをテンプレートとして参照できるため、配信レコードごとに文面を変えられる。
    /// テンプレートの {変数} は宛先行で解決される ({Name.Value} / {Contact.Email.Value} などのリンクパス可)。
    /// DbColumn を設定すると送信結果サマリ (JSON) がこのレコードの列に書き戻される (サーバー内部経路)。
    /// このフィールドを使うアプリはサーバー側のメール送信対応 (MailController) が必要。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "EmailOutline")]
    [Designer(DisplayName = "$BulkMailField")]
    [IgnoreBaseProperties(
        nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput),
        nameof(ValueFieldDesignBase.IsRequired), nameof(ValueFieldDesignBase.OnDataChanged),
        nameof(DbValueFieldDesignBase.IsUpdateProtected), nameof(DbValueFieldDesignBase.IsSimpleSearchParameter),
        nameof(DbValueFieldDesignBase.AllowEmptySearch), nameof(DbValueFieldDesignBase.OnSearchDataChanged))]
    public class BulkMailFieldDesign : DbValueFieldDesignBase
    {
        public BulkMailFieldDesign() : base(typeof(BulkMailFieldDesign).FullName!) { }

        /// <summary>宛先名簿のリストフィールド名 (List/DetailList/TileList)。そのリストの検索条件に合致する全行が送信対象。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Field, DisplayName = "$BulkMailRecipientListFieldName")]
        [TargetFieldType(Types = [typeof(ListFieldDesignBase)])]
        public string RecipientListFieldName { get; set; } = string.Empty;

        /// <summary>宛先モジュールの、メールアドレスを持つ変数 ("Email.Value")。リンクパス可 ("Contact.Email.Value")。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailEmailAddressVariable")]
        [ModuleMember(Member = "SearchCondition.ModuleName")]
        [RelativeField(Property = nameof(RecipientListFieldName))]
        public string EmailAddressVariable { get; set; } = string.Empty;

        /// <summary>
        /// 配信停止 (オプトアウト) の Boolean 変数。true の行には送らない (最終安全弁)。空なら判定なし。
        /// 人の恒久属性をリンクパスで指すのが典型 ("Contact.メール拒否.Value")。
        /// なお「今回のキャンペーンの対象から外す」は名簿の行を削除するのが正道 (このフラグの用途ではない)。
        /// </summary>
        [Designer(Index = 4, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailOptOutVariable")]
        [ModuleMember(Member = "SearchCondition.ModuleName")]
        [RelativeField(Property = nameof(RecipientListFieldName))]
        public string OptOutVariable { get; set; } = string.Empty;

        /// <summary>件名テンプレートを持つ自モジュールの変数 ("Title.Value")。Subject (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailSubjectVariable")]
        public string SubjectVariable { get; set; } = string.Empty;

        /// <summary>件名テンプレート (値)。入っていれば SubjectVariable より優先。{変数} は宛先行で解決される。</summary>
        [Designer(Index = 6, DisplayName = "$BulkMailSubject")]
        public string Subject { get; set; } = string.Empty;

        /// <summary>本文テンプレートを持つ自モジュールの変数 ("Body.Value")。Body (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 7, CandidateType = CandidateType.Variable, DisplayName = "$BulkMailBodyVariable")]
        public string BodyVariable { get; set; } = string.Empty;

        /// <summary>本文テンプレート (値)。入っていれば BodyVariable より優先。{変数} は宛先行で解決される。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.MultilineString, DisplayName = "$BulkMailBody")]
        public string Body { get; set; } = string.Empty;

        /// <summary>メールインフラ名 (appsettings の Mail.Infras の設定名 = どの送信インフラ・既定差出人を使うか)。空なら既定 (Mail.DefaultBulkInfraName → DefaultInfraName → 先頭)。</summary>
        [Designer(Index = 1, DisplayName = "$MailInfraName")]
        public string MailInfraName { get; set; } = string.Empty;

        /// <summary>本文を HTML として送るか。</summary>
        [Designer(Index = 12, DisplayName = "$BulkMailIsBodyHtml")]
        public bool IsBodyHtml { get; set; }

        /// <summary>返信先アドレスの変数 (自モジュールの変数・リンクパス可)。ReplyTo (値) が入っている場合はそちらが優先。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.Variable, DisplayName = "$MailFieldReplyToVariable")]
        public string ReplyToVariable { get; set; } = string.Empty;

        /// <summary>返信先アドレス (値)。入っていれば ReplyToVariable より優先。</summary>
        [Designer(Index = 11, DisplayName = "$BulkMailReplyTo")]
        public string ReplyTo { get; set; } = string.Empty;

        /// <summary>
        /// 自分 (操作ユーザー) を差出人にする。差出人アドレスはサーバーが操作ユーザーから解決する
        /// (アドレス指定は不可 = なりすましの構造的排除)。false = 送信インフラ設定の差出人 (システムのアドレス)。
        /// 要サーバー設定 Mail.UserModuleName / UserEmailFieldName。
        /// </summary>
        [Designer(Index = 9, DisplayName = "$MailFieldIsFromCurrentUser")]
        public bool IsFromCurrentUser { get; set; }

        /// <summary>ボタンの表示テキスト。空なら既定の文言。</summary>
        [Designer(Index = 13, DisplayName = "$BulkMailButtonText")]
        public string ButtonText { get; set; } = string.Empty;

        /// <summary>送信結果サマリ (JSON) の保存列。空ならサマリを保存しない (履歴モジュールの全量記録は別途 Mail.HistoryModuleName)。</summary>
        [Designer(Index = 14, CandidateType = CandidateType.DbColumn, DisplayName = "$BulkMailFieldDbColumn")]
        [DbColumn(nameof(BulkMailFieldData.Value))]
        public override string DbColumn { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(BulkMailFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new BulkMailField(this);

        public override FieldDataBase? CreateData() => new BulkMailFieldData();

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

            //宛先アドレスは必須。宛先モジュールの変数(リンクパス可)として検証する
            if (string.IsNullOrEmpty(EmailAddressVariable))
            {
                result.Add(CreateCheckInfo(context, nameof(EmailAddressVariable), Properties.Resources.BulkMailEmailAddressVariableRequired));
            }
            var targetModuleName = GetTargetModuleName(context);
            CheckRecipientVariable(context, nameof(EmailAddressVariable), targetModuleName, EmailAddressVariable).AddTo(result);
            CheckRecipientVariable(context, nameof(OptOutVariable), targetModuleName, OptOutVariable).AddTo(result);

            //テンプレートは変数参照(自モジュール)を検証。固定文字列とどちらも空なら知らせる
            context.CheckFieldVariableExistence(Name, nameof(SubjectVariable), SubjectVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(BodyVariable), BodyVariable).AddTo(result);
            context.CheckFieldVariableExistence(Name, nameof(ReplyToVariable), ReplyToVariable).AddTo(result);
            if (string.IsNullOrEmpty(SubjectVariable) && string.IsNullOrEmpty(Subject) &&
                string.IsNullOrEmpty(BodyVariable) && string.IsNullOrEmpty(Body))
            {
                result.Add(CreateCheckInfo(context, nameof(Subject), Properties.Resources.BulkMailSubjectOrBodyRequired));
            }
            return result;
        }

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context))
                .AddField(RecipientListFieldName, x => RecipientListFieldName = x)
                .AddVariable(SubjectVariable, x => SubjectVariable = x)
                .AddVariable(BodyVariable, x => BodyVariable = x)
                .AddVariable(ReplyToVariable, x => ReplyToVariable = x);

            //宛先モジュールの単純変数はリネーム追従する(リンクパスは非追従=既存のリンク越し変数と同じ制限)
            var targetModuleName = (context.GetFieldDesign(RecipientListFieldName) as IListFieldDesign)?.SearchCondition.ModuleName;
            if (!string.IsNullOrEmpty(targetModuleName))
            {
                builder.AddVariable(targetModuleName, EmailAddressVariable, x => EmailAddressVariable = x)
                    .AddVariable(targetModuleName, OptOutVariable, x => OptOutVariable = x);
            }
            return builder.Build();
        }

        string GetTargetModuleName(DesignCheckContext context)
        {
            var moduleDesign = context.DesignData.Modules.Find(context.OwnerModule);
            var listField = moduleDesign?.Fields.FirstOrDefault(e => e.Name == RecipientListFieldName);
            return (listField as IListFieldDesign)?.SearchCondition.ModuleName ?? string.Empty;
        }

        //宛先モジュールの変数を検証する。"Contact.Email.Value" のようなリンクパスは
        //Link/SelectFieldの参照先モジュールを辿って最終フィールドの存在まで確認する
        FieldDesignCheckInfo? CheckRecipientVariable(DesignCheckContext context, string memberName,
            string targetModuleName, string variable)
        {
            if (string.IsNullOrEmpty(targetModuleName) || string.IsNullOrEmpty(variable)) return null;

            var (fieldPath, _) = MailVariableResolver.ParseToken(variable);
            var current = context.DesignData.Modules.Find(targetModuleName);
            var segments = fieldPath.Split('.');
            for (var i = 0; i < segments.Length && current != null; i++)
            {
                var field = current.Fields.FirstOrDefault(e => e.Name == segments[i]);
                if (field == null)
                {
                    return CreateCheckInfo(context, memberName,
                        string.Format(Properties.Resources.BulkMailVariableNotFoundFormat, variable, targetModuleName));
                }
                if (i == segments.Length - 1) return null;

                var linkModuleName = field switch
                {
                    LinkFieldDesign linkField => linkField.SearchCondition.ModuleName,
                    SelectFieldDesign selectField => selectField.SearchCondition.ModuleName,
                    _ => string.Empty,
                };
                current = string.IsNullOrEmpty(linkModuleName) ? null : context.DesignData.Modules.Find(linkModuleName);
            }
            return current != null ? null : CreateCheckInfo(context, memberName,
                string.Format(Properties.Resources.BulkMailVariableNotFoundFormat, variable, targetModuleName));
        }

        FieldDesignCheckInfo CreateCheckInfo(DesignCheckContext context, string memberName, string message)
            => new()
            {
                Location = new() { Module = context.OwnerModule, Field = Name, Member = memberName },
                Message = message,
            };
    }
}
