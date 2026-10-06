-- Extras Example のタグのサンプル (TagTest 画面の確認用)。sqlite_sample_extras.db に対して実行する。作り直し可 (DROP → CREATE → INSERT)。
-- tags / tag_test_tags は tag-setup (タグのセットアップ) が出す DDL そのまま。tag_tests はタグを付ける画面のテーブル。
DROP TABLE IF EXISTS tag_test_tags;
DROP TABLE IF EXISTS tags;
DROP TABLE IF EXISTS tag_tests;
CREATE TABLE tag_tests (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT);
CREATE TABLE tags (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT NOT NULL
);
CREATE UNIQUE INDEX ux_tags_name ON tags (name COLLATE NOCASE);
CREATE TABLE tag_test_tags (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  tag_test_id INTEGER NOT NULL,
  tag_id INTEGER NOT NULL
);
CREATE UNIQUE INDEX ux_tag_test_tags_tag_test_id_tag_id ON tag_test_tags (tag_test_id, tag_id);
CREATE INDEX ix_tag_test_tags_tag_id ON tag_test_tags (tag_id);
INSERT INTO tags (name) VALUES ('展示会2026'), ('DXPO'), ('セミナー'), ('既存顧客');
INSERT INTO tag_tests (name) VALUES ('青山商事'), ('大阪フーズ'), ('北陸精機');
-- 青山商事 = 展示会2026, DXPO / 大阪フーズ = 展示会2026 / 北陸精機 = なし
INSERT INTO tag_test_tags (tag_test_id, tag_id) VALUES (1, 1), (1, 2), (2, 1);
