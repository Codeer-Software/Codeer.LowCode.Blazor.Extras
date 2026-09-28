const styleId = 'codeer-lowcode-extras-edit-history-css';

// 編集履歴の強調スタイル (edit-history.css) をページに 1 回だけ読み込む。
// 「この版を表示」のダイアログはコンポーネントの外に描画されるので、コンポーネントのスコープ CSS では届かない
export function ensureStyles(href) {
    if (document.getElementById(styleId)) return;
    const link = document.createElement('link');
    link.id = styleId;
    link.rel = 'stylesheet';
    link.href = href;
    document.head.appendChild(link);
}
