// AIChatField の入力欄と表示の補助。
// - Ctrl+Enter は常に送信、Shift+Enter は常に改行、Enter は sendOnEnter (デザインの SendOnEnter) に従う。IME 変換中は無視
// - 入力欄は最小行数 (デザインの MinInputRows) を確保し、内容に合わせて 12 行まで伸ばす
// - 返事が増えたら最下部へスクロール (ユーザーが上を読んでいる間は追従しない)
// - 返事のコピー

const handlers = new WeakMap();

function lineHeightPx(textarea) {
  const style = getComputedStyle(textarea);
  const lh = parseFloat(style.lineHeight);
  return isNaN(lh) ? parseFloat(style.fontSize) * 1.5 : lh;
}

const MAX_INPUT_ROWS = 12;   // これ以上は入力欄の中でスクロール (プロパティにはしない)

// 入力欄の高さ: minRows 行ぶんを最小にして、内容に合わせて MAX_INPUT_ROWS 行まで伸びる
function autoSize(textarea, minRows) {
  const style = getComputedStyle(textarea);
  const padding = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom);
  const border = parseFloat(style.borderTopWidth) + parseFloat(style.borderBottomWidth);
  const lh = lineHeightPx(textarea);
  const min = lh * minRows + padding + border;
  const max = lh * Math.max(minRows, MAX_INPUT_ROWS) + padding + border;
  textarea.style.height = 'auto';
  textarea.style.height = Math.min(Math.max(textarea.scrollHeight + border, min), max) + 'px';
}

export function initialize(textarea, minRows, sendOnEnter) {
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
  const onInput = () => autoSize(textarea, minRows);
  textarea.addEventListener('keydown', onKeyDown);
  textarea.addEventListener('input', onInput);
  handlers.set(textarea, { onKeyDown, onInput, minRows });
  autoSize(textarea, minRows);
}

export function dispose(textarea) {
  const h = textarea && handlers.get(textarea);
  if (!h) return;
  textarea.removeEventListener('keydown', h.onKeyDown);
  textarea.removeEventListener('input', h.onInput);
  handlers.delete(textarea);
}

// 送信後: 内容が空になった高さ (= 最小行数) に戻す
export function resetInput(textarea) {
  if (!textarea) return;
  const h = handlers.get(textarea);
  if (h) autoSize(textarea, h.minRows); else textarea.style.height = 'auto';
}

export function focus(textarea) {
  if (textarea && !textarea.disabled) textarea.focus();
}

// 追従の状態 (会話領域ごと)。follow = 最下部に追従する / programmatic = 自分で scrollTop を動かした直後 (その scroll イベントはユーザー操作ではない)
const followStates = new WeakMap();

// 会話領域の追従を始める。ユーザーが上へスクロールしたら追従を止め、最下部近くまで戻したら再開する。
// 「描画後に最下部との距離を見る」方式だと、表やグラフが一度に大きく入ったときに距離が開いて追従が外れるので、状態で持つ
export function initializeMessages(container) {
  if (!container || followStates.has(container)) return;
  const state = { follow: true, programmatic: false };
  const onScroll = () => {
    if (state.programmatic) { state.programmatic = false; return; }
    state.follow = container.scrollHeight - container.scrollTop - container.clientHeight < 40;
  };
  container.addEventListener('scroll', onScroll);
  state.onScroll = onScroll;
  followStates.set(container, state);
}

export function disposeMessages(container) {
  const state = container && followStates.get(container);
  if (!state) return;
  container.removeEventListener('scroll', state.onScroll);
  followStates.delete(container);
}

// 返事や途中経過が描かれた後に呼ぶ。force (自分が送った直後) は必ず最下部へ行き追従も再開する。
// それ以外は追従中のときだけ最下部へ。返事の逐次更新・完了のたびに呼ばれるので、追従中は常に最新の返事が見える
export function scrollToBottom(container, force) {
  if (!container) return;
  const state = followStates.get(container);
  if (state && force) state.follow = true;
  if (!force && state && !state.follow) return;
  const scroll = () => {
    const target = container.scrollHeight - container.clientHeight;
    if (Math.abs(container.scrollTop - target) < 1) return;
    if (state) state.programmatic = true;
    // CSS の scroll-behavior:smooth だと途中経過の scroll イベントが何度も飛び、ユーザー操作と区別できなくなるので即時に動かす
    container.scrollTo({ top: target, behavior: 'instant' });
  };
  scroll();
  requestAnimationFrame(scroll);   // 画像や表の幅が確定した後の高さにも合わせる
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
