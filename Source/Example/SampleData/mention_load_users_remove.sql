-- mention_load_users.sql で入れたユーザー 500 人を消す。sqlite_sample_extras.db に対して実行する (Setup に --mention-load-remove を付けると実行される)。
-- 消すのは Id が 100001 〜 100500 で、名前が「負荷確認 」で始まる人だけ。
DELETE FROM app_users WHERE id BETWEEN 100001 AND 100500 AND name LIKE '負荷確認 %';

-- Id の採番を、残っている人の最大の Id に戻す (戻さないと、次に作るユーザーの Id が 100501 になる)
UPDATE sqlite_sequence SET seq = (SELECT COALESCE(MAX(id), 0) FROM app_users) WHERE name = 'app_users';
