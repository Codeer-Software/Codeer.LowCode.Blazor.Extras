-- 承認待ち: ログインユーザーが今待たれている承認メンバー行 (回覧の未確認も含む)。
-- current_user_id はサーバーが束縛する予約パラメータ (クライアントから変えられない)。
-- 承認フロー側のモジュール (承認フロー / 承認メンバー) は UI を持たず、この検索用モジュールが一覧を担う。
SELECT
  m.id AS id,
  f.id AS flow_id,
  f.target_module_name AS target_module_name,
  f.target_id AS target_id,
  f.route_name AS route_name,
  u.name AS applicant_name,
  m.step_no AS step_no,
  m.step_name AS step_name,
  m.step_type AS step_type
FROM approval_flow_members m
JOIN approval_flows f ON f.id = m.flow_id AND f.attempt_no = m.attempt_no
LEFT JOIN app_users u ON u.id = f.applicant
WHERE m.approver_user = @current_user_id
  AND m.status = 'Waiting'
ORDER BY m.id DESC
