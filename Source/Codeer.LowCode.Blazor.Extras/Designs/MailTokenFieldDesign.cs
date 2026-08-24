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
    /// メール送信用のユーザー単位トークン (Gmail のリフレッシュトークン等) を保存するフィールド。
    /// PasswordHashField と同じ書き込み専用パターン: 同じモジュール内の PasswordField
    /// (<see cref="TokenInputFieldName"/>) に貼り付けられた値を、保存時にサーバー側ヘルパ
    /// (MailUserTokenHelper.ApplyMailToken) が <see cref="DbColumnToken"/> へ書き込む。
    /// **列は書き込み専用でクライアントには一切返さない** (トークンは所持=送信できる秘密のため)。
    /// 送信時はサーバーが差出人アドレスでこのモジュールを検索してトークンを引く
    /// (appsettings の Mail.Infras[GmailApi].UserTokenModuleName / UserEmailFieldName / UserTokenFieldName)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "EmailLock")]
    [Designer(DisplayName = "$MailTokenField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class MailTokenFieldDesign() : FieldDesignBase(typeof(MailTokenFieldDesign).FullName!)
    {
        /// <summary>トークンを貼り付ける、同じモジュール内の PasswordField のフィールド名 (空入力=既存トークン維持)。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Field, DisplayName = "TokenInputField")]
        public string TokenInputFieldName { get; set; } = string.Empty;

        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnToken"), DbColumn(nameof(MailTokenFieldData.Token), IsWriteOnly = true)]
        public string DbColumnToken { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(MailTokenFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new MailTokenField(this);
        public override FieldDataBase? CreateData() => new MailTokenFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnToken), DbColumnToken).AddTo(result);
            return result;
        }
    }
}
