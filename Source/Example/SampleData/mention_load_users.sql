-- メンションの候補の重さを確かめるためのユーザー 500 人 (docs/RichTextField.md「件数の制約」)。sqlite_sample_extras.db に対して実行する。
-- Setup (dotnet run --project Source/Example/SampleData/Setup) は、--mention-load を付けたときだけこの SQL を実行する。終わったら --mention-load-remove (mention_load_users_remove.sql) で消す。
-- 入れている間は、ユーザーの画面などほかの画面にも 500 人が出る。
--   Id: 100001 〜 100500 / 名前: 負荷確認 001 〜 負荷確認 500 / メール: load001@example.com 〜 / 部署: 営業部 / 役割: 一般
-- 何度実行しても同じ結果 (同じ Id の人がいれば足さない)。
-- 確かめ方: 営業部の人 (申請 太郎・承認 花子 など) でログインし、メンションの画面の「本文」で @ を打つ。続けて load49 などと打つと絞られる。
WITH RECURSIVE n(i) AS (
  SELECT 1
  UNION ALL
  SELECT i + 1 FROM n WHERE i < 500
)
INSERT OR IGNORE INTO app_users (id, name, email, dept, role)
SELECT 100000 + i, '負荷確認 ' || printf('%03d', i), 'load' || printf('%03d', i) || '@example.com', '営業部', '一般'
FROM n;
