// TagField の入力欄の補助。
// - Enter・区切りの文字 (TagField.Separators。.NET が渡す) で確定、候補の上下、Esc、空欄での Backspace を .NET に送る
// - IME 変換中のキーは送らない (変換を確定する Enter も)。変換で入った全角スペースなどの区切りは、確定後の入力で .NET が分ける
// - 箱の外へフォーカスが移ったら、打ちかけの文字もタグにする。箱の中 (チップ・×・候補・余白) を押してもフォーカスは入力欄のまま

const handlers = new WeakMap();

export function initialize(input, dotNetRef, separators) {
  if (!input || handlers.has(input)) return;
  const editor = input.closest('.tag-editor');
  if (!editor) return;
  const onKeyDown = (e) => {
    if (e.isComposing || e.keyCode === 229) return;
    const confirm = e.key === 'Enter' || separators.includes(e.key);
    if (confirm || e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault();
    } else if (e.key !== 'Escape' && !(e.key === 'Backspace' && input.value === '')) {
      return;
    }
    dotNetRef.invokeMethodAsync('OnKey', confirm ? 'Enter' : e.key, input.value);
  };
  const onInput = (e) => {
    if (e.isComposing) return;
    dotNetRef.invokeMethodAsync('OnText', input.value);
  };
  // 変換の確定後に input が来ないブラウザがあるので、変換の終わりでも送る (.NET 側は同じ内容なら何も変わらない)
  const onCompositionEnd = () => dotNetRef.invokeMethodAsync('OnText', input.value);
  // ウィンドウの切り替えで外れた時 (document にフォーカスが無い) は確定しない。
  // 画面を離れるときにも blur が来るが、そのときは .NET 側が既に破棄されていることがあるので失敗は無視する
  const onBlur = () => {
    if (!document.hasFocus() || input.value.trim() === '') return;
    try { dotNetRef.invokeMethodAsync('OnBlur', input.value).catch(() => { }); } catch { }
  };
  const onMouseDown = (e) => {
    if (e.target !== input) e.preventDefault();
  };
  input.addEventListener('keydown', onKeyDown);
  input.addEventListener('input', onInput);
  input.addEventListener('compositionend', onCompositionEnd);
  input.addEventListener('blur', onBlur);
  editor.addEventListener('mousedown', onMouseDown);
  handlers.set(input, { editor, onKeyDown, onInput, onCompositionEnd, onBlur, onMouseDown });
}

export function dispose(input) {
  const h = input && handlers.get(input);
  if (!h) return;
  input.removeEventListener('keydown', h.onKeyDown);
  input.removeEventListener('input', h.onInput);
  input.removeEventListener('compositionend', h.onCompositionEnd);
  input.removeEventListener('blur', h.onBlur);
  h.editor.removeEventListener('mousedown', h.onMouseDown);
  handlers.delete(input);
}

export function setText(input, text) {
  if (input) input.value = text;
}

export function focus(input) {
  if (input && !input.disabled) input.focus();
}
