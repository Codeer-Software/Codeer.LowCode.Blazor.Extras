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
    /// 二要素認証 (TOTP) の秘密鍵をユーザーモジュール (CurrentUserModule) の行に持たせるための宣言。
    /// 3 つの DB 列 (秘密鍵 / 確認済み / 最終タイムステップ) を書き込み専用で名指しする。UI も submit データも持たず、
    /// 読み書きはサーバー側の Codeer.LowCode.Blazor.Extras.Server.Auth.TotpLogin がログイン時に直接行う (PasswordHashField と同じ作法)。
    /// このフィールドがユーザーモジュールにあると、テンプレートのログインが二要素認証付きになる。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CellphoneKey")]
    [Designer(DisplayName = "$TotpSecretField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class TotpSecretFieldDesign() : FieldDesignBase(typeof(TotpSecretFieldDesign).FullName!)
    {
        /// <summary>秘密鍵 (Base32 文字列)。未登録は NULL / 空。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnSecret"), DbColumn(nameof(TotpSecretFieldData.Secret), IsWriteOnly = true)]
        public string DbColumnSecret { get; set; } = string.Empty;

        /// <summary>オーセンティケータのコードで一度でも確認できたか (整数 0/1)。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnConfirmed"), DbColumn(nameof(TotpSecretFieldData.Confirmed), IsWriteOnly = true)]
        public string DbColumnConfirmed { get; set; } = string.Empty;

        /// <summary>最後に成功したタイムステップ (整数)。同じコードの再利用を防ぐ。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "DbColumnLastTimestep"), DbColumn(nameof(TotpSecretFieldData.LastTimestep), IsWriteOnly = true)]
        public string DbColumnLastTimestep { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(TotpSecretFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new TotpSecretField(this);
        public override FieldDataBase? CreateData() => new TotpSecretFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnSecret), DbColumnSecret).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnConfirmed), DbColumnConfirmed).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnLastTimestep), DbColumnLastTimestep).AddTo(result);
            return result;
        }
    }
}
