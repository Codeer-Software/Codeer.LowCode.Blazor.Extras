using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Migrations
{
    /// <summary>
    /// 0.13.0: FileColumnMappingField の列ごとのコード変換 (ConversionModule / ConversionExternalField / ConversionInternalField) を
    /// 廃止し、<see cref="FileValueConversionFieldDesign"/> (変換対象 1 フィールドにつき 1 つ) へ移す。
    /// 変換付きの列ごとに、その Field (フィールド名部分) を対象とする変換フィールドを "フィールド名_Conversion" の名前で作り、列側の設定を消す。
    /// 同じ対象の変換フィールドが既にあり設定が同じなら列側だけ消す。設定が違うなら自動移行できないので列をそのまま残す (デザインチェックが指摘する)。
    /// デザイナがプロジェクトを開いたとき、前回保存時の Extras が 0.13.0 より古ければマイグレーション一覧に出る。
    /// </summary>
    public class FileColumnMappingConversionMigration : IDesignDataMigration
    {
        public string Version => "0.13.0.0";
        public string Title => Properties.Resources.FileColumnMappingConversionMigrationTitle;
        public string Description => Properties.Resources.FileColumnMappingConversionMigrationDescription;

        public void Execute(DesignData designData)
        {
#pragma warning disable CS0618 // 型またはメンバーが旧型式です
            foreach (var module in designData.Modules.ToList())
            {
                foreach (var mapping in module.Fields.OfType<FileColumnMappingFieldDesign>().ToList())
                {
                    foreach (var col in mapping.Columns.Items)
                    {
                        if (!col.HasObsoleteConversion()) continue;

                        //Field の無い列 (固定値/ブランク) の変換は使われていなかったので捨てる
                        var target = (col.Field ?? string.Empty).Split('.')[0];
                        if (string.IsNullOrEmpty(target))
                        {
                            ClearConversion(col);
                            continue;
                        }

                        var existing = module.Fields.OfType<FileValueConversionFieldDesign>().FirstOrDefault(f => f.TargetField == target);
                        if (existing == null)
                        {
                            module.Fields.Add(new FileValueConversionFieldDesign
                            {
                                Name = UniqueFieldName(module, $"{target}_Conversion"),
                                TargetField = target,
                                ConversionModule = col.ConversionModule ?? string.Empty,
                                ExternalField = col.ConversionExternalField ?? string.Empty,
                                InternalField = col.ConversionInternalField ?? string.Empty,
                            });
                            ClearConversion(col);
                        }
                        else if (IsSameConversion(existing, col))
                        {
                            ClearConversion(col);
                        }
                        //設定の違う変換が同じ対象に付いている列は残す (自動移行できない)
                    }
                }
            }
#pragma warning restore CS0618 // 型またはメンバーが旧型式です
        }

#pragma warning disable CS0618 // 型またはメンバーが旧型式です
        static bool IsSameConversion(FileValueConversionFieldDesign field, MappingColumn col)
            => field.ConversionModule == (col.ConversionModule ?? string.Empty) &&
               field.ExternalField == (col.ConversionExternalField ?? string.Empty) &&
               field.InternalField == (col.ConversionInternalField ?? string.Empty);

        static void ClearConversion(MappingColumn col)
        {
            col.ConversionModule = null;
            col.ConversionExternalField = null;
            col.ConversionInternalField = null;
        }
#pragma warning restore CS0618 // 型またはメンバーが旧型式です

        static string UniqueFieldName(ModuleDesign module, string baseName)
        {
            var name = baseName;
            for (var i = 2; module.Fields.Any(f => f.Name == name); i++) name = $"{baseName}{i}";
            return name;
        }
    }
}
