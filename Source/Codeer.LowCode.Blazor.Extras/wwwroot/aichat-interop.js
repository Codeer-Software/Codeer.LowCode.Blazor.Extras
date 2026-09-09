// AIChatField の入力欄と表示の補助。
// - Ctrl+Enter は常に送信、Shift+Enter は常に改行、Enter は sendOnEnter (デザインの SendOnEnter) に従う。IME 変換中は無視
// - 入力欄を内容に合わせて伸ばす (上限行数まで)
// - 返事が増えたら最下部へスクロール (ユーザーが上を読んでいる間は追従しない)
// - 返事のコピー

const handlers = new WeakMap();

function lineHeightPx(textarea) {
  const style = getComputedStyle(textarea);
  const lh = parseFloat(style.lineHeight);
  return isNaN(lh) ? parseFloat(style.fontSize) * 1.5 : lh;
}

function autoSize(textarea, maxRows) {
  const style = getComputedStyle(textarea);
  const padding = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom);
  const border = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
  const max = lineHeightPx(textarea) * maxRows + padding + border;
  textarea.style.height = 'auto';
  textarea.style.height = Math.min(textarea.scrollHeight + border, max) + 'px';
}

export function initialize(textarea, maxRows, sendOnEnter) {
  if (!textarea || handlers.has(textarea)) return;
  const onKeyDown = (e) => {
    if (e.key !== 'Enter') return;
    if (e.shiftKey) return;                          // Shift+Enter は常に改行 (既定の動作に任せる)
    if (!e.ctrlKey && sendOnEnter === false) return; // Enter で送信しない設定なら改行
    // IME 確定の Enter は送信しない
    if (e.isComposing || e.keyCode === 229) return;
    e.preventDefault();
    const row = textarea.closest('.aichat-input-row');
    const send = row && row.querySelector('.aichat-send');
    if (send && !send.disabled) send.click();
  };
  const onInput = () => autoSize(textarea, maxRows);
  textarea.addEventListener('keydown', onKeyDown);
  textarea.addEventListener('input', onInput);
  handlers.set(textarea, { onKeyDown, onInput });
  autoSize(textarea, maxRows);
}

export function dispose(textarea) {
  const h = textarea && handlers.get(textarea);
  if (!h) return;
  textarea.removeEventListener('keydown', h.onKeyDown);
  textarea.removeEventListener('input', h.onInput);
  handlers.delete(textarea);
}

export function resetInput(textarea) {
  if (!textarea) return;
  textarea.style.height = 'auto';
}

export function focus(textarea) {
  if (textarea && !textarea.disabled) textarea.focus();
}

export function scrollToBottom(container, force) {
  if (!container) return;
  const nearBottom = container.scrollHeight - container.scrollTop - container.clientHeight < 80;
  if (force || nearBottom) container.scrollTop = container.scrollHeight;
}

export async function copyReply(root, index) {
  if (!root) return;
  const reply = root.querySelector(`.aichat-msg[data-index="${index}"] .aichat-reply`);
  if (!reply) return;
  const text = reply.innerText;
  try {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      await navigator.clipboard.writeText(text);
    } else {
      const ta = document.createElement('textarea');
      ta.value = text;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }
  } catch (e) {
    console.warn('AIChat: copy failed', e);
  }
}
