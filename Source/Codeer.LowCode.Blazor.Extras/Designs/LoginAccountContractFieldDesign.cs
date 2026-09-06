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
    /// 宣言は 2 種類:
    /// - 役割 (自モジュールのフィールド参照): ログイン ID、外部 IdP の突き合わせ、有効フラグ、表示名、メール二要素の送信先
    /// - ログイン用の DB 列 (書き込み専用。通常のモジュール読み書きには出てこず、ログイン時にサーバーが直接読み書きする):
    ///   パスワードのハッシュ / ソルト (両方空 = パスワードログイン無し = 外部 IdP 専用)、認証アプリ (TOTP) の秘密鍵 / 確認済み / 最終タイムステップ (空 = TOTP 無し)
    ///
    /// パスワードを「書く」のは PasswordHashField (ユーザー登録画面・変更ダイアログに置く。同じ列を指す)。この契約は「照合する」側。
    /// UI もデータも持たない。
    /// </summary>
    [Designer(DisplayName = "$LoginAccountContractField")]
    [ToolboxIcon(PackIconMaterialKind = "AccountKeyOutline")]
    public class LoginAccountContractFieldDesign : ContractFieldDesignBase
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodePasswordColumnsPair = 1;
        private const int CodeTotpColumnsSet = 2;

        public LoginAccountContractFieldDesign() : base(typeof(LoginAccountContractFieldDesign).FullName!) { }

        //---- 役割 (フィールド参照) ----

        /// <summary>ログイン ID を持つ自モジュールのフィールド (ユーザー名・メール等。DB 列を持つ値フィールド)。ID/パスワードのログインで入力する値と比較する。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$LoginAccountContractLoginName")]
        public string LoginName { get; set; } = nameof(LoginName);

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

        //---- ログイン用の DB 列 (書き込み専用) ----

        /// <summary>パスワードのハッシュ列 (PasswordHashField の DbColumnHash と同じ列)。<see cref="DbColumnPasswordSalt"/> とペアで設定。両方空ならパスワードログイン無し。</summary>
        [Designer(Index = 8, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnPasswordHash"), DbColumn(nameof(LoginAccountContractFieldData.PasswordHash), IsWriteOnly = true)]
        public string DbColumnPasswordHash { get; set; } = string.Empty;

        /// <summary>パスワードのソルト列 (PasswordHashField の DbColumnSalt と同じ列)。</summary>
        [Designer(Index = 9, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnPasswordSalt"), DbColumn(nameof(LoginAccountContractFieldData.PasswordSalt), IsWriteOnly = true)]
        public string DbColumnPasswordSalt { get; set; } = string.Empty;

        /// <summary>認証アプリ (TOTP) の秘密鍵列 (Base32 文字列。未登録は NULL)。3 列そろえて設定すると認証アプリの二要素認証が有効。</summary>
        [Designer(Index = 10, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpSecret"), DbColumn(nameof(LoginAccountContractFieldData.TotpSecret), IsWriteOnly = true)]
        public string DbColumnTotpSecret { get; set; } = string.Empty;

        /// <summary>認証アプリのコードで一度でも確認できたか (整数 0/1)。</summary>
        [Designer(Index = 11, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpConfirmed"), DbColumn(nameof(LoginAccountContractFieldData.TotpConfirmed), IsWriteOnly = true)]
        public string DbColumnTotpConfirmed { get; set; } = string.Empty;

        /// <summary>最後に成功したタイムステップ (整数)。同じコードの再利用を防ぐ。</summary>
        [Designer(Index = 12, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpLastTimestep"), DbColumn(nameof(LoginAccountContractFieldData.TotpLastTimestep), IsWriteOnly = true)]
        public string DbColumnTotpLastTimestep { get; set; } = string.Empty;

        /// <summary>パスワードログイン (ハッシュ / ソルトの列) が宣言されているか。</summary>
        public bool HasPassword => !string.IsNullOrWhiteSpace(DbColumnPasswordHash) && !string.IsNullOrWhiteSpace(DbColumnPasswordSalt);

        /// <summary>認証アプリ (TOTP) の 3 列が宣言されているか。</summary>
        public bool HasTotp => !string.IsNullOrWhiteSpace(DbColumnTotpSecret) && !string.IsNullOrWhiteSpace(DbColumnTotpConfirmed) && !string.IsNullOrWhiteSpace(DbColumnTotpLastTimestep);

        private protected override HashSet<string> RequiredRoleNames => new() { nameof(LoginName) };

        //DbColumn 属性の DataMember 用 (ランタイムが送受信することは無い)
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
