using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.DesignLogic.Check;
using Codeer.LowCode.Blazor.DesignLogic.Refactor;
using Codeer.LowCode.Blazor.Extras.Components;
using Codeer.LowCode.Blazor.Extras.Fields;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Designs
{
    /// <summary>
    /// 一覧ページの一括ダウンロード/一括更新 (と BulkFileReader / BulkFileTransferService) で、
    /// 対象フィールド 1 つの値を「ファイル上の表し方 (外部値)」と「DB の値 (内部値)」の間で引き当てる設定用フィールド。
    /// 変換表はただの業務モジュール (<see cref="ConversionModule"/>)。出力時は内部値で <see cref="InternalField"/> を引いて
    /// <see cref="ExternalField"/> の値を出し、取込時は外部値で <see cref="ExternalField"/> を引いて <see cref="InternalField"/> の値を入れる。
    /// 典型: LinkField の参照を Id ではなく名前で入出力する / EDI の取引先コード⇔自社コードのようなテキスト列の変換。
    /// モジュールの Fields に定義するだけで有効 (レイアウト配置は不要。配置しても実行時は描画しない)。
    /// 標準形式 (内部名ヘッダ) でも <see cref="FileColumnMappingFieldDesign"/> 併用時でも同じように効く
    /// (列構成はマッピング、値の表し方はこのフィールド、という直交した分担)。変換対象 1 フィールドにつき 1 つ定義する。
    /// 対象が LinkField のときは <see cref="ConversionModule"/> 省略でリンク先モジュール、<see cref="InternalField"/> 省略で Id。
    /// このフィールドを使うアプリはサーバー側の対応実装 (BulkFileTransfer への移譲) が必要。
    /// </summary>
    [Designer(DisplayName = "$FileValueConversionField")]
    [IgnoreBaseProperties(nameof(FieldDesignBase.IgnoreModification), nameof(FieldDesignBase.OnValidateInput), nameof(FieldDesignBase.IsFocusSkip), nameof(FieldDesignBase.OnFocusMoving), nameof(FieldDesignBase.NextFocusField))]
    public class FileValueConversionFieldDesign() : FieldDesignBase(typeof(FileValueConversionFieldDesign).FullName!)
    {
        /// <summary>デザインチェック指摘の番号。DesignCheckCode.Create で発行クラス名と結合して "クラス名:番号" になる。番号は固定(追加は末尾・欠番は再利用しない)。</summary>
        private const int CodeTargetFieldRequired = 1;
        private const int CodeTargetFieldNotConvertible = 2;
        private const int CodeConversionModuleRequired = 3;
        private const int CodeExternalFieldRequired = 4;
        private const int CodeInternalFieldRequired = 5;
        private const int CodeConversionModuleDiffersFromLinkTarget = 6;
        private const int CodeTargetFieldDuplicated = 7;

        /// <summary>変換対象のフィールド名 (同じモジュール内)。</summary>
        [Designer(Index = 1, CandidateType = CandidateType.Field, DisplayName = "$FileValueConversionTargetField")]
        public string TargetField { get; set; } = string.Empty;

        /// <summary>変換表となる業務モジュール名。対象が LinkField なら省略時はリンク先モジュール。</summary>
        [Designer(Index = 2, CandidateType = CandidateType.Module, DisplayName = "$FileValueConversionConversionModule")]
        public string ConversionModule { get; set; } = string.Empty;

        /// <summary>変換表でファイルに出す値を持つフィールド名 (例 "名前")。</summary>
        [Designer(Index = 3, CandidateType = CandidateType.Field, DisplayName = "$FileValueConversionExternalField"),
         ModuleMember(Member = nameof(ConversionModule))]
        public string ExternalField { get; set; } = string.Empty;

        /// <summary>変換表で DB に入る値を持つフィールド名 (例 "Id")。対象が LinkField なら省略時は Id。</summary>
        [Designer(Index = 4, CandidateType = CandidateType.Field, DisplayName = "$FileValueConversionInternalField"),
         ModuleMember(Member = nameof(ConversionModule))]
        public string InternalField { get; set; } = string.Empty;

        /// <summary>変換表モジュール名を解決する (省略時は対象が LinkField ならリンク先モジュール、それ以外は空)。</summary>
        public string ResolveConversionModule(ModuleDesign owner)
        {
            if (!string.IsNullOrEmpty(ConversionModule)) return ConversionModule;
            return FindTargetField(owner) is LinkFieldDesign link ? link.SearchCondition.ModuleName : string.Empty;
        }

        /// <summary>内部値フィールド名を解決する (省略時は対象が LinkField なら Id、それ以外は空)。</summary>
        public string ResolveInternalField(ModuleDesign owner)
        {
            if (!string.IsNullOrEmpty(InternalField)) return InternalField;
            return FindTargetField(owner) is LinkFieldDesign ? SystemFieldNames.Id : string.Empty;
        }

        /// <summary>変換対象にできるフィールドか (Value データメンバを持ち、一括入出力の対象になる型)。</summary>
        public static bool IsConvertibleTarget(FieldDesignBase field)
            => field.GetType().GetCustomAttribute<DisableBulkDataUpdateAttribute>(true) == null &&
               field.CreateData()?.GetType().GetProperty("Value") != null;

        FieldDesignBase? FindTargetField(ModuleDesign owner) => owner.Fields.FirstOrDefault(f => f.Name == TargetField);

        public override string GetWebComponentTypeFullName() => typeof(FileValueConversionFieldComponent).FullName!;

        public override string GetSearchWebComponentTypeFullName() => string.Empty;

        public override string GetSearchControlTypeFullName() => string.Empty;

        public override FieldBase CreateField() => new FileValueConversionField(this);

        public override FieldDataBase? CreateData() => null;

        public override List<DesignCheckInfo> CheckDesign(DesignCheckContext context)
        {
            var result = new List<DesignCheckInfo>();
            context.CheckFieldName(Name).AddTo(result);

            var owner = context.GetModuleDesign();
            FieldDesignBase? target = null;

            //変換対象 (自モジュールのフィールド。値を持ち一括入出力できる型。対象 1 つにつき変換フィールドは 1 つ)
            if (string.IsNullOrEmpty(TargetField))
            {
                result.Add(Create(context, CodeTargetFieldRequired, nameof(TargetField), Properties.Resources.FileValueConversionTargetFieldRequired));
            }
            else if (owner != null)
            {
                target = FindTargetField(owner);
                if (target == null)
                    context.CheckFieldRelativeFieldExistence(Name, nameof(TargetField), context.OwnerModule, TargetField).AddTo(result);
                else if (!IsConvertibleTarget(target))
                    result.Add(Create(context, CodeTargetFieldNotConvertible, nameof(TargetField),
                        string.Format(Properties.Resources.FileValueConversionTargetFieldNotConvertible, TargetField)));

                if (owner.Fields.OfType<FileValueConversionFieldDesign>().Any(f => !ReferenceEquals(f, this) && f.TargetField == TargetField))
                    result.Add(Create(context, CodeTargetFieldDuplicated, nameof(TargetField),
                        string.Format(Properties.Resources.FileValueConversionTargetFieldDuplicated, TargetField)));
            }

            //変換表 (モジュールと外部値/内部値フィールド。LinkField 対象なら省略時の既定値で解決)
            var conversionModule = owner == null ? ConversionModule : ResolveConversionModule(owner);
            if (string.IsNullOrEmpty(conversionModule))
            {
                result.Add(Create(context, CodeConversionModuleRequired, nameof(ConversionModule), Properties.Resources.FileValueConversionModuleRequired));
                return result;
            }
            context.CheckFieldModuleExistence(Name, nameof(ConversionModule), conversionModule).AddTo(result);

            if (string.IsNullOrEmpty(ExternalField))
                result.Add(Create(context, CodeExternalFieldRequired, nameof(ExternalField), Properties.Resources.FileValueConversionExternalFieldRequired));
            else
                context.CheckFieldRelativeFieldExistence(Name, nameof(ExternalField), conversionModule, ExternalField).AddTo(result);

            var internalField = owner == null ? InternalField : ResolveInternalField(owner);
            if (string.IsNullOrEmpty(internalField))
                result.Add(Create(context, CodeInternalFieldRequired, nameof(InternalField), Properties.Resources.FileValueConversionInternalFieldRequired));
            else
                context.CheckFieldRelativeFieldExistence(Name, nameof(InternalField), conversionModule, internalField).AddTo(result);

            //LinkField 対象で変換表を明示し、リンク先と食い違う (別モジュール経由の変換は意図的なこともあるため、抑止して使う)
            if (target is LinkFieldDesign linkTarget && !string.IsNullOrEmpty(ConversionModule) && ConversionModule != linkTarget.SearchCondition.ModuleName)
                result.Add(Create(context, CodeConversionModuleDiffersFromLinkTarget, nameof(ConversionModule),
                    string.Format(Properties.Resources.FileValueConversionModuleDiffersFromLinkTarget, ConversionModule, linkTarget.SearchCondition.ModuleName)));

            return result;
        }

        FieldDesignCheckInfo Create(DesignCheckContext context, int code, string member, string message) => new()
        {
            Code = DesignCheckCode.Create(typeof(FileValueConversionFieldDesign), code),
            Location = new() { Module = context.OwnerModule, Field = Name, Member = member },
            Message = message
        };

        public override RenameResult ChangeName(RenameContext context)
        {
            var builder = context.Builder(base.ChangeName(context))
                .AddField(TargetField, x => TargetField = x)
                .AddModule(ConversionModule, x => ConversionModule = x);

            //変換表のフィールドは (省略時の既定値も含めて) 解決したモジュールのフィールドとして追従する
            var owner = context.GetModuleDesign();
            var conversionModule = owner == null ? ConversionModule : ResolveConversionModule(owner);
            if (!string.IsNullOrEmpty(conversionModule))
            {
                builder.AddField(conversionModule, ExternalField, x => ExternalField = x)
                    .AddField(conversionModule, InternalField, x => InternalField = x);
            }
            return builder.Build();
        }
    }
}
