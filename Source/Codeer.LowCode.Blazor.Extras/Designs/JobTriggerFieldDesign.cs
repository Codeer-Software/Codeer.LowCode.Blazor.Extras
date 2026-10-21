using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// ボタン (またはスクリプトの Run()) で、ジョブ名と画面の内容の JSON をホストの API (POST {JobTriggerField.EndPoint}/run) に渡してジョブの実行を頼み、結果を受け取る。
    /// JobName はホストが解決する呼び名で、ジョブをどう動かすかはホストの持ち物 (Starter テンプレートは JobController / JobProcess / JobRunner)。
    /// UseSubmitData なら画面の送信内容 (<c>{"submit":[ModuleSubmitData...]}</c>、送るものが無ければ現在値 <c>{"data":ModuleData}</c>) を渡す。保存はしない。データは持たない。
    /// </summary>
    [ToolboxIcon(PackIconMaterialKind = "CogOutline")]
    [Designer(DisplayName = "$JobTriggerField")]
    [AllowColorInheritance(false)]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput))]
    public class JobTriggerFieldDesign() : FieldDesignBase(typeof(JobTriggerFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeJobNameRequired = 1;

        /// <summary>実行するジョブ名 (ホストのジョブモードのジョブ名)。</summary>
        [Designer(Index = 2, DisplayName = "$JobTriggerJobName")]
        public string JobName { get; set; } = string.Empty;

        /// <summary>ボタンを出す。false ならスクリプトの Run() からだけ実行する (画面には何も出ない)。</summary>
        [Designer(Index = 3, DisplayName = "$JobTriggerShowButton")]
        public bool ShowButton { get; set; } = true;

        /// <summary>ボタンの文言。空なら既定 (「実行」)。</summary>
        [Designer(Index = 4, DisplayName = "$JobTriggerButtonText")]
        public string ButtonText { get; set; } = string.Empty;

        /// <summary>画面の内容 (送信内容。無ければ現在値) をジョブに渡す。false なら {} を渡す。</summary>
        [Designer(Index = 5, DisplayName = "$JobTriggerUseSubmitData")]
        public bool UseSubmitData { get; set; } = true;

        /// <summary>非同期で実行する。true ならジョブを起動したらすぐ戻る (結果は画面に返らず、失敗はサーバーのログ)。false ならジョブが終わるまで待って結果を出す。</summary>
        [Designer(Index = 6, DisplayName = "$JobTriggerIsAsynchronous")]
        public bool IsAsynchronous { get; set; }

        public override string GetWebComponentTypeFullName() => typeof(JobTriggerFieldComponent).FullName!;
        public override string GetSearchWebComponentTypeFullName() => string.Empty;
        public override string GetSearchControlTypeFullName() => string.Empty;
        public override FieldBase CreateField() => new JobTriggerField(this);
        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = base.CheckDesign(context);
            if (string.IsNullOrWhiteSpace(JobName))
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Location = new() { Module = context.OwnerModule, Field = Name, Member = nameof(JobName) },
                    Message = Properties.Resources.JobTriggerJobNameRequired,
                    Code = DesignCheckCode.Create(typeof(JobTriggerFieldDesign), CodeJobNameRequired),
                });
            }
            return result;
        }
    }
}
