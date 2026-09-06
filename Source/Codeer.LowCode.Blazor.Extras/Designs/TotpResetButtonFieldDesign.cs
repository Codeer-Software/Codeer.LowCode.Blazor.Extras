using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 認証アプリ (TOTP) の登録を解除するボタン。ログインユーザーモジュールの詳細画面に置く。
    /// 表示中の行のユーザーが登録済みなら「解除」ボタン、未登録なら状態だけ出す。認証アプリの二要素認証を使っていないアプリでは何も出さない。
    /// 自分の行なら本人による解除、他人の行なら「その行を編集できる人」だけが解除できる (サーバーが CLB の権限モデルで判定)。
    /// サーバー側の入口 (状態取得 / 解除) はテンプレートの AccountController に組み込み済み (エンドポイントは TotpResetButtonField の静的プロパティで結線)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CellphoneRemove")]
    [Designer(DisplayName = "$TotpResetButtonField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class TotpResetButtonFieldDesign() : FieldDesignBase(typeof(TotpResetButtonFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        public static class Codes
        {
            public const int NotOnUserModule = 1;
        }

        /// <summary>ボタンの文字。空なら既定 ("認証アプリを解除")。</summary>
        [Designer(Index = 2, DisplayName = "$TotpResetButtonText")]
        public string Text { get; set; } = string.Empty;

        /// <summary>解除前の確認メッセージ。空なら既定。</summary>
        [Designer(Index = 3, DisplayName = "$TotpResetButtonConfirmMessage")]
        public string ConfirmMessage { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(TotpResetButtonFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new TotpResetButtonField(this);
        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            //解除の対象は表示中の行 = ログインユーザーモジュールの行
            if (context.OwnerModule != context.DesignData.AppSettings.CurrentUserModuleDesignName)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(TotpResetButtonFieldDesign), Codes.NotOnUserModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = Properties.Resources.TotpResetCheck_NotOnUserModule,
                });
            }
            return result;
        }
    }
}
