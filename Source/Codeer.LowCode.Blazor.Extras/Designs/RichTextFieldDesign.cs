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
    [Designer(DisplayName = "$RichTextField")]
    public class RichTextFieldDesign() : ValueFieldDesignBase(typeof(RichTextFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeMentionNoUserModule = 1;
        private const int CodeMentionNoLoginContract = 2;

        [Designer(Index = 0, CandidateType = CandidateType.DbColumn, DisplayName = "$RichTextFieldDbColumn"), DbColumn(nameof(RichTextFieldData.Value))]
        public string DbColumn { get; set; } = string.Empty;

        /// <summary>
        /// 本文の中で @ を打つと、アプリを使える人 (ログインユーザーのモジュールの行) の候補を出して埋め込めるようにする。
        /// 候補はサーバーに問い合わせる (共通の API の Mention.Candidates)。既定 false。
        /// </summary>
        [Designer(Index = 1, DisplayName = "$RichTextFieldEnableMention")]
        public bool EnableMention { get; set; }

        public override string GetWebComponentTypeFullName() => typeof(RichTextFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new RichTextField(this);

        public override FieldDataBase? CreateData() => new RichTextFieldData();

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);
            context.CheckFieldDbColumnExistence(Name, nameof(DbColumn), DbColumn).AddTo(result);
            context.CheckFieldFunctionExistence(Name, nameof(OnDataChanged), OnDataChanged,
                context.GetScriptMethodAttribute(GetType(), nameof(OnDataChanged))).AddTo(result);
            if (EnableMention) CheckMention(context, result);
            return result;
        }

        //候補はログインユーザーのモジュールの行で、表示名とログイン ID はそこのログインアカウント契約の役割から引く
        void CheckMention(DesignCheckContext context, List<DesignCheckInfo> result)
        {
            var userModuleName = context.DesignData.AppSettings.CurrentUserModuleDesignName;
            var userModule = string.IsNullOrEmpty(userModuleName) ? null : context.DesignData.Modules.Find(userModuleName);
            if (userModule == null)
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), CodeMentionNoUserModule),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(EnableMention) },
                    Message = Properties.Resources.RichTextMentionCheck_NoUserModule,
                });
                return;
            }
            if (!userModule.Fields.OfType<LoginAccountContractFieldDesign>().Any())
            {
                result.Add(new FieldDesignCheckInfo
                {
                    Code = DesignCheckCode.Create(typeof(RichTextFieldDesign), CodeMentionNoLoginContract),
                    Location = new FieldDesignDataLocation { Module = context.OwnerModule, Field = Name, Member = nameof(EnableMention) },
                    Message = string.Format(Properties.Resources.RichTextMentionCheck_NoLoginContract, userModuleName),
                });
            }
        }
    }
}
