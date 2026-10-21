using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Location;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// ボタン (またはスクリプトの Run()) で、ホストにジョブの実行を頼むフィールド (POST JobTriggerField.EndPoint)。
    /// ホストに渡すのはモジュール名・フィールド名・画面の内容の JSON で、どのジョブを動かすか (JobName)・待つかどうか (IsAsynchronous) はホストがこのデザインから読む。
    /// ジョブをどう動かすかはホストの実装 (Extras.Server の JobProcess とテンプレの Jobs/README.md)。保存はしない。データは持たない。
    /// 実行の可否はサーバーがこのフィールドの書き込み権限で判定する (行を持たない = IExecutionFieldDesign)。
    /// </summary>
    [Designer(DisplayName = "$JobTriggerField")]
    [ToolboxIcon(PackIconMaterialKind = "CogOutline")]
    [AllowColorInheritance(false)]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class JobTriggerFieldDesign() : FieldDesignBase(typeof(JobTriggerFieldDesign).FullName!), IExecutionFieldDesign
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeJobNameRequired = 1;
        private const int CodeDataConditionsNotAppliedToJobExecution = 2;

        /// <summary>実行するジョブの名前 (ホストが解決する)。必須。</summary>
        [Designer(Index = 3, DisplayName = "$JobTriggerJobName")]
        public string JobName { get; set; } = string.Empty;

        /// <summary>ボタンを出す。false なら画面には何も出ず、スクリプトの Run() からだけ実行する。</summary>
        [Designer(Index = 4, DisplayName = "$JobTriggerShowButton")]
        public bool ShowButton { get; set; } = true;

        /// <summary>ボタンの文言。空なら既定 (「実行」)。</summary>
        [Designer(Index = 5, DisplayName = "$JobTriggerButtonText")]
        public string ButtonText { get; set; } = string.Empty;

        /// <summary>画面の内容 (現在値と、保存ボタンが送る送信内容) をジョブに渡す。false なら {} を渡す。</summary>
        [Designer(Index = 6, DisplayName = "$JobTriggerUseSubmitData")]
        public bool UseSubmitData { get; set; } = true;

        /// <summary>非同期で実行する。true ならジョブを起動したらすぐ戻る (結果は画面に返らない)。false ならジョブが終わるまで待って結果を出す。</summary>
        [Designer(Index = 7, DisplayName = "$JobTriggerIsAsynchronous")]
        public bool IsAsynchronous { get; set; }

        public override string GetWebComponentTypeFullName() => typeof(JobTriggerFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldDataBase? CreateData() => null;

        public override FieldBase CreateField() => new JobTriggerField(this);

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            if (string.IsNullOrWhiteSpace(JobName))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(JobTriggerFieldDesign), CodeJobNameRequired),
                    Location = new FieldDesignDataLocation
                    {
                        Module = context.OwnerModule,
                        Field = Name,
                        Member = nameof(JobName),
                    },
                    Message = Resources.DesignCheck_JobTriggerJobNameRequired,
                });
            }

            //ジョブの実行は対象行が無く (ExecuteSqlField の Standalone 実行と同じ)、モジュールの行の条件はサーバーの実行可否に合成できない。
            //画面ではその行でボタンが押せなくなるので守られていると誤解しやすいため検出する (効くのは UserWriteCondition と PermissionField のユーザー条件だけ)
            var moduleDesign = context.GetModuleDesign();
            if (moduleDesign?.DataReadCondition.HasEffectiveCondition() == true || moduleDesign?.DataWriteCondition.HasEffectiveCondition() == true)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(JobTriggerFieldDesign), CodeDataConditionsNotAppliedToJobExecution),
                    Location = new FieldDesignDataLocation
                    {
                        Module = context.OwnerModule,
                        Field = Name,
                        Member = nameof(JobName),
                    },
                    Message = string.Format(Resources.DesignCheck_Data_conditions_are_not_applied_to_job_execution_, Name),
                });
            }
            return result;
        }
    }
}
