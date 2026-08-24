using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// Gmail のリフレッシュトークン (ユーザー同意モード) をユーザー単位で保存するフィールド。
    /// ユーザーモジュール (AppUser 等) に置くと、そのユーザーが「自分を差出人にする」で送ったメールが
    /// 本人の Gmail から送られ、送信済みも本人に残る。
    /// **列は書き込み専用でクライアントには一切返さない** (トークンは所持=送信できる秘密のため)。
    /// 入力欄はこのフィールド自身が持ち (貼り付け / トークンJSONファイルの読み込み)、
    /// 入力された平文はサーバー側で暗号化されてから列に入る
    /// (Extras.Server の GmailTokenHelper.ProtectGmailTokens + Gmail 設定の TokenEncryptionKey)。
    /// 送信時はサーバーが差出人アドレスでこのモジュールを検索して復号する
    /// (appsettings の Mail.UserModuleName / Mail.UserEmailFieldName と Gmail 設定の UserTokenFieldName)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "EmailLock")]
    [Designer(DisplayName = "$GmailTokenField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class GmailTokenFieldDesign() : FieldDesignBase(typeof(GmailTokenFieldDesign).FullName!)
    {
        [Designer(Index = 2, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnToken"), DbColumn(nameof(GmailTokenFieldData.RefreshToken), IsWriteOnly = true)]
        public string DbColumnToken { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(GmailTokenFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new GmailTokenField(this);
        public override FieldDataBase? CreateData() => new GmailTokenFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnToken), DbColumnToken).AddTo(result);
            return result;
        }
    }
}
