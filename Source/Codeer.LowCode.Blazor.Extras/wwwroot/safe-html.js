// 値として持っている HTML (RichTextField の値・MarkdownField の描画結果・AIChatField の返事) を画面に入れる前の消毒。
// DOMPurify (lib/dompurify) でスクリプトが動くもの (script・イベント属性・javascript: の URL 等) を取り除き、
// さらに入力部品 (form 等) と、画面の他の部分を覆える装飾 (許可していない class・style のプロパティ) を落とす。
// Blazor 側は要素の data-safe-html に HTML を書き、描画後に apply を呼ぶ。apply は消毒した HTML を中へ入れ、属性を外す
// (値が変わると Blazor がまた属性を書くので、次の apply で入れ直される)。

import createDOMPurify from './lib/dompurify/purify.es.js';

// 用途ごとの許可。classes = 残す class (完全一致) / classPrefixes = 残す class の接頭辞 / checkbox = 表示専用のチェックボックス (タスクリスト) を残す /
// newTab = target の無いリンクを別タブで開く / svg = SVG を残す (グラフ) / embeds = 動画サイトの埋め込み (Markdown の動画リンクが作る iframe) を残す
const PROFILES = {
  richtext: { classes: [], classPrefixes: [], checkbox: false, newTab: true, svg: false, embeds: false },
  markdown: { classes: ['contains-task-list', 'task-list-item', 'task-list-item-checkbox'], classPrefixes: ['language-'], checkbox: true, newTab: false, svg: false, embeds: true },
  chat: { classes: ['aichat-chart', 'contains-task-list', 'task-list-item', 'task-list-item-checkbox'], classPrefixes: ['language-'], checkbox: true, newTab: false, svg: true, embeds: true },
};

// 残す埋め込みの src (Markdown の動画リンクが作るもの)
const EMBED_SOURCES = ['https://www.youtube.com/embed/', 'https://www.youtube-nocookie.com/embed/', 'https://player.vimeo.com/video/'];
const EMBED_ATTRIBUTES = new Set(['src', 'width', 'height', 'frameborder', 'allowfullscreen']);
const SVG_NAMESPACE = 'http://www.w3.org/2000/svg';

// 残す style のプロパティ (文字・色・揃え・罫線・余白・大きさ・並べ方)。位置 (position / top / z-index 等)・変形は落とす。
// margin は負の値 (隣に重ねられる) だけ落とす
const STYLE_PROPERTY = /^(color|background-color|text-align|text-decoration(-[a-z]+)*|text-indent|font(-[a-z]+)*|line-height|letter-spacing|white-space|word-break|overflow-wrap|vertical-align|list-style-(type|position)|table-layout|border-collapse|border-spacing|border(-(top|right|bottom|left))?(-(width|style|color))?|border(-(top|bottom)-(left|right))?-radius|padding(-(top|right|bottom|left))?|margin(-(top|right|bottom|left))?|(min-|max-)?(width|height)|box-sizing|overflow(-[xy])?|display|(row-|column-)?gap|flex(-(grow|shrink|basis|wrap|direction))?|align-(items|self|content)|justify-(content|items|self)|grid-template-(columns|rows))$/;
const NEGATIVE = /-/;

const FORBID_TAGS = ['form', 'button', 'select', 'option', 'optgroup', 'textarea', 'style', 'link', 'meta', 'base'];
const CHECKBOX_ATTRIBUTES = new Set(['type', 'checked', 'disabled', 'class']);

const purify = createDOMPurify(window);
let current = PROFILES.richtext;

purify.addHook('afterSanitizeAttributes', node => {
  if (node.nodeType !== 1) return;
  //SVG ははみ出して描けると画面の他の部分を覆えるので、はみ出しの指定を落とす (既定ではみ出さない)
  const isSvg = node.namespaceURI === SVG_NAMESPACE;
  if (isSvg) node.removeAttribute('overflow');

  if (node.hasAttribute('style')) {
    const style = node.style;
    for (let i = style.length - 1; i >= 0; i--) {
      const name = style[i];
      const value = style.getPropertyValue(name);
      if (!STYLE_PROPERTY.test(name) || /url\s*\(/i.test(value) || (name.startsWith('margin') && NEGATIVE.test(value)) || (isSvg && name.startsWith('overflow'))) style.removeProperty(name);
    }
    if (style.length === 0) node.removeAttribute('style');
  }

  if (node.hasAttribute('class')) {
    const kept = Array.from(node.classList).filter(c => current.classes.includes(c) || current.classPrefixes.some(p => c.startsWith(p)));
    if (kept.length === 0) node.removeAttribute('class');
    else node.setAttribute('class', kept.join(' '));
  }

  if (node.tagName === 'INPUT') {
    if (!current.checkbox || (node.getAttribute('type') || '').toLowerCase() !== 'checkbox') {
      node.remove();
      return;
    }
    for (const attr of Array.from(node.attributes)) {
      if (!CHECKBOX_ATTRIBUTES.has(attr.name)) node.removeAttribute(attr.name);
    }
    node.setAttribute('disabled', '');
  }

  if (node.tagName === 'IFRAME') {
    const src = node.getAttribute('src') || '';
    if (!current.embeds || !EMBED_SOURCES.some(e => src.startsWith(e))) {
      node.remove();
      return;
    }
    for (const attr of Array.from(node.attributes)) {
      if (!EMBED_ATTRIBUTES.has(attr.name)) node.removeAttribute(attr.name);
    }
  }

  if (node.tagName === 'A') {
    if (current.newTab && !node.hasAttribute('target')) node.setAttribute('target', '_blank');
    //開いた先のページから元のページを操作させない
    if (node.hasAttribute('target')) node.setAttribute('rel', 'noopener noreferrer');
  }
});

/** HTML を消毒して返す。profile は PROFILES のキー。 */
export function sanitize(html, profile) {
  current = PROFILES[profile] || PROFILES.richtext;
  try {
    return purify.sanitize(html || '', {
      USE_PROFILES: { html: true, svg: current.svg },
      FORBID_TAGS,
      ADD_TAGS: current.embeds ? ['iframe'] : [],
      ADD_ATTR: current.embeds ? ['target', 'frameborder', 'allowfullscreen'] : ['target'],
      ALLOW_DATA_ATTR: false,
    });
  } finally {
    current = PROFILES.richtext;
  }
}

/** root とその中の data-safe-html を持つ要素に、消毒した HTML を入れる。プロファイルは data-safe-html-profile。 */
export function apply(root) {
  if (!root) return;
  const targets = root.matches && root.matches('[data-safe-html]') ? [root] : [];
  targets.push(...root.querySelectorAll('[data-safe-html]'));
  for (const el of targets) {
    const html = el.getAttribute('data-safe-html');
    el.removeAttribute('data-safe-html');
    el.innerHTML = sanitize(html, el.getAttribute('data-safe-html-profile'));
  }
}
