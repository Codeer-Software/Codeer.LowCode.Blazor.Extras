using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Design;
using System.Globalization;
using System.Reflection;

namespace Codeer.LowCode.Blazor.Extras.Test.DesignCheck
{
    /// <summary>
    /// デザイナの表示名 ("$キー") が Resources から解決できることの網羅チェック。
    /// 解決は本体の DisplayNameManager が「デザイン型 (と基底型) のアセンブリの Resources 型に
    /// キー名の static プロパティがあるか」で行うので、ここでも同じ規則で確認する
    /// (= resx に足して Resources.Designer.cs の手動同期を忘れると落ちる)。
    /// Extras 由来のキーは日本語も必須 (本体由来のキーの翻訳は本体の責任なので英語だけ確認する)。
    /// </summary>
    public class DesignerDisplayNameTest
    {
        static readonly Assembly _extras = typeof(ApprovalFlowContractFieldDesign).Assembly;

        //型・プロパティ・enum値に付いた [Designer(DisplayName = "$...")] のキー ("$" 込み)
        static IEnumerable<(string Key, Type DeclaringType, string Where)> GetDisplayNameKeys()
        {
            foreach (var type in _extras.GetTypes())
            {
                var onType = type.GetCustomAttribute<DesignerAttribute>();
                if (onType != null && onType.DisplayName.StartsWith('$'))
                    yield return (onType.DisplayName, type, type.Name);

                foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    var attr = member.GetCustomAttribute<DesignerAttribute>();
                    if (attr != null && attr.DisplayName.StartsWith('$'))
                        yield return (attr.DisplayName, type, $"{type.Name}.{member.Name}");
                }
            }
        }

        //DisplayNameManager と同じ探索: 型→基底型のアセンブリ順に Resources 型の static プロパティを引く
        static (string? Value, Assembly? From) Resolve(Type designType, string key, CultureInfo culture)
        {
            var backup = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = culture;
                for (var type = designType; type != null && type != typeof(object); type = type.BaseType)
                {
                    var value = FindResourcesType(type.Assembly)
                        ?.GetProperty(key[1..], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                        ?.GetValue(null)?.ToString();
                    if (!string.IsNullOrEmpty(value)) return (value, type.Assembly);
                }
                return (null, null);
            }
            finally
            {
                CultureInfo.CurrentUICulture = backup;
            }
        }

        static Type? FindResourcesType(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes().FirstOrDefault(e => e.Name == "Resources" &&
                    e.GetProperty("ResourceManager", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null);
            }
            catch (ReflectionTypeLoadException)
            {
                return null;
            }
        }

        [Test]
        public void すべての表示名キーが解決できExtras由来のキーは日本語もある()
        {
            var keys = GetDisplayNameKeys().ToList();
            Assert.That(keys, Is.Not.Empty);

            var problems = new List<string>();
            foreach (var (key, declaringType, where) in keys)
            {
                var (value, from) = Resolve(declaringType, key, CultureInfo.InvariantCulture);
                if (string.IsNullOrEmpty(value))
                {
                    problems.Add($"{where}: {key} (未定義)");
                    continue;
                }
                if (from != _extras) continue; //本体側のキー

                var (ja, _) = Resolve(declaringType, key, new CultureInfo("ja-JP"));
                if (string.IsNullOrEmpty(ja)) problems.Add($"{where}: {key} (ja-JP なし)");
            }
            Assert.That(problems, Is.Empty, () => "表示名キーの問題: " + string.Join(", ", problems));
        }

        [Test]
        public void 承認フロー契約の役割はすべて表示名を持つ()
        {
            var contracts = new[]
            {
                typeof(ApprovalFlowContractFieldDesign), typeof(ApprovalMemberContractFieldDesign),
                typeof(ApprovalHistoryContractFieldDesign),
            };

            var missing = new List<string>();
            foreach (var type in contracts)
            {
                //役割プロパティ = この型で宣言された [Designer] 付きの string プロパティ
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var attr = property.GetCustomAttribute<DesignerAttribute>();
                    if (attr == null || property.PropertyType != typeof(string)) continue;
                    if (!attr.DisplayName.StartsWith('$')) missing.Add($"{type.Name}.{property.Name}");
                }
            }
            Assert.That(missing, Is.Empty, () => "表示名 ($キー) 未設定の役割: " + string.Join(", ", missing));
        }
    }
}
