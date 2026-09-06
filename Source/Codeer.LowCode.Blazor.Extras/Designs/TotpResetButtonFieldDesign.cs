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
    /// 表示中の行のユーザーの認証アプリ (TOTP) 登録を解除するボタン。ログインユーザーモジュールの詳細画面に置く。
    /// TOTP の 3 列を書き込み専用列として持ち、解除は「3 列を空にする」データを通常の Submit で書く。
    /// そのため権限は CLB の権限モデル (モジュールの UserWriteCondition / 行の DataWriteCondition) がそのまま効き、サーバーに専用の入口は要らない。
    /// 列は書き込み専用なので秘密鍵はクライアントに来ない (登録済みかどうかも表示しない)。
    /// 列名はログインアカウント契約 (LoginAccountContractField) の TOTP 列と同じでなければならない (デザインチェック)。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CellphoneRemove")]
    [Designer(DisplayName = "$TotpResetButtonField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class TotpResetButtonFieldDesign() : FieldDesignBase(typeof(TotpResetButtonFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeNotOnUserModule = 1;
        private const int CodeColumnsMismatch = 2;

        /// <summary>ボタンの文字。空なら既定 ("認証アプリを解除")。</summary>
        [Designer(Index = 2, DisplayName = "$TotpResetButtonText")]
        public string Text { get; set; } = string.Empty;

        /// <summary>解除前の確認メッセージ。空なら既定。</summary>
        [Designer(Index = 3, DisplayName = "$TotpResetButtonConfirmMessage")]
        public string ConfirmMessage { get; set; } = string.Empty;

        /// <summary>秘密鍵の列 (書き込み専用。解除で null)。契約の DbColumnTotpSecret と同じ列。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpSecret"), DbColumn(nameof(TotpResetButtonFieldData.TotpSecret), IsWriteOnly = true)]
        public string DbColumnTotpSecret { get; set; } = string.Empty;

        /// <summary>確認済みの列 (書き込み専用。解除で 0)。契約の DbColumnTotpConfirmed と同じ列。</summary>
        [Designer(Index = 5, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpConfirmed"), DbColumn(nameof(TotpResetButtonFieldData.TotpConfirmed), IsWriteOnly = true)]
        public string DbColumnTotpConfirmed { get; set; } = string.Empty;

        /// <summary>最終タイムステップの列 (書き込み専用。解除で 0)。契約の DbColumnTotpLastTimestep と同じ列。</summary>
        [Designer(Index = 6, CandidateType = CandidateType.DbColumn, DisplayName = "$LoginAccountContractDbColumnTotpLastTimestep"), DbColumn(nameof(TotpResetButtonFieldData.TotpLastTimestep), IsWriteOnly = true)]
        public string DbColumnTotpLastTimestep { get; set; } = string.Empty;

        public override string GetWebComponentTypeFullName() => typeof(TotpResetButtonFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new TotpResetButtonField(this);
        public override FieldDataBase? CreateData() => new TotpResetButtonFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);

            //解除の対象は表示中の行 = ログインユーザーモジュールの行
            if (context.OwnerModule != context.DesignData.AppSettings.CurrentUserModuleDesignName)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(TotpResetButtonFieldDesign), CodeNotOnUserModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(Name) },
                    Message = Properties.Resources.TotpResetCheck_NotOnUserModule,
                });
            }

            foreach (var (member, column) in new[]
            {
                (nameof(DbColumnTotpSecret), DbColumnTotpSecret), (nameof(DbColumnTotpConfirmed), DbColumnTotpConfirmed), (nameof(DbColumnTotpLastTimestep), DbColumnTotpLastTimestep),
            })
            {
                context.CheckFieldDbColumnExistence(Name, member, column).AddTo(result);
            }

            //サーバー (TotpLogin) が見るのは契約の列。ボタンが別の列を空にしても解除にならないので一致を要求する
            var contract = context.GetModuleDesign()?.Fields.OfType<LoginAccountContractFieldDesign>().FirstOrDefault();
            var expected = contract?.HasTotp == true ? (contract.DbColumnTotpSecret, contract.DbColumnTotpConfirmed, contract.DbColumnTotpLastTimestep) : default;
            var mismatch = expected.Item1 != DbColumnTotpSecret ? nameof(DbColumnTotpSecret)
                : expected.Item2 != DbColumnTotpConfirmed ? nameof(DbColumnTotpConfirmed)
                : expected.Item3 != DbColumnTotpLastTimestep ? nameof(DbColumnTotpLastTimestep) : null;
            if (mismatch != null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(TotpResetButtonFieldDesign), CodeColumnsMismatch),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = mismatch },
                    Message = Properties.Resources.TotpResetCheck_ColumnsMismatch,
                });
            }
            return result;
        }
    }
}
