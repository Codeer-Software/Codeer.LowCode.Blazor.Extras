using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;

namespace Codeer.LowCode.Blazor.Extras.Services
{
    /// <summary>
    /// <see cref="MailTokenFieldDesign"/> のサーバー側ヘルパ (PasswordHashHelper と同じ流儀)。
    /// </summary>
    /// <remarks>
    /// <see cref="ApplyMailToken"/> を <c>ModuleDataIO</c> の派生
    /// (通常 <c>CustomizedModuleDataIO.AddAsync</c> / <c>UpdateAsync</c>) から呼ぶこと。
    /// 入力フィールド (PasswordField) に値が貼り付けられている場合だけ、書き込み専用の
    /// トークン列を新しい値で更新する (空入力 = 既存トークン維持)。
    /// </remarks>
    public static class MailUserTokenHelper
    {
        public static void ApplyMailToken(ModuleDesign moduleDesign, ModuleData data)
        {
            foreach (var tokenFieldDesign in moduleDesign.Fields.OfType<MailTokenFieldDesign>())
            {
                if (string.IsNullOrEmpty(tokenFieldDesign.TokenInputFieldName)) continue;
                if (!data.Fields.TryGetValue(tokenFieldDesign.TokenInputFieldName, out var inputFieldData)) continue;
                var token = (inputFieldData as PasswordFieldData)?.Value;
                if (string.IsNullOrEmpty(token)) continue;

                data.Fields[tokenFieldDesign.Name] = new MailTokenFieldData { Token = token };
            }
        }
    }
}
