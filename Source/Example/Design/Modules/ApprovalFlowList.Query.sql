-- 承認状況: 全フローの一覧 (状態 / 経路名で検索)。
-- 承認フロー側のモジュール (承認フロー / 承認メンバー) は UI を持たず、この検索用モジュールが一覧を担う。
SELECT
  f.id AS id,
  f.status AS status,
  u.name AS applicant_name,
  f.target_module_name AS target_module_name,
  f.target_id AS target_id,
  f.route_name AS route_name,
  f.current_step_no AS current_step_no,
  (SELECT MIN(m.step_name) FROM approval_flow_members m
    WHERE m.flow_id = f.id AND m.attempt_no = f.attempt_no AND m.step_no = f.current_step_no) AS current_step_name,
  f.attempt_no AS attempt_no
FROM approval_flows f
LEFT JOIN app_users u ON u.id = f.applicant
WHERE (@status_filter IS NULL OR @status_filter = '' OR f.status = @status_filter)
  AND (@route_name_filter IS NULL OR @route_name_filter = '' OR f.route_name LIKE '%' || @route_name_filter || '%')
ORDER BY f.id DESC
