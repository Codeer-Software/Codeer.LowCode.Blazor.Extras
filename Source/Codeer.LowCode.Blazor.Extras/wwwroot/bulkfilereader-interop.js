// BulkFileReader (スクリプトの一括ファイル取込) のファイル選択。
// 隠し input[type=file] を DOM に置いてダイアログを開き、選択されたファイルを base64 で返す (キャンセルは null)。
// 自動テスト (navigator.webdriver=true) では OS ダイアログを開かず、
// テストドライバが input[data-system='bulk-file-reader'] へ直接ファイルを設定するのを待つ。
export function pickFile(accept) {
  return new Promise(resolve => {
    const input = document.createElement("input");
    input.type = "file";
    if (accept) input.accept = accept;
    input.style.display = "none";
    input.setAttribute("data-system", "bulk-file-reader");
    const cleanup = () => { input.remove(); };
    input.addEventListener("change", async () => {
      const file = input.files && input.files[0];
      if (!file) { cleanup(); resolve(null); return; }
      const bytes = new Uint8Array(await file.arrayBuffer());
      let binary = "";
      const chunk = 0x8000;
      for (let i = 0; i < bytes.length; i += chunk) {
        binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
      }
      cleanup();
      resolve({ name: file.name, contentBase64: btoa(binary) });
    });
    input.addEventListener("cancel", () => { cleanup(); resolve(null); });
    document.body.appendChild(input);
    if (!navigator.webdriver) input.click();
  });
}
