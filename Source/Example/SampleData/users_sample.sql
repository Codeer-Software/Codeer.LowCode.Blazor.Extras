-- Extras Example のユーザーのサンプル (ログインする人・承認者・メンションの候補)。sqlite_sample_extras.db に対して実行する。
-- 先に base_tables.sql を実行しておく。何度実行しても同じ結果で、今あるユーザーは消さない (同じ Id の人がいれば足さない)。
-- 部署 (dept) は、メンションの候補の条件「Dept = CurrentUser.Dept」(Home 画面の RichText) の確認用:
--   営業部 5 人 / 開発部 5 人 / 総務部 1 人 / 部署なし 2 人。ログインする人を変えて @ を打つと、同じ部署の人だけが候補に出る。
INSERT OR IGNORE INTO app_users (id, name, email, dept) VALUES
  (1, '申請 太郎', NULL, '営業部'),
  (2, '承認 花子', NULL, '営業部'),
  (3, '決裁 次郎', NULL, '開発部'),
  (4, '監査 四郎', NULL, '開発部'),
  (5, 'アドレス未登録 五郎', NULL, NULL),
  (6, '営業 さくら', 'sakura@example.com', '営業部'),
  (7, '営業 大輔', 'daisuke@example.com', '営業部'),
  (8, '営業 美咲', 'misaki@example.com', '営業部'),
  (9, '開発 健太', 'kenta@example.com', '開発部'),
  (10, '開発 由美', 'yumi@example.com', '開発部'),
  (11, '開発 翔', 'sho@example.com', '開発部'),
  (12, '総務 みどり', 'midori@example.com', '総務部'),
  (13, '無所属 六郎', 'rokuro@example.com', NULL);

-- 以前から DB を持っていて、部署がまだ入っていない人にだけ部署を入れる
UPDATE app_users SET dept = '営業部' WHERE dept IS NULL AND name IN ('申請 太郎', '承認 花子');
UPDATE app_users SET dept = '開発部' WHERE dept IS NULL AND name IN ('決裁 次郎', '監査 四郎');
