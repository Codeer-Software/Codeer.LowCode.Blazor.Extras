using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Mail;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using static Codeer.LowCode.Blazor.Extras.Server.ModuleDataValues;

namespace Codeer.LowCode.Blazor.Extras.Server.Approval
{
    /// <summary>
    /// 承認フローの状態遷移エンジン (command API の本体。Controller は薄く保つ)。
    /// すべての遷移をサーバーで検証し、承認モジュールへの書き込みは操作ユーザーの
    /// 権限に依存しない内部経路 (add/update デリゲート) で行う。
    /// 1リクエスト = 1トランザクション (親保存・フロー・メンバー・履歴・FK を同時に確定)。
    /// </summary>
    public class ApprovalEngine
    {
        readonly DesignData _designData;
        readonly ModuleDataIO _io;
        readonly IDbAccessor _db;
        readonly Func<ModuleData, Task<string>> _addInternalAsync;
        readonly Func<ModuleData, Task> _updateInternalAsync;

        public ApprovalEngine(DesignData designData, ModuleDataIO io, IDbAccessor db,
            Func<ModuleData, Task<string>> addInternalAsync, Func<ModuleData, Task> updateInternalAsync)
        {
            _designData = designData;
            _io = io;
            _db = db;
            _addInternalAsync = addInternalAsync;
            _updateInternalAsync = updateInternalAsync;
        }

        /// <summary>
        /// 通知メールの送信口 (任意)。設定すると、メンバー契約の TurnNotifyMail (MailField) を使って
        /// 承認の順番が回ってきたメンバーへ通知メールを送る。未設定 = 通知しない。
        /// </summary>
        public Mail.MailDispatcher? MailDispatcher { get; set; }

        /// <summary>通知メールの失敗などのログ (任意)。通知の失敗は承認操作を失敗させない。</summary>
        public Action<string>? LogError { get; set; }

        /// <summary>申請。親保存 → 経路検証 → フロー生成 → FK 設定 → 履歴 を同一トランザクションで行う。</summary>
        public async Task<ApprovalActionResult> SubmitAsync(ApprovalSubmitRequest request)
        {
            var (ctx, error) = await ResolveContextAsync(request.TargetModuleName, request.FieldName);
            if (ctx == null) return ApprovalActionResult.Failure(error);
            if (request.TargetSubmitData == null) return ApprovalActionResult.Failure(Resources.ApprovalError_TargetSaveFailedFormat.Replace("{0}", "no data"));

            var routeError = ValidateRoute(ctx, request.Route);
            if (routeError != null) return ApprovalActionResult.Failure(routeError);
            var route = request.Route!;

            _db.StartTransaction();
            try
            {
                //親 (申請書) の保存。権限チェック込みの正規経路 (未申請状態なので編集ロックは掛かっていない)
                var (targetId, saveError) = await SaveTargetAsync(request.TargetSubmitData);
                if (targetId == null)
                {
                    await _db.RollbackAsync();
                    return ApprovalActionResult.Failure(string.Format(Resources.ApprovalError_TargetSaveFailedFormat, saveError));
                }

                //二重申請ガード (FK は1本なので、同一レコードへのフローは1つだけ)
                var existing = await LoadFlowByTargetAsync(ctx, targetId);
                if (existing != null)
                {
                    await _db.RollbackAsync();
                    return ApprovalActionResult.Failure(Resources.ApprovalError_AlreadySubmitted);
                }

                //フロー生成 (申請時スナップショット)
                var flowId = await CreateFlowAsync(ctx, route, targetId, attemptNo: 1);
                await CreateMembersAsync(ctx, route, flowId, attemptNo: 1);
                await AddHistoryAsync(ctx, flowId, 1, ApprovalAction.Submit.ToDesignValue(), request.Comment);

                //親レコードに FK を書く (システム経路。クライアントは送信できない)。
                //状態・申請者はフロー行が正で、条件はリンク越し参照 ((フィールド名).Status 等) で読む
                await UpdateTargetFlowIdAsync(ctx, targetId, flowId);

                await _db.CommitAsync();
                await NotifyTurnAsync(ctx, flowId, attemptNo: 1, onlyMemberIds: null);
                return ApprovalActionResult.Success(flowId, targetId);
            }
            catch (Exception ex)
            {
                await _db.RollbackAsync();
                return ApprovalActionResult.Failure(ex.Message);
            }
        }

        /// <summary>再申請 (却下・差し戻し・取り戻し後)。経路を再検証し新しい試行としてメンバーを作り直す。</summary>
        public async Task<ApprovalActionResult> ResubmitAsync(ApprovalSubmitRequest request)
        {
            var (ctx, error) = await ResolveContextAsync(request.TargetModuleName, request.FieldName);
            if (ctx == null) return ApprovalActionResult.Failure(error);
            if (request.TargetSubmitData == null) return ApprovalActionResult.Failure(Resources.ApprovalError_TargetSaveFailedFormat.Replace("{0}", "no data"));

            var routeError = ValidateRoute(ctx, request.Route);
            if (routeError != null) return ApprovalActionResult.Failure(routeError);
            var route = request.Route!;

            _db.StartTransaction();
            try
            {
                var flow = await LoadFlowAsync(ctx, request.FlowId);
                if (flow == null) { await _db.RollbackAsync(); return ApprovalActionResult.Failure(Resources.ApprovalError_FlowNotFound); }
                if (flow.Version != request.ExpectedVersion) { await _db.RollbackAsync(); return ApprovalActionResult.Failure(Resources.ApprovalError_VersionMismatch); }
                if (!ApprovalFlowStatusLogic.CanResubmit(flow.Status)) { await _db.RollbackAsync(); return ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState); }

                if (string.IsNullOrEmpty(ctx.ActorId) || ctx.ActorId != flow.Applicant)
                {
                    await _db.RollbackAsync();
                    return ApprovalActionResult.Failure(Resources.ApprovalError_NotApplicant);
                }

                //編集内容の保存 (差し戻し・取り戻し状態は DataWriteCondition が申請者本人の編集を許す想定)
                var (targetId, saveError) = await SaveTargetAsync(request.TargetSubmitData);
                if (targetId == null)
                {
                    await _db.RollbackAsync();
                    return ApprovalActionResult.Failure(string.Format(Resources.ApprovalError_TargetSaveFailedFormat, saveError));
                }

                var newAttempt = flow.AttemptNo + 1;
                await CreateMembersAsync(ctx, route, flow.Id, newAttempt);

                var flowUpdate = CreateFlowUpdate(ctx, flow);
                SetString(ctx.FlowModule, flowUpdate, ctx.Flow.Status, ApprovalFlowStatus.InProgress.ToDesignValue());
                SetNumber(ctx.FlowModule, flowUpdate, ctx.Flow.AttemptNo, newAttempt);
                SetNumber(ctx.FlowModule, flowUpdate, ctx.Flow.CurrentStepNo, FirstApprovalStepNo(route));
                await _updateInternalAsync(flowUpdate);

                await AddHistoryAsync(ctx, flow.Id, newAttempt, ApprovalAction.Resubmit.ToDesignValue(), request.Comment);

                await _db.CommitAsync();
                await NotifyTurnAsync(ctx, flow.Id, newAttempt, onlyMemberIds: null);
                return ApprovalActionResult.Success(flow.Id, targetId);
            }
            catch (Exception ex)
            {
                await _db.RollbackAsync();
                return ApprovalActionResult.Failure(ex.Message);
            }
        }

        /// <summary>承認・却下・差し戻し・取り戻し・取消・確認。</summary>
        public async Task<ApprovalActionResult> ExecuteAsync(string action, ApprovalActionRequest request)
        {
            var (ctx, error) = await ResolveContextAsync(request.TargetModuleName, request.FieldName);
            if (ctx == null) return ApprovalActionResult.Failure(error);

            _db.StartTransaction();
            try
            {
                var flow = await LoadFlowAsync(ctx, request.FlowId);
                if (flow == null) { await _db.RollbackAsync(); return ApprovalActionResult.Failure(Resources.ApprovalError_FlowNotFound); }
                if (flow.Version != request.ExpectedVersion) { await _db.RollbackAsync(); return ApprovalActionResult.Failure(Resources.ApprovalError_VersionMismatch); }

                var members = await LoadMembersAsync(ctx, flow.Id, flow.AttemptNo);
                var waitingBefore = members
                    .Where(e => e.Status == ApprovalMemberStatus.Waiting.ToDesignValue())
                    .Select(e => e.Id).ToHashSet();
                var result = Enum.TryParse<ApprovalAction>(action, out var parsedAction) ? parsedAction switch
                {
                    ApprovalAction.Approve => await ApproveAsync(ctx, flow, members, request),
                    ApprovalAction.Reject => await RejectAsync(ctx, flow, members, request),
                    ApprovalAction.Return => await ReturnAsync(ctx, flow, members, request),
                    ApprovalAction.Withdraw => await WithdrawAsync(ctx, flow, members, request),
                    ApprovalAction.Confirm => await ConfirmAsync(ctx, flow, members, request),
                    _ => ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState),
                } : ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState);

                if (result.IsSuccess) await _db.CommitAsync();
                else await _db.RollbackAsync();

                if (result.IsSuccess)
                {
                    //この操作で順番が回ってきた (Waiting へ昇格した) メンバーだけに通知する
                    var newlyWaiting = members
                        .Where(e => e.Status == ApprovalMemberStatus.Waiting.ToDesignValue() && !waitingBefore.Contains(e.Id))
                        .Select(e => e.Id).ToHashSet();
                    if (newlyWaiting.Count > 0) await NotifyTurnAsync(ctx, flow.Id, flow.AttemptNo, newlyWaiting);
                }
                return result;
            }
            catch (Exception ex)
            {
                await _db.RollbackAsync();
                return ApprovalActionResult.Failure(ex.Message);
            }
        }

        //====================================================================
        // アクション本体 (トランザクション内で呼ばれる)
        //====================================================================

        async Task<ApprovalActionResult> ApproveAsync(Context ctx, FlowRow flow, List<MemberRow> members, ApprovalActionRequest request)
        {
            if (flow.Status != ApprovalFlowStatus.InProgress.ToDesignValue()) return ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState);

            var currentStepNo = GetCurrentStepNo(members);
            var member = FindWaitingApprover(members, ctx.ActorId);
            if (member == null) return ApprovalActionResult.Failure(Resources.ApprovalError_NotApprover);

            await UpdateMemberStatusAsync(ctx, member, ApprovalMemberStatus.Approved.ToDesignValue(), DateTime.Now);
            member.Status = ApprovalMemberStatus.Approved.ToDesignValue();

            //次ステップのメンバーを Waiting に昇格 (回覧も到達したら Waiting になる)
            await NormalizeMemberStatusesAsync(ctx, members);

            var nextStepNo = GetCurrentStepNo(members);
            var newStatus = nextStepNo == 0 ? ApprovalFlowStatus.Completed.ToDesignValue() : flow.Status;

            var flowUpdate = CreateFlowUpdate(ctx, flow);
            SetString(ctx.FlowModule, flowUpdate, ctx.Flow.Status, newStatus);
            SetNumber(ctx.FlowModule, flowUpdate, ctx.Flow.CurrentStepNo, nextStepNo == 0 ? currentStepNo : nextStepNo);
            await _updateInternalAsync(flowUpdate);

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, ApprovalAction.Approve.ToDesignValue(), request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        async Task<ApprovalActionResult> RejectAsync(Context ctx, FlowRow flow, List<MemberRow> members, ApprovalActionRequest request)
        {
            if (flow.Status != ApprovalFlowStatus.InProgress.ToDesignValue()) return ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState);

            var member = FindWaitingApprover(members, ctx.ActorId);
            if (member == null) return ApprovalActionResult.Failure(Resources.ApprovalError_NotApprover);
            if (member.IsCommentRequired && string.IsNullOrWhiteSpace(request.Comment))
                return ApprovalActionResult.Failure(Resources.ApprovalCommentRequired);

            await UpdateMemberStatusAsync(ctx, member, ApprovalMemberStatus.Rejected.ToDesignValue(), DateTime.Now);
            member.Status = ApprovalMemberStatus.Rejected.ToDesignValue();
            await SkipWaitingMembersAsync(ctx, members);

            var flowUpdate = CreateFlowUpdate(ctx, flow);
            SetString(ctx.FlowModule, flowUpdate, ctx.Flow.Status, ApprovalFlowStatus.Rejected.ToDesignValue());
            await _updateInternalAsync(flowUpdate);

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, ApprovalAction.Reject.ToDesignValue(), request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        async Task<ApprovalActionResult> ReturnAsync(Context ctx, FlowRow flow, List<MemberRow> members, ApprovalActionRequest request)
        {
            if (flow.Status != ApprovalFlowStatus.InProgress.ToDesignValue()) return ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState);

            var currentStepNo = GetCurrentStepNo(members);
            var member = FindWaitingApprover(members, ctx.ActorId);
            if (member == null) return ApprovalActionResult.Failure(Resources.ApprovalError_NotApprover);
            if (member.IsCommentRequired && string.IsNullOrWhiteSpace(request.Comment))
                return ApprovalActionResult.Failure(Resources.ApprovalCommentRequired);

            if (request.TargetStepNo is int targetStepNo)
            {
                //過去ステップへの差し戻し (現在ステップの ReturnScope が許す場合のみ)
                if (member.ReturnScope != ApprovalReturnScope.AnyPreviousStep.ToDesignValue())
                    return ApprovalActionResult.Failure(Resources.ApprovalError_ReturnNotAllowed);
                var validTarget = targetStepNo >= 1 && targetStepNo < currentStepNo
                    && members.Any(e => e.StepNo == targetStepNo && e.StepType == ApprovalStepType.Approval.ToDesignValue());
                if (!validTarget) return ApprovalActionResult.Failure(Resources.ApprovalError_ReturnNotAllowed);

                //対象〜現在の承認メンバーを未処理へ戻し、到達状態は正規化に任せる
                //(対象ステップ = Waiting、それ以降 = Pending になる。ステップ完了で Skipped になった相方も戻す)
                foreach (var m in members.Where(e => e.StepType == ApprovalStepType.Approval.ToDesignValue()
                    && e.StepNo >= targetStepNo && e.StepNo <= currentStepNo
                    && (e.Status == ApprovalMemberStatus.Approved.ToDesignValue() ||
                        e.Status == ApprovalMemberStatus.Waiting.ToDesignValue() ||
                        e.Status == ApprovalMemberStatus.Skipped.ToDesignValue())))
                {
                    await UpdateMemberStatusAsync(ctx, m, ApprovalMemberStatus.Pending.ToDesignValue(), null);
                    m.Status = ApprovalMemberStatus.Pending.ToDesignValue();
                }
                await NormalizeMemberStatusesAsync(ctx, members);

                var stepUpdate = CreateFlowUpdate(ctx, flow);
                SetNumber(ctx.FlowModule, stepUpdate, ctx.Flow.CurrentStepNo, targetStepNo);
                await _updateInternalAsync(stepUpdate);

                await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, ApprovalAction.Return.ToDesignValue(), request.Comment);
                return ApprovalActionResult.Success(flow.Id, flow.TargetId);
            }

            //申請者への差し戻し。進行中を離れるので残りの承認待ちはスキップにする
            //(承認待ち一覧に処理できない行を残さない。再申請時は新しい試行としてメンバーを作り直す)
            await SkipWaitingMembersAsync(ctx, members);

            var flowUpdate = CreateFlowUpdate(ctx, flow);
            SetString(ctx.FlowModule, flowUpdate, ctx.Flow.Status, ApprovalFlowStatus.Returned.ToDesignValue());
            await _updateInternalAsync(flowUpdate);

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, ApprovalAction.Return.ToDesignValue(), request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        async Task<ApprovalActionResult> WithdrawAsync(Context ctx, FlowRow flow, List<MemberRow> members, ApprovalActionRequest request)
        {
            if (flow.Status != ApprovalFlowStatus.InProgress.ToDesignValue()) return ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState);
            if (string.IsNullOrEmpty(ctx.ActorId) || ctx.ActorId != flow.Applicant)
                return ApprovalActionResult.Failure(Resources.ApprovalError_NotApplicant);

            //取り下げの許可範囲は業務ポリシー (デザインで可変)。既定は承認が始まる前だけ
            //(Garoon 等の「取り戻し」と同じ。承認後は承認者に差し戻してもらう)
            if (ctx.FieldDesign.WithdrawPolicy == ApprovalWithdrawPolicy.BeforeFirstApproval
                && members.Any(e => e.StepType == ApprovalStepType.Approval.ToDesignValue() && e.Status == ApprovalMemberStatus.Approved.ToDesignValue()))
            {
                return ApprovalActionResult.Failure(Resources.ApprovalError_WithdrawNotAllowed);
            }

            await SkipWaitingMembersAsync(ctx, members);

            var flowUpdate = CreateFlowUpdate(ctx, flow);
            SetString(ctx.FlowModule, flowUpdate, ctx.Flow.Status, ApprovalFlowStatus.Withdrawn.ToDesignValue());
            await _updateInternalAsync(flowUpdate);

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, ApprovalAction.Withdraw.ToDesignValue(), request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        async Task<ApprovalActionResult> ConfirmAsync(Context ctx, FlowRow flow, List<MemberRow> members, ApprovalActionRequest request)
        {
            //回覧は到達済み (= 正規化で Waiting になっている) なら確認できる。フロー終了後も可
            var member = members.FirstOrDefault(e => e.StepType == ApprovalStepType.Confirmation.ToDesignValue()
                && e.Status == ApprovalMemberStatus.Waiting.ToDesignValue()
                && e.ApproverUserId == ctx.ActorId);
            if (member == null) return ApprovalActionResult.Failure(Resources.ApprovalError_NoConfirmation);

            await UpdateMemberStatusAsync(ctx, member, ApprovalMemberStatus.Confirmed.ToDesignValue(), DateTime.Now);

            //確認はフロー状態を変えないが、楽観ロックの版だけ進めて操作を直列化する
            var flowUpdate = CreateFlowUpdate(ctx, flow);
            await _updateInternalAsync(flowUpdate);

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, ApprovalAction.Confirm.ToDesignValue(), request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        //====================================================================
        // コンテキスト・検証
        //====================================================================

        class Context
        {
            public ApprovalFlowFieldDesign FieldDesign { get; init; } = null!;
            public ModuleDesign TargetModule { get; init; } = null!;
            public ApprovalModules Modules { get; init; } = null!;
            public string ActorId { get; init; } = string.Empty;

            public ModuleDesign FlowModule => Modules.FlowModule;
            public ModuleDesign MemberModule => Modules.MemberModule;
            public ModuleDesign HistoryModule => Modules.HistoryModule;

            //契約(役割→フィールド名のマッピング)。エンジンはフィールド名をこの解決経由で読む
            public ApprovalFlowContractFieldDesign Flow => Modules.Flow;
            public ApprovalMemberContractFieldDesign Member => Modules.Member;
            public ApprovalHistoryContractFieldDesign History => Modules.History;
        }

        async Task<(Context?, string)> ResolveContextAsync(string targetModuleName, string fieldName)
        {
            await _io.CheckAppAuthorization();

            var targetModule = _designData.Modules.Find(targetModuleName);
            var fieldDesign = targetModule?.Fields.OfType<ApprovalFlowFieldDesign>().FirstOrDefault(e => e.Name == fieldName);
            if (targetModule == null || fieldDesign == null || string.IsNullOrEmpty(fieldDesign.DbColumn))
                return (null, Resources.ApprovalError_DesignNotFound);

            //承認モジュール群 (フロー / メンバー / 履歴) と契約は、クライアントと同じ解決を使う
            var modules = ApprovalModules.Resolve(_designData, fieldDesign.FlowModuleName);
            if (modules == null) return (null, Resources.ApprovalError_DesignNotFound);

            var currentUser = await _io.GetCurrentUser();
            var actorId = currentUser == null ? string.Empty : GetId(currentUser);

            return (new Context
            {
                FieldDesign = fieldDesign,
                TargetModule = targetModule,
                Modules = modules,
                ActorId = actorId,
            }, string.Empty);
        }

        static string? ValidateRoute(Context ctx, ApprovalRouteData? route)
        {
            //v1 の経路ソースはスクリプト組み立てのみ。誰が経路を組んだかは履歴に不変記録される
            if (route == null) return Resources.ApprovalError_RouteRequired;

            if (route.Steps.Count == 0)
                return string.Format(Resources.ApprovalError_InvalidRouteFormat, "no steps");
            if (!route.Steps.Any(e => e.StepType == ApprovalStepType.Approval.ToDesignValue()))
                return string.Format(Resources.ApprovalError_InvalidRouteFormat, "no approval step");

            foreach (var step in route.Steps)
            {
                if (step.StepType != ApprovalStepType.Approval.ToDesignValue() &&
                    step.StepType != ApprovalStepType.Confirmation.ToDesignValue())
                    return string.Format(Resources.ApprovalError_InvalidRouteFormat, $"unknown step type '{step.StepType}'");
                if (!Enum.TryParse<ApprovalCompletionPolicy>(step.CompletionPolicy, out _))
                    return string.Format(Resources.ApprovalError_InvalidRouteFormat, $"unknown completion policy '{step.CompletionPolicy}'");
                if (!Enum.TryParse<ApprovalReturnScope>(step.ReturnScope, out _))
                    return string.Format(Resources.ApprovalError_InvalidRouteFormat, $"unknown return scope '{step.ReturnScope}'");
                if (step.Members.Count == 0)
                    return string.Format(Resources.ApprovalError_InvalidRouteFormat, $"step '{step.Name}' has no members");
                if (step.Members.Any(e => string.IsNullOrWhiteSpace(e.UserId)))
                    return string.Format(Resources.ApprovalError_InvalidRouteFormat, $"step '{step.Name}' has an empty user id");
            }
            return null;
        }

        //====================================================================
        // 行の読み込み
        //====================================================================

        class FlowRow
        {
            public string Id { get; init; } = string.Empty;
            public string Status { get; init; } = string.Empty;
            public string TargetId { get; init; } = string.Empty;
            public string Applicant { get; init; } = string.Empty;
            public int AttemptNo { get; init; }
            public string Version { get; init; } = string.Empty;
            public OptimisticLockingFieldData? OptimisticLocking { get; init; }
        }

        class MemberRow
        {
            public string Id { get; init; } = string.Empty;
            public int StepNo { get; init; }
            public string StepType { get; init; } = ApprovalStepType.Approval.ToDesignValue();
            public string CompletionPolicy { get; init; } = ApprovalCompletionPolicy.RequiredMembers.ToDesignValue();
            public bool IsCommentRequired { get; init; }
            public string ReturnScope { get; init; } = ApprovalReturnScope.ApplicantOnly.ToDesignValue();
            public string ApproverUserId { get; init; } = string.Empty;
            public bool IsRequired { get; init; }
            public string Status { get; set; } = ApprovalMemberStatus.Waiting.ToDesignValue();
        }

        async Task<FlowRow?> LoadFlowAsync(Context ctx, string flowId)
        {
            if (string.IsNullOrEmpty(flowId)) return null;
            var condition = new SearchCondition
            {
                ModuleName = ctx.FieldDesign.FlowModuleName,
                Condition = EqualsCondition($"{SystemFieldNames.Id}.Value", flowId),
                SelectFields =
                [
                    SystemFieldNames.Id, SystemFieldNames.OptimisticLocking,
                    ctx.Flow.Status, ctx.Flow.TargetId,
                    ctx.Flow.Applicant, ctx.Flow.AttemptNo,
                ],
            };
            var row = (await _io.GetListAsync(condition, 0)).Items.FirstOrDefault();
            return row == null ? null : CreateFlowRow(ctx, row);
        }

        async Task<FlowRow?> LoadFlowByTargetAsync(Context ctx, string targetId)
        {
            var condition = new SearchCondition
            {
                ModuleName = ctx.FieldDesign.FlowModuleName,
                Condition = new MultiMatchCondition
                {
                    Children =
                    [
                        EqualsCondition($"{ctx.Flow.TargetModuleName}.Value", ctx.TargetModule.Name),
                        EqualsCondition($"{ctx.Flow.TargetId}.Value", targetId),
                    ],
                },
                SelectFields = [SystemFieldNames.Id, ctx.Flow.Status, ctx.Flow.AttemptNo, ctx.Flow.TargetId, ctx.Flow.Applicant],
            };
            var row = (await _io.GetListAsync(condition, 0)).Items.FirstOrDefault();
            return row == null ? null : CreateFlowRow(ctx, row);
        }

        static FlowRow CreateFlowRow(Context ctx, ModuleData row) => new()
        {
            Id = GetId(row),
            Status = GetString(row, ctx.Flow.Status),
            TargetId = GetString(row, ctx.Flow.TargetId),
            Applicant = GetString(row, ctx.Flow.Applicant),
            AttemptNo = GetInt(row, ctx.Flow.AttemptNo),
            Version = (row.Fields.GetValueOrDefault(SystemFieldNames.OptimisticLocking) as OptimisticLockingFieldData)?.GetValue()?.ToString() ?? string.Empty,
            OptimisticLocking = row.Fields.GetValueOrDefault(SystemFieldNames.OptimisticLocking) as OptimisticLockingFieldData,
        };

        async Task<List<MemberRow>> LoadMembersAsync(Context ctx, string flowId, int attemptNo)
        {
            var condition = new SearchCondition
            {
                ModuleName = ctx.MemberModule.Name,
                Condition = new MultiMatchCondition
                {
                    Children =
                    [
                        EqualsCondition($"{ctx.Member.Flow}.Value", flowId),
                        EqualsCondition($"{ctx.Member.AttemptNo}.Value", attemptNo.ToString()),
                    ],
                },
                SortConditions = [new SortCondition { Variable = $"{ctx.Member.StepNo}.Value" }],
                SelectFields =
                [
                    SystemFieldNames.Id,
                    ctx.Member.StepNo, ctx.Member.StepType,
                    ctx.Member.CompletionPolicy, ctx.Member.IsCommentRequiredOnReject,
                    ctx.Member.ReturnScope, ctx.Member.ApproverUser,
                    ctx.Member.IsRequired, ctx.Member.Status,
                ],
            };
            return (await _io.GetListAsync(condition, 0)).Items.Select(e => new MemberRow
            {
                Id = GetId(e),
                StepNo = GetInt(e, ctx.Member.StepNo),
                StepType = GetString(e, ctx.Member.StepType),
                CompletionPolicy = GetString(e, ctx.Member.CompletionPolicy),
                IsCommentRequired = GetBool(e, ctx.Member.IsCommentRequiredOnReject),
                ReturnScope = GetString(e, ctx.Member.ReturnScope),
                ApproverUserId = GetString(e, ctx.Member.ApproverUser),
                IsRequired = GetBool(e, ctx.Member.IsRequired),
                Status = GetString(e, ctx.Member.Status),
            }).ToList();
        }


        //====================================================================
        // 状態機械
        //====================================================================

        /// <summary>現在の承認ステップ番号。0 = 全承認ステップ完了。</summary>
        static int GetCurrentStepNo(List<MemberRow> members)
        {
            foreach (var group in members.Where(e => e.StepType == ApprovalStepType.Approval.ToDesignValue())
                         .GroupBy(e => e.StepNo).OrderBy(e => e.Key))
            {
                if (!IsStepCompleted(group.ToList())) return group.Key;
            }
            return 0;
        }

        static bool IsStepCompleted(List<MemberRow> stepMembers)
        {
            var active = stepMembers.Where(e => e.Status != ApprovalMemberStatus.Skipped.ToDesignValue()).ToList();
            if (active.Count == 0) return true;
            Enum.TryParse<ApprovalCompletionPolicy>(stepMembers[0].CompletionPolicy, out var policy);
            switch (policy)
            {
                case ApprovalCompletionPolicy.All:
                    return active.All(e => e.Status == ApprovalMemberStatus.Approved.ToDesignValue());
                case ApprovalCompletionPolicy.Any:
                    return active.Any(e => e.Status == ApprovalMemberStatus.Approved.ToDesignValue());
                default:
                    //必須全員承認。必須ゼロなら任意1人 (現行テンプレート互換)
                    var required = active.Where(e => e.IsRequired).ToList();
                    return required.Count > 0
                        ? required.All(e => e.Status == ApprovalMemberStatus.Approved.ToDesignValue())
                        : active.Any(e => e.Status == ApprovalMemberStatus.Approved.ToDesignValue());
            }
        }

        static MemberRow? FindWaitingApprover(List<MemberRow> members, string actorId)
        {
            //正規化により Waiting の承認メンバー = 現在ステップの承認待ち、が保証されている
            if (string.IsNullOrEmpty(actorId)) return null;
            return members.FirstOrDefault(e => e.StepType == ApprovalStepType.Approval.ToDesignValue()
                && e.Status == ApprovalMemberStatus.Waiting.ToDesignValue()
                && e.ApproverUserId == actorId);
        }

        static int FirstApprovalStepNo(ApprovalRouteData route)
        {
            for (var i = 0; i < route.Steps.Count; i++)
            {
                if (route.Steps[i].StepType == ApprovalStepType.Approval.ToDesignValue()) return i + 1;
            }
            return 1;
        }

        //====================================================================
        // 行の書き込み (システム経路)
        //====================================================================

        async Task<(string?, string)> SaveTargetAsync(ModuleSubmitData submitData)
        {
            var results = await _io.SubmitAsync(Guid.NewGuid(), [submitData]);
            var failed = results.FirstOrDefault(e => !string.IsNullOrEmpty(e.ExceptionMessage));
            if (failed != null) return (null, failed.ExceptionMessage);

            var result = results.FirstOrDefault();
            if (result == null) return (null, "no result");
            if (!string.IsNullOrEmpty(result.DestinationId)) return (result.DestinationId, string.Empty);
            if (result.TemporaryIdMap.TryGetValue(submitData.Id, out var real)) return (real, string.Empty);
            return (submitData.Id, string.Empty);
        }

        async Task<string> CreateFlowAsync(Context ctx, ApprovalRouteData route, string targetId, int attemptNo)
        {
            var data = new ModuleData { Name = ctx.FieldDesign.FlowModuleName };
            SetString(ctx.FlowModule, data, ctx.Flow.Status, ApprovalFlowStatus.InProgress.ToDesignValue());
            SetString(ctx.FlowModule, data, ctx.Flow.TargetModuleName, ctx.TargetModule.Name);
            SetString(ctx.FlowModule, data, ctx.Flow.TargetId, targetId);
            SetString(ctx.FlowModule, data, ctx.Flow.Applicant, ctx.ActorId);
            SetNumber(ctx.FlowModule, data, ctx.Flow.AttemptNo, attemptNo);
            SetNumber(ctx.FlowModule, data, ctx.Flow.CurrentStepNo, FirstApprovalStepNo(route));
            return await _addInternalAsync(data);
        }

        async Task CreateMembersAsync(Context ctx, ApprovalRouteData route, string flowId, int attemptNo)
        {
            //最終承認ステップ (条件式で「最終承認者」を表すためにメンバー行へスナップショット)
            var lastApprovalStepNo = 0;
            for (var i = 0; i < route.Steps.Count; i++)
            {
                if (route.Steps[i].StepType == ApprovalStepType.Approval.ToDesignValue()) lastApprovalStepNo = i + 1;
            }

            for (var i = 0; i < route.Steps.Count; i++)
            {
                var step = route.Steps[i];
                var stepNo = i + 1;

                //Waiting は「本当に今待っている人」だけ。自分より前に未完了の承認ステップが
                //あるうちは Pending (前のステップが完了したら正規化で Waiting に昇格する)
                var reached = route.Steps.Take(i).All(e => e.StepType != ApprovalStepType.Approval.ToDesignValue());
                var status = reached ? ApprovalMemberStatus.Waiting.ToDesignValue() : ApprovalMemberStatus.Pending.ToDesignValue();

                foreach (var member in step.Members)
                {
                    var data = new ModuleData { Name = ctx.MemberModule.Name };
                    SetString(ctx.MemberModule, data, ctx.Member.Flow, flowId);
                    SetNumber(ctx.MemberModule, data, ctx.Member.AttemptNo, attemptNo);
                    SetNumber(ctx.MemberModule, data, ctx.Member.StepNo, stepNo);
                    SetString(ctx.MemberModule, data, ctx.Member.StepName, step.Name);
                    SetString(ctx.MemberModule, data, ctx.Member.StepType, step.StepType);
                    SetString(ctx.MemberModule, data, ctx.Member.CompletionPolicy, step.CompletionPolicy);
                    SetBool(ctx.MemberModule, data, ctx.Member.IsCommentRequiredOnReject, step.IsCommentRequiredOnReject);
                    SetString(ctx.MemberModule, data, ctx.Member.ReturnScope, step.ReturnScope);
                    SetString(ctx.MemberModule, data, ctx.Member.ApproverUser, member.UserId);
                    SetBool(ctx.MemberModule, data, ctx.Member.IsRequired, member.IsRequired);
                    SetBool(ctx.MemberModule, data, ctx.Member.IsFinalStep,
                        step.StepType == ApprovalStepType.Approval.ToDesignValue() && stepNo == lastApprovalStepNo);
                    SetString(ctx.MemberModule, data, ctx.Member.Status, status);
                    await _addInternalAsync(data);
                }
            }
        }

        //到達状態の正規化: 未処理メンバー (Pending / Waiting) を「自分より前の承認ステップが
        //全て完了しているか」で Waiting / Pending に揃える (冪等)。
        //承認による前進も、ステップ差し戻しによる後退も、この1つで整合する。
        //完了した承認ステップに残った未処理メンバー (Any / 任意メンバーの相方) は Skipped にする
        //(承認不要になった人が承認待ちに残らない・承認できない。回覧は未確認のまま Waiting でよい)
        async Task NormalizeMemberStatusesAsync(Context ctx, List<MemberRow> members)
        {
            foreach (var member in members.Where(e =>
                e.Status == ApprovalMemberStatus.Pending.ToDesignValue() ||
                e.Status == ApprovalMemberStatus.Waiting.ToDesignValue()))
            {
                var reached = members
                    .Where(e => e.StepType == ApprovalStepType.Approval.ToDesignValue() && e.StepNo < member.StepNo)
                    .GroupBy(e => e.StepNo)
                    .All(g => IsStepCompleted(g.ToList()));
                var status = reached ? ApprovalMemberStatus.Waiting.ToDesignValue() : ApprovalMemberStatus.Pending.ToDesignValue();
                if (member.StepType == ApprovalStepType.Approval.ToDesignValue()
                    && IsStepCompleted(members.Where(e => e.StepNo == member.StepNo).ToList()))
                {
                    status = ApprovalMemberStatus.Skipped.ToDesignValue();
                }
                if (member.Status == status) continue;
                await UpdateMemberStatusAsync(ctx, member, status, null);
                member.Status = status;
            }
        }

        async Task AddHistoryAsync(Context ctx, string flowId, int attemptNo, string action, string comment)
        {
            var data = new ModuleData { Name = ctx.HistoryModule.Name };
            SetString(ctx.HistoryModule, data, ctx.History.Flow, flowId);
            SetNumber(ctx.HistoryModule, data, ctx.History.AttemptNo, attemptNo);
            SetString(ctx.HistoryModule, data, ctx.History.Action, action);
            SetString(ctx.HistoryModule, data, ctx.History.ActorUser, ctx.ActorId);
            SetString(ctx.HistoryModule, data, ctx.History.Comment, comment);
            SetDateTime(ctx.HistoryModule, data, ctx.History.ActedAt, DateTime.Now);
            await _addInternalAsync(data);
        }

        async Task UpdateMemberStatusAsync(Context ctx, MemberRow member, string status, DateTime? actedAt)
        {
            var data = new ModuleData { Name = ctx.MemberModule.Name };
            data.Fields[SystemFieldNames.Id] = new IdFieldData { Value = member.Id };
            SetString(ctx.MemberModule, data, ctx.Member.Status, status);
            SetDateTime(ctx.MemberModule, data, ctx.Member.ActedAt, actedAt);
            await _updateInternalAsync(data);
        }

        async Task SkipWaitingMembersAsync(Context ctx, List<MemberRow> members)
        {
            foreach (var member in members.Where(e =>
                e.Status == ApprovalMemberStatus.Waiting.ToDesignValue() ||
                e.Status == ApprovalMemberStatus.Pending.ToDesignValue()))
            {
                await UpdateMemberStatusAsync(ctx, member, ApprovalMemberStatus.Skipped.ToDesignValue(), null);
                member.Status = ApprovalMemberStatus.Skipped.ToDesignValue();
            }
        }

        //====================================================================
        // 通知メール (順番到達)
        //====================================================================

        //承認の順番が回ってきた (Waiting になった) メンバーへ通知メールを送る。
        //テンプレートはメンバー契約の TurnNotifyMail が指す自モジュールの MailField。
        //コミット後に同期送信し、通知の失敗は承認操作を失敗させない (ログのみ)
        async Task NotifyTurnAsync(Context ctx, string flowId, int attemptNo, HashSet<string>? onlyMemberIds)
        {
            try
            {
                var dispatcher = MailDispatcher;
                if (dispatcher == null) return;
                if (string.IsNullOrEmpty(ctx.Member.TurnNotifyMail)) return;
                if (ctx.MemberModule.Fields.FirstOrDefault(e => e.Name == ctx.Member.TurnNotifyMail)
                    is not MailFieldDesign mail) return;

                //テンプレート解決に必要なフィールドパス (リンクパス可)
                var paths = MailTemplateEngine.GetVariableNames(mail.Subject, mail.Body)
                    .Concat([mail.ToVariable, mail.CcVariable, mail.BccVariable, mail.SubjectVariable,
                        mail.BodyVariable, mail.ReplyToVariable])
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Select(e => MailVariableResolver.ParseToken(e).FieldPath)
                    .Where(e => !string.IsNullOrEmpty(e))
                    .Distinct().ToList();
                //自モジュール分 (リンクパスはルートの FK) を取得し、リンク先は後段で一括解決する
                var selectFields = paths
                    .Select(e => new FieldName(e).Root)
                    .Append(SystemFieldNames.Id)
                    .Distinct().ToList();

                var condition = new SearchCondition
                {
                    ModuleName = ctx.MemberModule.Name,
                    Condition = new MultiMatchCondition
                    {
                        Children =
                        [
                            EqualsCondition($"{ctx.Member.Flow}.Value", flowId),
                            EqualsCondition($"{ctx.Member.AttemptNo}.Value", attemptNo.ToString()),
                            EqualsCondition($"{ctx.Member.Status}.Value", ApprovalMemberStatus.Waiting.ToDesignValue()),
                        ],
                    },
                    SelectFields = selectFields,
                };
                var rows = (await _io.GetListAsync(condition, 0)).Items;
                await Mail.MailLinkPathLoader.LoadAsync(_io, _designData, ctx.MemberModule, rows, paths);
                foreach (var row in rows)
                {
                    var memberId = GetId(row);
                    if (onlyMemberIds != null && !onlyMemberIds.Contains(memberId)) continue;

                    var to = SplitAddresses(ResolveMailText(row, mail.ToVariable, mail.To));
                    if (to.Count == 0) continue; //アドレスの無いメンバーはスキップ

                    //MailField と同じ規則: 値が入っていれば値、空なら変数のフィールド値をテンプレートにする
                    var subjectTemplate = ResolveMailText(row, mail.SubjectVariable, mail.Subject);
                    var bodyTemplate = ResolveMailText(row, mail.BodyVariable, mail.Body);
                    var names = MailTemplateEngine.GetVariableNames(subjectTemplate, bodyTemplate);
                    var variables = MailVariableResolver.Resolve(ctx.MemberModule, row, names, _designData.Modules.Find);

                    //差出人はシステム (インフラ既定)。申請者への返信は ReplyToVariable で表現する
                    var result = await dispatcher.SendAsync(mail.MailInfraName, new MailMessage
                    {
                        To = to,
                        Cc = SplitAddresses(ResolveMailText(row, mail.CcVariable, mail.Cc)),
                        Bcc = SplitAddresses(ResolveMailText(row, mail.BccVariable, mail.Bcc)),
                        Subject = MailTemplateEngine.Fill(subjectTemplate, variables),
                        Body = MailTemplateEngine.Fill(bodyTemplate, variables),
                        IsBodyHtml = mail.IsBodyHtml,
                        ReplyTo = ResolveMailText(row, mail.ReplyToVariable, mail.ReplyTo),
                    }, Mail.MailDispatcher.CreateSource(ctx.MemberModule.Name, memberId));
                    if (!result.IsSuccess)
                        LogError?.Invoke($"Approval turn notification failed: {result.Failures.FirstOrDefault()?.Error}");
                }
            }
            catch (Exception ex)
            {
                LogError?.Invoke($"Approval turn notification failed: {ex.Message}");
            }
        }

        //値優先: 値が入っていればそれを使い、空なら変数を解決する (MailField と同じ規則)
        static string ResolveMailText(ModuleData data, string variable, string literal)
            => !string.IsNullOrEmpty(literal) ? literal
                : string.IsNullOrEmpty(variable) ? string.Empty
                : MailVariableResolver.GetValueText(data, variable);

        static List<string> SplitAddresses(string addresses)
            => addresses.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        //申請時に親レコードへフロー行の FK を書く (唯一の親書き込み。以降の状態遷移で親は触らない)
        async Task UpdateTargetFlowIdAsync(Context ctx, string targetId, string flowId)
        {
            var data = new ModuleData { Name = ctx.TargetModule.Name };
            data.Fields[SystemFieldNames.Id] = new IdFieldData { Value = targetId };
            var fieldData = ctx.FieldDesign.CreateData() as Codeer.LowCode.Blazor.Extras.Data.ApprovalFlowFieldData
                ?? throw new InvalidOperationException("invalid field data");
            fieldData.Id = flowId;
            data.Fields[ctx.FieldDesign.Name] = fieldData;
            await _updateInternalAsync(data);
        }

        /// <summary>フロー行更新のベース (Id + 楽観ロック。楽観ロックの WHERE 合成で操作を直列化する)。</summary>
        static ModuleData CreateFlowUpdate(Context ctx, FlowRow flow)
        {
            var data = new ModuleData { Name = ctx.FieldDesign.FlowModuleName };
            data.Fields[SystemFieldNames.Id] = new IdFieldData { Value = flow.Id };
            if (flow.OptimisticLocking != null)
                data.Fields[SystemFieldNames.OptimisticLocking] = flow.OptimisticLocking.Clone();
            return data;
        }

        //====================================================================
        // フィールド値ヘルパー (契約の役割名で厳格に読む/書く。名指しされたフィールドの欠落は設定ミス = 例外。
        // 必須でない役割は空 = 書かない。必須役割の空はデザインチェックが弾く)
        //====================================================================

        static FieldDataBase CreateFieldData(ModuleDesign design, string fieldName)
        {
            var fieldData = design.Fields.FirstOrDefault(e => e.Name == fieldName)?.CreateData()
                ?? throw new InvalidOperationException(
                    string.Format(Resources.ApprovalCheck_RequiredFieldMissingFormat, design.Name, fieldName));
            return fieldData;
        }

        //文字列系 (Text / Select / Link の FK 等。値型は ValueFieldDataBase<string>)
        static void SetString(ModuleDesign design, ModuleData data, string fieldName, string value)
        {
            if (string.IsNullOrEmpty(fieldName)) return; //任意役割の「使わない」宣言
            var fieldData = CreateFieldData(design, fieldName);
            ((ValueFieldDataBase<string>)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetNumber(ModuleDesign design, ModuleData data, string fieldName, int value)
        {
            if (string.IsNullOrEmpty(fieldName)) return; //任意役割の「使わない」宣言
            var fieldData = CreateFieldData(design, fieldName);
            ((NumberFieldData)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetBool(ModuleDesign design, ModuleData data, string fieldName, bool value)
        {
            if (string.IsNullOrEmpty(fieldName)) return; //任意役割の「使わない」宣言
            var fieldData = CreateFieldData(design, fieldName);
            ((BooleanFieldData)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetDateTime(ModuleDesign design, ModuleData data, string fieldName, DateTime? value)
        {
            if (string.IsNullOrEmpty(fieldName)) return; //任意役割の「使わない」宣言
            var fieldData = CreateFieldData(design, fieldName);
            ((DateTimeFieldData)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static FieldValueMatchCondition EqualsCondition(string variable, string value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = MatchComparison.Equal,
            Value = MultiTypeValue.Create(value),
        };
    }
}
