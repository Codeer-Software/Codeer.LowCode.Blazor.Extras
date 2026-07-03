using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Script;
using QRCoder;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    public class QrCodeField(QrCodeFieldDesign design) : FieldBase<QrCodeFieldDesign>(design)
    {
        private string _text = string.Empty;
        private string _dataUrl = string.Empty;

        /// <summary>
        /// QR化する文字列。スクリプトから設定すると即座に再描画される。
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                value ??= string.Empty;
                if (_text == value) return;
                _text = value;
                UpdateDataUrl();
                NotifyStateChanged();
            }
        }

        /// <summary>生成した QR コードの data URI (PNG)。内容が空/生成失敗時は空文字。</summary>
        [ScriptHide]
        public string DataUrl => _dataUrl;

        [ScriptHide]
        public override bool IsModified => false;

        [ScriptHide]
        public override FieldDataBase? GetData() => null;

        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override Task SetDataAsync(FieldDataBase? fieldDataBase) => Task.CompletedTask;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            await Task.CompletedTask;
            _text = GetInitialText();
            UpdateDataUrl();
        }

        [ScriptHide]
        public override async Task OnExternalFieldChangedAsync(string fieldName)
        {
            await Task.CompletedTask;
            if (string.IsNullOrEmpty(Design.SourceField) || fieldName != Design.SourceField) return;
            Text = GetSourceText();
        }

        private string GetInitialText()
            => string.IsNullOrEmpty(Design.SourceField) ? (Design.Text ?? string.Empty) : GetSourceText();

        private string GetSourceText()
        {
            if (string.IsNullOrEmpty(Design.SourceField)) return string.Empty;
            var data = Module.GetField(Design.SourceField)?.GetData();
            return data switch
            {
                ValueFieldDataBase<string> s => s.Value ?? string.Empty,
                ValueFieldDataBase<decimal?> n => n.Value?.ToString() ?? string.Empty,
                _ => string.Empty,
            };
        }

        private void UpdateDataUrl()
        {
            if (string.IsNullOrEmpty(_text))
            {
                _dataUrl = string.Empty;
                return;
            }

            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(_text, ToEccLevel(Design.EccLevel));
                var png = new PngByteQRCode(data);
                var bytes = png.GetGraphic(
                    pixelsPerModule: 10,
                    darkColorRgba: ToRgba(Design.DarkColor, new byte[] { 0, 0, 0, 255 }),
                    lightColorRgba: ToRgba(Design.LightColor, new byte[] { 255, 255, 255, 255 }),
                    drawQuietZones: true);
                _dataUrl = "data:image/png;base64," + Convert.ToBase64String(bytes);
            }
            catch
            {
                // 文字数超過など生成不能時は非表示にする
                _dataUrl = string.Empty;
            }
        }

        private static QRCodeGenerator.ECCLevel ToEccLevel(QrCodeEccLevel level) => level switch
        {
            QrCodeEccLevel.L => QRCodeGenerator.ECCLevel.L,
            QrCodeEccLevel.Q => QRCodeGenerator.ECCLevel.Q,
            QrCodeEccLevel.H => QRCodeGenerator.ECCLevel.H,
            _ => QRCodeGenerator.ECCLevel.M,
        };

        private static byte[] ToRgba(string? hex, byte[] fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 3)
                s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
            if (s.Length != 6) return fallback;
            try
            {
                var r = Convert.ToByte(s.Substring(0, 2), 16);
                var g = Convert.ToByte(s.Substring(2, 2), 16);
                var b = Convert.ToByte(s.Substring(4, 2), 16);
                return new byte[] { r, g, b, 255 };
            }
            catch
            {
                return fallback;
            }
        }
    }
}
