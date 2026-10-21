-- Extras Example のジョブトリガーのサンプル (JobTriggerSample / JobTriggerMissing 画面の確認用)。sqlite_sample_extras.db に対して実行する。作り直し可 (DROP → CREATE → INSERT)。
DROP TABLE IF EXISTS job_trigger_samples;
CREATE TABLE job_trigger_samples (id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT);
INSERT INTO job_trigger_samples (text) VALUES ('サンプル 1'), ('サンプル 2');
