-- Extras Example のメンションの候補の条件のサンプル (Home 画面の RichText で @ を打つ確認用)。sqlite_sample_extras.db に対して実行する。
-- ユーザー (app_users) に部署の列を足し、部署の違う人を増やす。AppUser モジュールの Dept フィールドがこの列を読む。
-- 条件の例: 「Dept = CurrentUser.Dept」で、ログインしている人と同じ部署の人だけが候補に出る。
-- 最初の 1 行 (列の追加) は 1 回だけ実行する (2 回目はエラーになるので、やり直すときは 2 行目以降を実行する)。
ALTER TABLE app_users ADD COLUMN dept TEXT;

-- 既存の人: 申請 太郎・承認 花子 = 営業部 / 決裁 次郎・監査 四郎 = 開発部 / アドレス未登録 五郎 = 部署なし
UPDATE app_users SET dept = '営業部' WHERE id IN (1, 2);
UPDATE app_users SET dept = '開発部' WHERE id IN (3, 4);
UPDATE app_users SET dept = NULL WHERE id = 5;

-- 足す人 (作り直し可)。営業部 3 人・開発部 3 人・総務部 1 人・部署なし 1 人
DELETE FROM app_users WHERE name IN ('営業 さくら', '営業 大輔', '営業 美咲', '開発 健太', '開発 由美', '開発 翔', '総務 みどり', '無所属 六郎');
INSERT INTO app_users (name, email, dept) VALUES
  ('営業 さくら', 'sakura@example.com', '営業部'),
  ('営業 大輔', 'daisuke@example.com', '営業部'),
  ('営業 美咲', 'misaki@example.com', '営業部'),
  ('開発 健太', 'kenta@example.com', '開発部'),
  ('開発 由美', 'yumi@example.com', '開発部'),
  ('開発 翔', 'sho@example.com', '開発部'),
  ('総務 みどり', 'midori@example.com', '総務部'),
  ('無所属 六郎', 'rokuro@example.com', NULL);
