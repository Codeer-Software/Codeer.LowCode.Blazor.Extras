-- Extras Example の問い合わせサンプル (AI チャットの確認用)。sqlite_sample_extras.db に対して実行する。作り直し可 (DROP → CREATE → INSERT)。
DROP TABLE IF EXISTS inquiries;
CREATE TABLE inquiries (id INTEGER PRIMARY KEY AUTOINCREMENT, subject TEXT, customer_id INTEGER, kind TEXT, status TEXT, received_on TEXT, body TEXT, response TEXT, is_deleted INTEGER DEFAULT 0);
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('納期の確認', 1, 'Q', '9', '2026-08-03', '先週注文したノートPC 14型がまだ届きません。いつ頃届くか教えてください。', '物流の遅延で 2 日遅れていました。8/5 に配達完了。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('請求書の宛名変更', 3, 'Q', '9', '2026-08-05', '7 月分の請求書の宛名を部署名入りにして再発行してほしい。', '宛名を「大阪フーズ 購買部」に変更して再発行しました。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('モニターに縦線が出る', 4, 'C', '9', '2026-08-10', '納品された 27型モニターの 1 台に、電源投入後しばらくすると縦線が出ます。初期不良ではないでしょうか。', '初期不良として交換対応。8/14 に代替品を発送。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('出荷遅延の再発防止を求める', 2, 'C', '5', '2026-08-18', '8 月の出荷が 2 回続けて納期に間に合わなかった。原因と再発防止策を書面で提出してほしい。', '物流委託先の変更を含めて再発防止策を作成中。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('ドッキングステーションの対応機種', 5, 'Q', '9', '2026-08-20', 'ドッキングステーションは他社製ノートPCでも使えますか。USB-C 給電の上限も知りたい。', 'USB-C (DisplayPort Alt Mode) 対応機なら使用可。給電は 65W まで。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('保守サポートの更新案内が届かない', 7, 'Q', '9', '2026-08-22', '保守サポートの期限が近いはずだが更新の案内が届いていない。契約状況を確認したい。', '9 月末期限。更新見積を送付済み。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('見積の再送依頼', 9, 'Q', '9', '2026-08-25', '先日いただいたデスクトップPC 10 台の見積書を、担当者が変わったのでもう一度メールで送ってほしい。', '新担当者宛てに再送。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('キーボードの一部キーが反応しない', 6, 'C', '9', '2026-08-28', 'ワイヤレスキーボードの一部のキー (Enter と右 Shift) が反応しないことがある。電池は新品。', 'ファームウェア更新で解消しない場合は交換する旨を案内。9/2 に交換。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('クラウドバックアップの容量追加', 12, 'R', '5', '2026-09-01', 'クラウドバックアップの容量を倍にしたい。年額の差額と切替の手順を教えてほしい。', '追加プランの見積を作成中。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('配送先の変更', 10, 'Q', '9', '2026-09-03', '9/10 出荷予定のラベルプリンタの配送先を本社から福岡営業所に変更したい。', '配送先を変更。伝票番号を連絡済み。');
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('納品が遅れて現場が止まった', 8, 'C', '1', '2026-09-05', '注文したノートPC 16型の納品が予定より 1 週間遅れ、現場の立ち上げが止まった。損害の補償について相談したい。', NULL);
INSERT INTO inquiries (subject, customer_id, kind, status, received_on, body, response) VALUES ('導入設定作業の日程調整', 11, 'Q', '1', '2026-09-08', '導入設定作業の日程を 9 月第 4 週に変更できるか。作業員の人数も教えてほしい。', NULL);
