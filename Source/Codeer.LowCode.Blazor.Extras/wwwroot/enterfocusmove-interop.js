const handlerMap = new WeakMap();

const focusableSelector = [
  'input:not([type="hidden"]):not([disabled]):not([readonly])',
  'select:not([disabled])',
  'button:not([disabled])',
  '[contenteditable="true"]',
  '[tabindex]:not([tabindex="-1"])'
].join(',');

function getModuleRoot(anchor) {
  // モジュールのルート要素を探す
  // DetailPageComponent: data-module, ModuleDialog/Panel: data-module-design
  return anchor.closest('[data-module], [data-module-design]') || anchor.closest('form') || anchor.parentElement;
}

function getFocusableElements(root) {
  if (!root) return [];
  return Array.from(root.querySelectorAll(focusableSelector))
    .filter(el => {
      // 非表示要素を除外
      if (el.offsetParent === null && el.style.position !== 'fixed') return false;
      // tabindex=-1 は除外済み（セレクタ側）
      return true;
    })
    .sort((a, b) => {
      const ta = parseInt(a.getAttribute('tabindex') || '0', 10);
      const tb = parseInt(b.getAttribute('tabindex') || '0', 10);
      // tabindex > 0 が先、0はDOM順
      if (ta > 0 && tb > 0) return ta - tb;
      if (ta > 0) return -1;
      if (tb > 0) return 1;
      return 0; // DOM順を維持
    });
}

function shouldSkipEnter(target) {
  // textareaは改行が必要
  if (target.tagName === 'TEXTAREA') return true;
  // contenteditable は改行が必要
  if (target.isContentEditable) return true;
  // data-consumes-enter属性でカスタムフィールドが除外を宣言
  if (target.dataset && target.dataset.consumesEnter !== undefined) return true;
  // 同じ属性を祖先で持つ場合も除外
  if (target.closest('[data-consumes-enter]')) return true;
  return false;
}

function handleKeyDown(root, e) {
  if (e.key !== 'Enter') return;
  if (e.isComposing) return; // IME変換中は無視
  if (shouldSkipEnter(e.target)) return;

  // submit ボタンの場合はデフォルト動作を許可
  const target = e.target;
  if (target.tagName === 'BUTTON' && target.type === 'submit') return;
  if (target.tagName === 'INPUT' && target.type === 'submit') return;

  e.preventDefault();

  const focusables = getFocusableElements(root);
  const idx = focusables.indexOf(target);
  if (idx === -1) return;

  const next = idx + 1 < focusables.length ? focusables[idx + 1] : focusables[0];
  if (next) {
    next.focus();
    // input要素の場合は全選択して使いやすくする
    if (next.tagName === 'INPUT' && next.select) {
      next.select();
    }
  }
}

export function initialize(anchor) {
  const root = getModuleRoot(anchor);
  if (!root) return;

  const handler = (e) => handleKeyDown(root, e);
  handlerMap.set(anchor, { root, handler });
  root.addEventListener('keydown', handler);
}

export function dispose(anchor) {
  const entry = handlerMap.get(anchor);
  if (!entry) return;
  entry.root.removeEventListener('keydown', entry.handler);
  handlerMap.delete(anchor);
}
