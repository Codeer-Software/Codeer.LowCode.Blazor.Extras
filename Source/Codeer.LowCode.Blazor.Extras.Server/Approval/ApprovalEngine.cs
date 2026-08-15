using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DataIO.Db;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Properties;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;

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
                await AddHistoryAsync(ctx, flowId, 1, 0, ApprovalAction.Submit.ToDesignValue(), string.Empty,
                    ApprovalFlowStatus.InProgress.ToDesignValue(), request.Comment);

                //親レコードに FK を書く (システム経路。クライアントは送信できない)。
                //状態・申請者はフロー行が正で、条件はリンク越し参照 ((フィールド名).Status 等) で読む
                await UpdateTargetFlowIdAsync(ctx, targetId, flowId);

                await _db.CommitAsync();
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
                SetString(ctx.FlowModule, flowUpdate, ctx.Flow.RouteName, route.Name);
                SetNumber(ctx.FlowModule, flowUpdate, ctx.Flow.AttemptNo, newAttempt);
                SetNumber(ctx.FlowModule, flowUpdate, ctx.Flow.CurrentStepNo, FirstApprovalStepNo(route));
                await _updateInternalAsync(flowUpdate);

                await AddHistoryAsync(ctx, flow.Id, newAttempt, 0, ApprovalAction.Resubmit.ToDesignValue(), flow.Status,
                    ApprovalFlowStatus.InProgress.ToDesignValue(), request.Comment);

                await _db.CommitAsync();
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

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, currentStepNo, ApprovalAction.Approve.ToDesignValue(),
                flow.Status, newStatus, request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        async Task<ApprovalActionResult> RejectAsync(Context ctx, FlowRow flow, List<MemberRow> members, ApprovalActionRequest request)
        {
            if (flow.Status != ApprovalFlowStatus.InProgress.ToDesignValue()) return ApprovalActionResult.Failure(Resources.ApprovalError_InvalidState);

            var currentStepNo = GetCurrentStepNo(members);
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

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, currentStepNo, ApprovalAction.Reject.ToDesignValue(),
                flow.Status, ApprovalFlowStatus.Rejected.ToDesignValue(), request.Comment);
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
                //(対象ステップ = Waiting、それ以降 = Pending になる)
                foreach (var m in members.Where(e => e.StepType == ApprovalStepType.Approval.ToDesignValue()
                    && e.StepNo >= targetStepNo && e.StepNo <= currentStepNo
                    && (e.Status == ApprovalMemberStatus.Approved.ToDesignValue() ||
                        e.Status == ApprovalMemberStatus.Waiting.ToDesignValue())))
                {
                    await UpdateMemberStatusAsync(ctx, m, ApprovalMemberStatus.Pending.ToDesignValue(), null);
                    m.Status = ApprovalMemberStatus.Pending.ToDesignValue();
                }
                await NormalizeMemberStatusesAsync(ctx, members);

                var stepUpdate = CreateFlowUpdate(ctx, flow);
                SetNumber(ctx.FlowModule, stepUpdate, ctx.Flow.CurrentStepNo, targetStepNo);
                await _updateInternalAsync(stepUpdate);

                await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, currentStepNo, ApprovalAction.Return.ToDesignValue(),
                    flow.Status, flow.Status, request.Comment);
                return ApprovalActionResult.Success(flow.Id, flow.TargetId);
            }

            //申請者への差し戻し。進行中を離れるので残りの承認待ちはスキップにする
            //(承認待ち一覧に処理できない行を残さない。再申請時は新しい試行としてメンバーを作り直す)
            await SkipWaitingMembersAsync(ctx, members);

            var flowUpdate = CreateFlowUpdate(ctx, flow);
            SetString(ctx.FlowModule, flowUpdate, ctx.Flow.Status, ApprovalFlowStatus.Returned.ToDesignValue());
            await _updateInternalAsync(flowUpdate);

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, currentStepNo, ApprovalAction.Return.ToDesignValue(),
                flow.Status, ApprovalFlowStatus.Returned.ToDesignValue(), request.Comment);
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

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, 0, ApprovalAction.Withdraw.ToDesignValue(),
                flow.Status, ApprovalFlowStatus.Withdrawn.ToDesignValue(), request.Comment);
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

            await AddHistoryAsync(ctx, flow.Id, flow.AttemptNo, member.StepNo, ApprovalAction.Confirm.ToDesignValue(),
                flow.Status, flow.Status, request.Comment);
            return ApprovalActionResult.Success(flow.Id, flow.TargetId);
        }

        //====================================================================
        // コンテキスト・検証
        //====================================================================

        class Context
        {
            public ApprovalFlowFieldDesign FieldDesign { get; init; } = null!;
            public ModuleDesign TargetModule { get; init; } = null!;
            public ModuleDesign FlowModule { get; init; } = null!;
            public ModuleDesign MemberModule { get; init; } = null!;
            public ModuleDesign HistoryModule { get; init; } = null!;

            //契約(役割→フィールド名のマッピング)。エンジンはフィールド名をこの解決経由で読む
            public ApprovalFlowContractFieldDesign Flow { get; init; } = null!;
            public ApprovalMemberContractFieldDesign Member { get; init; } = null!;
            public ApprovalHistoryContractFieldDesign History { get; init; } = null!;

            public string ActorId { get; init; } = string.Empty;
        }

        async Task<(Context?, string)> ResolveContextAsync(string targetModuleName, string fieldName)
        {
            await _io.CheckAppAuthorization();

            var targetModule = _designData.Modules.Find(targetModuleName);
            var fieldDesign = targetModule?.Fields.OfType<ApprovalFlowFieldDesign>().FirstOrDefault(e => e.Name == fieldName);
            if (targetModule == null || fieldDesign == null || string.IsNullOrEmpty(fieldDesign.DbColumn))
                return (null, Resources.ApprovalError_DesignNotFound);

            //メンバー・履歴モジュールはフロー契約の Members / Histories 一覧の参照先として決まる
            var flowModule = _designData.Modules.Find(fieldDesign.FlowModuleName);
            var flowContract = ApprovalContracts.Flow(flowModule);
            var memberModule = _designData.Modules.Find(ApprovalContracts.GetMemberModuleName(flowModule));
            var historyModule = _designData.Modules.Find(ApprovalContracts.GetHistoryModuleName(flowModule));
            var memberContract = ApprovalContracts.Member(memberModule);
            var historyContract = ApprovalContracts.History(historyModule);
            if (flowModule == null || flowContract == null ||
                memberModule == null || memberContract == null ||
                historyModule == null || historyContract == null)
            {
                return (null, Resources.ApprovalError_DesignNotFound);
            }

            var currentUser = await _io.GetCurrentUser();
            var actorId = currentUser == null ? string.Empty : GetId(currentUser);

            return (new Context
            {
                FieldDesign = fieldDesign,
                TargetModule = targetModule,
                FlowModule = flowModule,
                MemberModule = memberModule,
                HistoryModule = historyModule,
                Flow = flowContract,
                Member = memberContract,
                History = historyContract,
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
            Applicant = (row.Fields.GetValueOrDefault(ctx.Flow.Applicant) as ValueFieldDataBase<string>)?.Value ?? string.Empty,
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
                ApproverUserId = (e.Fields.GetValueOrDefault(ctx.Member.ApproverUser) as ValueFieldDataBase<string>)?.Value ?? string.Empty,
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
            SetString(ctx.FlowModule, data, ctx.Flow.RouteName, route.Name);
            SetLink(ctx.FlowModule, data, ctx.Flow.Applicant, ctx.ActorId);
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
                    SetLink(ctx.MemberModule, data, ctx.Member.Flow, flowId);
                    SetNumber(ctx.MemberModule, data, ctx.Member.AttemptNo, attemptNo);
                    SetNumber(ctx.MemberModule, data, ctx.Member.StepNo, stepNo);
                    SetString(ctx.MemberModule, data, ctx.Member.StepName, step.Name);
                    SetString(ctx.MemberModule, data, ctx.Member.StepType, step.StepType);
                    SetString(ctx.MemberModule, data, ctx.Member.CompletionPolicy, step.CompletionPolicy);
                    SetBool(ctx.MemberModule, data, ctx.Member.IsCommentRequiredOnReject, step.IsCommentRequiredOnReject);
                    SetString(ctx.MemberModule, data, ctx.Member.ReturnScope, step.ReturnScope);
                    SetLink(ctx.MemberModule, data, ctx.Member.ApproverUser, member.UserId);
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
        //承認による前進も、ステップ差し戻しによる後退も、この1つで整合する
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
                if (member.Status == status) continue;
                await UpdateMemberStatusAsync(ctx, member, status, null);
                member.Status = status;
            }
        }

        async Task AddHistoryAsync(Context ctx, string flowId, int attemptNo, int stepNo,
            string action, string fromStatus, string toStatus, string comment)
        {
            var data = new ModuleData { Name = ctx.HistoryModule.Name };
            SetLink(ctx.HistoryModule, data, ctx.History.Flow, flowId);
            SetNumber(ctx.HistoryModule, data, ctx.History.AttemptNo, attemptNo);
            SetNumber(ctx.HistoryModule, data, ctx.History.StepNo, stepNo);
            SetString(ctx.HistoryModule, data, ctx.History.Action, action);
            SetLink(ctx.HistoryModule, data, ctx.History.ActorUser, ctx.ActorId);
            SetString(ctx.HistoryModule, data, ctx.History.FromStatus, fromStatus);
            SetString(ctx.HistoryModule, data, ctx.History.ToStatus, toStatus);
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

        //親レコードの FK と State/Applicant コピー列を書き戻す (コピー列はデザインで
        //列名が設定されているときだけ実際に書かれる = FieldData 経由の列マッピング)
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
        // フィールド値ヘルパー (既定フィールド名で厳格に読む/書く。欠落は設定ミス = 例外)
        //====================================================================

        static FieldDataBase CreateFieldData(ModuleDesign design, string fieldName)
        {
            var fieldData = design.Fields.FirstOrDefault(e => e.Name == fieldName)?.CreateData()
                ?? throw new InvalidOperationException(
                    string.Format(Resources.ApprovalCheck_RequiredFieldMissingFormat, design.Name, fieldName));
            return fieldData;
        }

        static void SetString(ModuleDesign design, ModuleData data, string fieldName, string value)
        {
            var fieldData = CreateFieldData(design, fieldName);
            ((ValueFieldDataBase<string>)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetNumber(ModuleDesign design, ModuleData data, string fieldName, int value)
        {
            var fieldData = CreateFieldData(design, fieldName);
            ((NumberFieldData)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetBool(ModuleDesign design, ModuleData data, string fieldName, bool value)
        {
            var fieldData = CreateFieldData(design, fieldName);
            ((BooleanFieldData)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetDateTime(ModuleDesign design, ModuleData data, string fieldName, DateTime? value)
        {
            var fieldData = CreateFieldData(design, fieldName);
            ((DateTimeFieldData)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static void SetLink(ModuleDesign design, ModuleData data, string fieldName, string value)
        {
            var fieldData = CreateFieldData(design, fieldName);
            ((ValueFieldDataBase<string>)fieldData).Value = value;
            data.Fields[fieldName] = fieldData;
        }

        static string GetId(ModuleData data)
            => (data.Fields.GetValueOrDefault(SystemFieldNames.Id) as IdFieldData)?.Value ?? string.Empty;

        static string GetString(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value ?? string.Empty;

        static int GetInt(ModuleData data, string fieldName)
            => (int)((data.Fields.GetValueOrDefault(fieldName) as NumberFieldData)?.Value ?? 0);

        static bool GetBool(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as BooleanFieldData)?.Value == true;

        static FieldValueMatchCondition EqualsCondition(string variable, string value) => new()
        {
            SearchTargetVariable = variable,
            Comparison = MatchComparison.Equal,
            Value = MultiTypeValue.Create(value),
        };
    }
}
