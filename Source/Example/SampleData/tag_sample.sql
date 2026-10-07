-- Extras Example のタグのサンプル (TagTest 画面の確認用)。sqlite_sample_extras.db に対して実行する。作り直し可 (DROP → CREATE → INSERT)。
-- tag_test_tags は tag-setup (タグのセットアップ) が出す DDL そのまま (タグを付けたレコードの Id + タグ名。タグのマスタは無い)。tag_tests はタグを付ける画面のテーブル。
DROP TABLE IF EXISTS tag_test_tags;
DROP TABLE IF EXISTS tags;
DROP TABLE IF EXISTS tag_tests;
CREATE TABLE tag_tests (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT);
CREATE TABLE tag_test_tags (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  owner_id INTEGER NOT NULL REFERENCES tag_tests (id) ON DELETE CASCADE,
  name TEXT NOT NULL COLLATE NOCASE
);
CREATE UNIQUE INDEX ux_tag_test_tags_owner_id_name ON tag_test_tags (owner_id, name);
CREATE INDEX ix_tag_test_tags_name ON tag_test_tags (name);
INSERT INTO tag_tests (name) VALUES ('青山商事'), ('大阪フーズ'), ('北陸精機');
-- 青山商事 = 展示会2026, DXPO / 大阪フーズ = 展示会2026 / 北陸精機 = なし
INSERT INTO tag_test_tags (owner_id, name) VALUES (1, '展示会2026'), (1, 'DXPO'), (2, '展示会2026');
