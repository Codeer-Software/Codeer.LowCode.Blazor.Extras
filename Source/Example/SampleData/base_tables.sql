-- Extras Example の土台の表。sqlite_sample_extras.db に対して実行する。
-- Example の設計 (Source/Example/Design) が使う表のうち、ほかの *_sample.sql が作らないものをすべて作る。
-- どれも CREATE TABLE IF NOT EXISTS なので、何度実行しても今あるデータは消えない (すでにある表の列は変えない)。
-- 実行の順番と、以前から DB を持っている場合の注意は README.md。

-- ===== ユーザー (AppUser)。ログインする人・承認者・メンションの候補 =====
CREATE TABLE IF NOT EXISTS app_users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT,
  email TEXT,
  dept TEXT,
  role TEXT
);

-- ===== サーバーの機能が使う表 (appsettings.json に表の名前がある) =====
-- 監査ログ (AuditLog)。Extras.Server の DatabaseAuditSink が書く。appsettings.json の AuditLog が Strict なので、この表が無いとログインもできない
CREATE TABLE IF NOT EXISTS audit_log (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  occurred_at_utc TEXT NOT NULL,
  category TEXT NOT NULL,
  "action" TEXT NOT NULL,
  result TEXT NOT NULL,
  user_id TEXT,
  client_ip TEXT,
  user_agent TEXT,
  request_id TEXT,
  host TEXT,
  design_version TEXT,
  targets TEXT,
  detail TEXT
);
CREATE INDEX IF NOT EXISTS ix_audit_log_occurred_at ON audit_log (occurred_at_utc);

-- アップロード中のファイルの控え (TemporaryFileTableInfo)。添付ファイルのあるフィールド (MarkerListTest2 の画像など) が使う
CREATE TABLE IF NOT EXISTS temporary_files (
  guid TEXT,
  created_date_time TEXT
);

-- ===== メール (MailHistory) =====
CREATE TABLE IF NOT EXISTS mail_histories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  sent_at DATETIME,
  mail_infra_name TEXT,
  subject TEXT,
  total_count INTEGER,
  success_count INTEGER,
  failure_details TEXT,
  source_module TEXT,
  source_id TEXT
);

-- ===== リッチテキストのメンション (MentionTest)。restricted_body は役割が「管理者」の人だけが読める項目 =====
CREATE TABLE IF NOT EXISTS mention_tests (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT,
  body TEXT,
  restricted_body TEXT
);

-- ===== 編集履歴 (EditHistory) と、履歴を取る画面 (Order / Project / Plan) =====
CREATE TABLE IF NOT EXISTS edit_histories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  module_name TEXT,
  data_id TEXT,
  change_type TEXT,
  snapshot TEXT,
  user_id INTEGER,
  date_time DATETIME
);
CREATE TABLE IF NOT EXISTS orders (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  order_no TEXT,
  customer_name TEXT,
  order_date DATE,
  status TEXT,
  amount REAL,
  note TEXT,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS order_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  order_id INTEGER,
  product_name TEXT,
  qty REAL,
  unit_price REAL,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS projects (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT,
  note TEXT,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS project_tasks (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  project_id INTEGER,
  title TEXT,
  "start" DATETIME,
  "end" DATETIME,
  progress REAL,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS plans (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  name TEXT,
  note TEXT,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS plan_events (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  plan_id INTEGER,
  title TEXT,
  "start" DATETIME,
  "end" DATETIME,
  all_day INTEGER,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS plan_cards (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  plan_id INTEGER,
  title TEXT,
  status TEXT,
  sort_index INTEGER,
  is_deleted INTEGER DEFAULT 0
);
CREATE TABLE IF NOT EXISTS plan_markers (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  plan_id INTEGER,
  name TEXT,
  x REAL,
  y REAL,
  is_deleted INTEGER DEFAULT 0
);

-- ===== 承認フロー (Approval*) と、申請する画面 (ExpenseRequest / PurchaseRequest) =====
CREATE TABLE IF NOT EXISTS approval_routes (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  route_name TEXT
);
CREATE TABLE IF NOT EXISTS approval_route_steps (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  approval_route_id INTEGER,
  step_no INTEGER,
  step_name TEXT,
  step_type TEXT,
  completion_policy TEXT,
  is_comment_required INTEGER,
  return_scope TEXT
);
CREATE TABLE IF NOT EXISTS approval_route_step_members (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  approval_route_step_id INTEGER,
  approver_user INTEGER,
  is_required INTEGER
);
CREATE TABLE IF NOT EXISTS approval_flows (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  status TEXT,
  target_module_name TEXT,
  target_id TEXT,
  applicant INTEGER,
  attempt_no INTEGER,
  current_step_no INTEGER,
  version INTEGER
);
CREATE TABLE IF NOT EXISTS approval_flow_members (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  flow_id INTEGER,
  attempt_no INTEGER,
  step_no INTEGER,
  step_name TEXT,
  step_type TEXT,
  completion_policy TEXT,
  is_comment_required_on_reject INTEGER,
  return_scope TEXT,
  approver_user INTEGER,
  is_required INTEGER,
  is_final_step INTEGER,
  status TEXT,
  acted_at DATETIME
);
CREATE TABLE IF NOT EXISTS approval_histories (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  flow_id INTEGER,
  attempt_no INTEGER,
  "action" TEXT,
  actor_user INTEGER,
  comment TEXT,
  acted_at DATETIME
);
CREATE TABLE IF NOT EXISTS expense_requests (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT,
  amount REAL,
  approval_id INTEGER
);
CREATE TABLE IF NOT EXISTS purchase_requests (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT,
  amount REAL,
  approval_id INTEGER,
  assessed_amount REAL
);

-- ===== カレンダー (Schedule) =====
CREATE TABLE IF NOT EXISTS schedule (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT,
  "start" DATETIME,
  "end" DATETIME,
  all_day INTEGER,
  color TEXT
);

-- ===== ガントチャート (GanttTask / TaskDep) =====
CREATE TABLE IF NOT EXISTS task (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  title TEXT,
  "start" DATETIME,
  "end" DATETIME,
  progress REAL,
  processing_counter INTEGER,
  back TEXT,
  front TEXT
);
CREATE TABLE IF NOT EXISTS task_dep (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  src INTEGER,
  dst INTEGER
);

-- ===== カンバンボード (TaskItem) =====
CREATE TABLE IF NOT EXISTS task_item (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  sort_index INTEGER,
  status TEXT,
  content TEXT
);

-- ===== 画像マーカー (MarkerItem / MarkerListTest2 + MarkerItem2) =====
CREATE TABLE IF NOT EXISTS marker_item (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  x REAL,
  y REAL,
  name TEXT
);
CREATE TABLE IF NOT EXISTS marker2 (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  file_name TEXT,
  file_size INTEGER,
  file_guid TEXT
);
CREATE TABLE IF NOT EXISTS marker_item2 (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  x REAL,
  y REAL,
  name TEXT,
  parent INTEGER
);
