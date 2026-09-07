using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// ログインアカウント契約。ログインユーザーモジュール (AppSettings.CurrentUserModuleDesignName) に 1 つ置き、
    /// 「このモジュールの行がログインアカウントとしてどう振る舞うか」を宣言する。サーバー側のログイン
    /// (Codeer.LowCode.Blazor.Extras.Server.Auth.LoginAccountStore / TotpLogin / EmailOtpLogin) はこの契約だけを見る。
    ///
    /// 宣言は 3 種類:
    /// - 役割 (自モジュールのフィールド参照): ログイン ID、外部 IdP の突き合わせ、有効フラグ、表示名、メール二要素の送信先
    /// - パスワードの入力フィールド (<see cref="PasswordField"/>): 指定すると保存時にサーバーがその平文からハッシュ / ソルトを作り、下の列に書く
    ///   (PasswordHashField を置かなくてよい。PasswordHashField は別モジュールの変更ダイアログなど、契約の無いモジュール向けに残る)
    /// - ログイン用の DB 列: パスワードのハッシュ / ソルト (書き込み専用列。両方空 = パスワードログイン無し = 外部 IdP 専用)、
    ///   認証アプリ (TOTP) の秘密鍵 / 確認済み / 最終タイムステップ (列名だけの宣言。サーバーのログイン処理が直接読み書きし、本体の読み書きには乗せない)
    ///
    /// UI は持たない。データはハッシュ / ソルトの 2 列分だけで、サーバーが保存時に差し込む (クライアントは送受信しない)。
    /// </summary>
    [Designer(DisplayName = "$LoginAccountContractField")]
    [ToolboxIcon(PackIconMaterialKind = "AccountKeyOutline")]
    public class LoginAccountContractFieldDesign : ContractFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodePasswordColumnsPair = 1;
        private const int CodeTotpColumnsSet = 2;
        private const int CodePasswordFieldRequiresColumns = 3;
        private const int CodePasswordFieldConflictsWithHashField = 4;

        public LoginAccountContractFieldDesign() : base(typeof(LoginAccountContractFieldDesign).FullName!) { }

        //---- 役割 (フィールド参照) ----

        /// <summary>ログイン ID を持つ自モジュールのフィールド (ユーザー名・メール等。DB 列を持つ値フィールド)。ID/パスワードのログインで入力する値と比較する。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractLoginName")]
        public string LoginName { get; set; } = string.Empty;

        /// <summary>
        /// 外部 IdP が確認した本人 (Entra = UPN、Google / Cognito = メール) と突き合わせるフィールド。
        /// 空なら <see cref="LoginName"/> と同じ列で突き合わせる (社内のログイン ID とメールが別ならここにメールのフィールドを指定する)。
        /// </summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractExternalLoginName")]
        public string ExternalLoginName { get; set; } = string.Empty;

        /// <summary>有効フラグ (Boolean 等)。偽ならログインを拒否する (退職・停止)。空なら判定しない。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractIsActive")]
        public string IsActive { get; set; } = string.Empty;

        /// <summary>表示名。Cookie の Name クレーム (画面のユーザー表示) と二要素認証の QR のアカウント名に使う。空なら <see cref="LoginName"/>。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractDisplayName")]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// メールのワンタイムコードによる二要素認証の送信先 (メールアドレスのフィールド)。設定するとパスワード成功後にコードを送って入力を求める。
        /// 空なら無効。TOTP の列も設定されていればそちら (認証アプリ) が優先される。
        /// </summary>
        [Designer(Index = 7, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractTwoFactorEmail")]
        public string TwoFactorEmail { get; set; } = string.Empty;

        /// <summary>
        /// パスワードの入力フィールド (同じモジュールの PasswordField。平文の DB 列は持たない)。
        /// 指定すると、保存時にその平文からハッシュ / ソルトを作って <see cref="DbColumnPasswordHash"/> / <see cref="DbColumnPasswordSalt"/> に書く
        /// (サーバーの PasswordHashHelper.ApplyPasswordHash。テンプレートは組み込み済み)。空なら書かない (PasswordHashField か外部 IdP 専用)。
        /// </summary>
        [Designer(Index = 8, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractPasswordField")]
        public string PasswordField { get; set; } = string.Empty;

        //---- ログイン用の DB 列 ----

        /// <summary>パスワードのハッシュ列 (書き込み専用)。<see cref="DbColumnPasswordSalt"/> とペアで設定。両方空ならパスワードログイン無し。</summary>
        [Designer(Index = 9, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnPasswordHash"), DbColumn(nameof(LoginAccountContractFieldData.PasswordHash), IsWriteOnly = true)]
        public string DbColumnPasswordHash { get; set; } = string.Empty;

        /// <summary>パスワードのソルト列 (書き込み専用)。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnPasswordSalt"), DbColumn(nameof(LoginAccountContractFieldData.PasswordSalt), IsWriteOnly = true)]
        public string DbColumnPasswordSalt { get; set; } = string.Empty;

        //TOTP の 3 列は列名だけの宣言 (DbColumn 属性を付けない)。本体の読み書きに乗せると、パスワード保存 (契約データの差し込み) のたびに NULL で上書きされるため。
        //読み書きはサーバーのログイン処理 (TotpLogin) が列名で直接行う。解除は TotpResetButtonField (自分の書き込み専用列で書く)。

        /// <summary>認証アプリ (TOTP) の秘密鍵列 (TEXT。Base32 文字列。未登録は NULL)。3 列そろえて設定すると認証アプリの二要素認証が有効。</summary>
        [Designer(Index = 11, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpSecret")]
        public string DbColumnTotpSecret { get; set; } = string.Empty;

        /// <summary>認証アプリのコードで一度でも確認できたか (INTEGER 0/1)。</summary>
        [Designer(Index = 12, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpConfirmed")]
        public string DbColumnTotpConfirmed { get; set; } = string.Empty;

        /// <summary>最後に成功したタイムステップ (INTEGER)。同じコードの再利用を防ぐ。</summary>
        [Designer(Index = 13, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpLastTimestep")]
        public string DbColumnTotpLastTimestep { get; set; } = string.Empty;

        /// <summary>パスワードログイン (ハッシュ / ソルトの列) が宣言されているか。</summary>
        public bool HasPassword => !string.IsNullOrWhiteSpace(DbColumnPasswordHash) && !string.IsNullOrWhiteSpace(DbColumnPasswordSalt);

        /// <summary>保存時にハッシュを書く (PasswordField と列がそろっている) か。</summary>
        public bool WritesPassword => HasPassword && !string.IsNullOrWhiteSpace(PasswordField);

        /// <summary>認証アプリ (TOTP) の 3 列が宣言されているか。</summary>
        public bool HasTotp => !string.IsNullOrWhiteSpace(DbColumnTotpSecret) && !string.IsNullOrWhiteSpace(DbColumnTotpConfirmed) && !string.IsNullOrWhiteSpace(DbColumnTotpLastTimestep);

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(LoginName) };

        //ハッシュ / ソルト列の書き込み用 (サーバーが保存時に差し込む)
        public override FieldDataBase? CreateData() => new LoginAccountContractFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //指定した列は実テーブルにあること
            foreach (var (member, column) in new[]
            {
                (nameof(DbColumnPasswordHash), DbColumnPasswordHash), (nameof(DbColumnPasswordSalt), DbColumnPasswordSalt),
                (nameof(DbColumnTotpSecret), DbColumnTotpSecret), (nameof(DbColumnTotpConfirmed), DbColumnTotpConfirmed), (nameof(DbColumnTotpLastTimestep), DbColumnTotpLastTimestep),
            })
            {
                if (!string.IsNullOrEmpty(column)) context.CheckFieldDbColumnExistence(Name, member, column).AddTo(result);
            }

            //ハッシュとソルトは両方かどちらも無し
            if (string.IsNullOrWhiteSpace(DbColumnPasswordHash) != string.IsNullOrWhiteSpace(DbColumnPasswordSalt))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(LoginAccountContractFieldDesign), CodePasswordColumnsPair),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = string.IsNullOrWhiteSpace(DbColumnPasswordHash) ? nameof(DbColumnPasswordHash) : nameof(DbColumnPasswordSalt) },
                    Message = Properties.Resources.LoginAccountCheck_PasswordColumnsPair,
                });
            }

            //パスワード入力フィールドは PasswordField 型で、書き先の列がそろっていること。PasswordHashField との併用は同じ列を二重に書くので不可
            if (!string.IsNullOrWhiteSpace(PasswordField))
            {
                context.CheckFieldFieldInstanceType(Name, nameof(PasswordField), PasswordField, typeof(PasswordFieldDesign)).AddTo(result);
                if (!HasPassword)
                {
                    result.Add(new FieldDesignCheckInfo
                    {
                        Code = DesignCheckCode.Create(typeof(LoginAccountContractFieldDesign), CodePasswordFieldRequiresColumns),
                        Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(PasswordField) },
                        Message = Properties.Resources.LoginAccountCheck_PasswordFieldRequiresColumns,
                    });
                }
                var hashField = context.GetModuleDesign()?.Fields.OfType<PasswordHashFieldDesign>().FirstOrDefault();
                if (hashField != null)
                {
                    result.Add(new FieldDesignCheckInfo
                    {
                        Code = DesignCheckCode.Create(typeof(LoginAccountContractFieldDesign), CodePasswordFieldConflictsWithHashField),
                        Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(PasswordField) },
                        Message = string.Format(Properties.Resources.LoginAccountCheck_PasswordFieldConflictsWithHashFieldFormat, hashField.Name),
                    });
                }
            }

            //TOTP は 3 列そろえる
            var totp = new[] { DbColumnTotpSecret, DbColumnTotpConfirmed, DbColumnTotpLastTimestep };
            if (totp.Any(c => !string.IsNullOrWhiteSpace(c)) && totp.Any(string.IsNullOrWhiteSpace))
            {
                var member = string.IsNullOrWhiteSpace(DbColumnTotpSecret) ? nameof(DbColumnTotpSecret)
                    : string.IsNullOrWhiteSpace(DbColumnTotpConfirmed) ? nameof(DbColumnTotpConfirmed) : nameof(DbColumnTotpLastTimestep);
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(LoginAccountContractFieldDesign), CodeTotpColumnsSet),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = member },
                    Message = Properties.Resources.LoginAccountCheck_TotpColumnsSet,
                });
            }
            return result;
        }
    }
}
