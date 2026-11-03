using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 表示中の行のユーザーを全端末からログアウトさせるボタン (管理者用)。ログインユーザーモジュールの詳細画面に置く。
    /// セッションの印の列を書き込み専用列として持ち、ログアウトは「印を NULL にする」データを通常の Submit で書く。
    /// 印が NULL の行はログイン済み Cookie の照合で落ちる (次のログインで新しい印が採番される)。
    /// 権限は CLB の権限モデル (モジュールの UserWriteCondition / 行の DataWriteCondition) がそのまま効き、サーバーに専用の入口は要らない。
    /// 列名はログインアカウント契約 (LoginAccountContractField) の DbColumnSessionStamp と同じでなければならない (デザインチェック)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "AccountOff")]
    [Designer(DisplayName = "$SessionRevokeButtonField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class SessionRevokeButtonFieldDesign() : FieldDesignBase(typeof(SessionRevokeButtonFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeNotOnUserModule = 1;
        private const int CodeColumnsMismatch = 2;

        /// <summary>ボタンの文字。空なら既定 ("全端末からログアウト")。</summary>
        [Designer(Index = 2, DisplayName = "$SessionRevokeButtonText")]
        public string Text { get; set; } = string.Empty;

        /// <summary>ログアウト前の確認メッセージ。空なら既定。</summary>
        [Designer(Index = 3, DisplayName = "$SessionRevokeButtonConfirmMessage")]
        public string ConfirmMessage { get; set; } = string.Empty;

        /// <summary>セッションの印の列 (書き込み専用。ログアウトで null)。契約の DbColumnSessionStamp と同じ列。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnSessionStamp"), DbColumn(nameof(SessionRevokeButtonFieldData.SessionStamp), IsWriteOnly = true)]
        public string DbColumnSessionStamp { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(SessionRevokeButtonFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new SessionRevokeButtonField(this);
        public override FieldDataBase? CreateData() => new SessionRevokeButtonFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //ログアウトの対象は表示中の行 = ログインユーザーモジュールの行
            if (context.OwnerModule != context.DesignData.AppSettings.CurrentUserModuleDesignName)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(SessionRevokeButtonFieldDesign), CodeNotOnUserModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = Properties.Resources.SessionRevokeCheck_NotOnUserModule,
                });
            }

            context.CheckFieldDbColumnExistence(Name, nameof(DbColumnSessionStamp), DbColumnSessionStamp).AddTo(result);

            //照合が見るのは契約の列。ボタンが別の列を空にしてもログアウトにならないので一致を要求する
            var contract = context.GetModuleDesign()?.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            if (contract?.HasSessionStamp != true || contract.DbColumnSessionStamp != DbColumnSessionStamp)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(SessionRevokeButtonFieldDesign), CodeColumnsMismatch),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(DbColumnSessionStamp) },
                    Message = Properties.Resources.SessionRevokeCheck_ColumnsMismatch,
                });
            }
            return result;
        }
    }
}
