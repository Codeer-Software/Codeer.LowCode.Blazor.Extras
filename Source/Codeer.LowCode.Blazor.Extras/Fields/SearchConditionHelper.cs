using Codeer.LowCode.Blazor.DataLogic;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Json;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    internal static class SearchConditionHelper
    {
        internal static SearchCondition ResolveSearchCondition(
            IAppInfoService appInfoService, SearchCondition searchCondition, ModuleData? moduleData)
        {
            var modules = appInfoService.GetDesignData().Modules;
            var copy = AssignValue(searchCondition, modules, moduleData);
            return AssignCurrentUserValue(copy, modules, appInfoService.CurrentUserData);
        }

        internal static Dictionary<string, FieldDataBase> AssignFromConditionValues(
            ModuleData moduleData, IAppInfoService appInfoService, SearchCondition condition)
        {
            var assignedFields = new Dictionary<string, FieldDataBase>();
            var design = appInfoService.GetDesignData().Modules.Find(condition.ModuleName);
            if (design == null) return assignedFields;

            foreach (var valueCondition in condition.GetFieldValueConditions())
            {
                if (valueCondition.Comparison != MatchComparison.Equal &&
                    valueCondition.Comparison != MatchComparison.GreaterThanOrEqual) continue;

                var fieldAndMemberName = new VariableName(valueCondition.SearchTargetVariable);
                var fieldDesign = design.Fields.FirstOrDefault(d => d.Name == fieldAndMemberName.FieldName.FullName);
                if (fieldDesign == null) continue;

                if (!moduleData.Fields.TryGetValue(fieldAndMemberName.FieldName.FullName, out var fieldData))
                {
                    fieldData = fieldDesign.CreateData();
                    if (fieldData == null) continue;
                    moduleData.Fields[fieldAndMemberName.FieldName.FullName] = fieldData;
                    assignedFields[fieldAndMemberName.FieldName.FullName] = fieldData;
                }

                var prop = fieldData.GetType().GetProperty(fieldAndMemberName.MemberName);
                if (prop == null) continue;
                prop.SetValue(fieldData, ConvertValue(valueCondition.Value.GetValue(), prop.PropertyType));
            }
            return assignedFields;
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null || value is DBNull) return null;

            var underlyingType = Nullable.GetUnderlyingType(targetType);
            if (underlyingType != null) targetType = underlyingType;

            if (targetType.IsAssignableFrom(value.GetType())) return value;

            if (targetType == typeof(DateOnly) && value is DateTime dt)
                return DateOnly.FromDateTime(dt);
            if (targetType == typeof(DateTime) && value is DateOnly d)
                return d.ToDateTime(TimeOnly.MinValue);
            if (targetType == typeof(TimeOnly) && value is TimeSpan ts)
                return new TimeOnly(ts.Hours, ts.Minutes, ts.Seconds);

            return Convert.ChangeType(value, targetType);
        }

        private static T AssignValue<T>(T searchCondition, IModuleDesigns modules, ModuleData? moduleData)
            where T : ModuleMatchCondition
            => AssignValueCore(searchCondition, modules, moduleData, null);

        private static T AssignCurrentUserValue<T>(T searchCondition, IModuleDesigns modules, ModuleData? currentUserData)
            where T : ModuleMatchCondition
            => AssignValueCore(searchCondition, modules, currentUserData, SystemFieldNames.CurrentUser);

        private static T AssignValueCore<T>(T searchCondition, IModuleDesigns modules, ModuleData? moduleData, string? specialFieldName)
            where T : ModuleMatchCondition
        {
            if (searchCondition.Condition == null) return searchCondition;

            var copy = searchCondition.JsonClone();
            if (moduleData == null) return copy;

            var design = modules.Find(moduleData.Name);

            MatchConditionBase Convert(MatchConditionBase src)
            {
                if (src is FieldVariableMatchCondition fieldVariable)
                {
                    var variable = fieldVariable.Variable;
                    if (!string.IsNullOrEmpty(specialFieldName))
                    {
                        if (!variable.StartsWith(specialFieldName + ".")) return src;
                        variable = variable.Substring(specialFieldName.Length + 1);
                    }

                    if (!moduleData.TryGetVariableValue(variable, out var value))
                    {
                        var name = new VariableName(variable);
                        var defaultField = design?.Fields.FirstOrDefault(e => e.Name == name.FieldName.FullName)?.CreateData();
                        if (defaultField == null) return src;
                        var prop = defaultField.GetType().GetProperty(name.MemberName);
                        if (prop == null) return src;
                        value = prop.GetValue(defaultField);
                    }

                    return new FieldValueMatchCondition
                    {
                        Comparison = fieldVariable.Comparison,
                        SearchTargetVariable = fieldVariable.SearchTargetVariable,
                        Value = MultiTypeValue.Create(value)
                    };
                }
                else if (src is MultiMatchCondition multiMatchCondition)
                {
                    for (int i = 0; i < multiMatchCondition.Children.Count; i++)
                    {
                        multiMatchCondition.Children[i] = Convert(multiMatchCondition.Children[i]);
                    }
                    return src;
                }
                return src;
            }

            copy.Condition = Convert(copy.Condition!);
            return copy;
        }
    }
}
