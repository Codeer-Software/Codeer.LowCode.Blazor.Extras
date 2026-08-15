using Codeer.LowCode.Blazor.DataIO;
using Codeer.LowCode.Blazor.DesignLogic;
using Codeer.LowCode.Blazor.Extras.Approval;
using Codeer.LowCode.Blazor.Extras.Data;
using Codeer.LowCode.Blazor.Extras.Designs;
using Codeer.LowCode.Blazor.Extras.Services;
using Codeer.LowCode.Blazor.OperatingModel;
using Codeer.LowCode.Blazor.Repository;
using Codeer.LowCode.Blazor.Repository.Data;
using Codeer.LowCode.Blazor.Repository.Design;
using Codeer.LowCode.Blazor.Repository.Match;
using Codeer.LowCode.Blazor.RequestInterfaces;
using Codeer.LowCode.Blazor.Script;
using Microsoft.Extensions.DependencyInjection;

namespace Codeer.LowCode.Blazor.Extras.Fields
{
    /// <summary>承認フローの1ステップの表示情報 (現在の試行のみ)。</summary>
    public class ApprovalStepView
    {
        public int StepNo { get; internal set; }
        public string StepName { get; internal set; } = string.Empty;
        public string StepType { get; internal set; } = ApprovalStepType.Approval.ToDesignValue();
        public bool IsCurrent { get; internal set; }
        public List<ApprovalMemberView> Members { get; } = new();
    }

    /// <summary>承認メンバーの表示情報。</summary>
    public class ApprovalMemberView
    {
        public string MemberId { get; internal set; } = string.Empty;
        public string UserId { get; internal set; } = string.Empty;
        public string UserDisplayText { get; internal set; } = string.Empty;
        public bool IsRequired { get; internal set; }
        public string Status { get; internal set; } = ApprovalMemberStatus.Waiting.ToDesignValue();
        public DateTime? ActedAt { get; internal set; }
    }

    /// <summary>承認履歴の表示情報。</summary>
    public class ApprovalHistoryView
    {
        public int AttemptNo { get; internal set; }
        public string Action { get; internal set; } = string.Empty;
        public string ActorDisplayText { get; internal set; } = string.Empty;
        public string Comment { get; internal set; } = string.Empty;
        public DateTime? ActedAt { get; internal set; }
    }

    /// <summary>
    /// 承認フローフィールド。FK (承認フロー行の Id) を保持し、表示用にフロー・メンバー・履歴を読む。
    /// 状態遷移はすべてサーバーの command API 経由 (このフィールドは FK を送信しない =
    /// クライアントから FK・承認状態を改ざんできない)。
    /// </summary>
    public class ApprovalFlowField(ApprovalFlowFieldDesign design) : FieldBase<ApprovalFlowFieldDesign>(design)
    {
        string _flowId = string.Empty;
        bool _isLoaded;
        bool _isBusy;

        //契約(役割→フィールド名のマッピング)の解決。メンバー・履歴モジュールは
        //フロー契約の Members / Histories 一覧の参照先として決まる。
        //契約フィールドが無い壊れたデザインでも表示側は既定名で保守的に動く(サーバーは厳格に拒否する)
        ModuleDesign? FlowModuleDesign => Services.AppInfoService.GetDesignData().Modules.Find(Design.FlowModuleName);
        ApprovalFlowContractFieldDesign FlowNames => ApprovalContracts.Flow(FlowModuleDesign) ?? new();
        ApprovalMemberContractFieldDesign MemberNames
            => ApprovalContracts.Member(Services.AppInfoService.GetDesignData().Modules.Find(MemberModuleName)) ?? new();
        ApprovalHistoryContractFieldDesign HistoryNames
            => ApprovalContracts.History(Services.AppInfoService.GetDesignData().Modules.Find(HistoryModuleName)) ?? new();
        string MemberModuleName => ApprovalContracts.GetMemberModuleName(FlowModuleDesign);
        string HistoryModuleName => ApprovalContracts.GetHistoryModuleName(FlowModuleDesign);

        /// <summary>承認フロー行の Id (未申請は空)。</summary>
        public string FlowId => _flowId;

        /// <summary>申請済みか。</summary>
        public bool IsSubmitted => !string.IsNullOrEmpty(_flowId);

        /// <summary>フロー全体の状態 (ApprovalFlowStatus の保存値。未申請は空)。</summary>
        public string FlowStatus { get; private set; } = string.Empty;

        [ScriptHide]
        public int AttemptNo { get; private set; }

        [ScriptHide]
        public int CurrentStepNo { get; private set; }

        /// <summary>楽観ロック検証値 (command API の ExpectedVersion に渡す)。</summary>
        [ScriptHide]
        public string Version { get; private set; } = string.Empty;

        /// <summary>申請者のユーザー Id (履歴の最初の Submit の実行者)。</summary>
        [ScriptHide]
        public string ApplicantUserId { get; private set; } = string.Empty;

        /// <summary>現在の試行のステップ表示情報。</summary>
        [ScriptHide]
        public List<ApprovalStepView> Steps { get; } = new();

        /// <summary>履歴表示情報 (新しい順)。</summary>
        [ScriptHide]
        public List<ApprovalHistoryView> History { get; } = new();

        /// <summary>アクションに添えるコメント (組み込みコメント欄がバインドする。スクリプトからも設定可能)。</summary>
        public string Comment { get; set; } = string.Empty;

        /// <summary>通信中か (二重実行防止)。</summary>
        [ScriptHide]
        public bool IsBusy => _isBusy;

        /// <summary>表示データ読み込み済みか。</summary>
        [ScriptHide]
        public bool IsLoaded => _isLoaded;

        //FK はサーバーだけが書くため、クライアント編集による変更は存在しない
        public override bool IsModified => false;

        [ScriptHide]
        public override async Task InitializeDataAsync(FieldDataBase? fieldDataBase)
        {
            var data = fieldDataBase as ApprovalFlowFieldData;
            _flowId = data?.Id ?? string.Empty;
            _isLoaded = false;
            FlowStatus = string.Empty;
            Steps.Clear();
            History.Clear();

            //詳細ページではモジュールスクリプト (OnAfterInitialization) が FlowStatus 等を
            //参照できるよう、初期化時点で表示データまで読み込む。
            //一覧の行モジュールでは読まない (行数分のリクエストになるため。表示は一覧列の世界)
            if (IsSubmitted && ModuleLayoutType == Repository.Design.ModuleLayoutType.Detail && !Services.AppInfoService.IsDesignMode)
            {
                await ReloadAsync();
            }
        }

        //Id は未申請なら null (空文字だと 1:N バインド条件が null 検索にならない)。
        //状態・申請者はフロー行が正 (条件はリンク越し参照 "(フィールド名).Status" 等で読む)
        [ScriptHide]
        public override FieldDataBase? GetData() => new ApprovalFlowFieldData
        {
            Id = string.IsNullOrEmpty(_flowId) ? null : _flowId,
        };

        //FK はクライアントから送信しない (サーバーの command API だけが書く)
        [ScriptHide]
        public override FieldSubmitData GetSubmitData() => new();

        [ScriptHide]
        public override async Task SetDataAsync(FieldDataBase? fieldDataBase)
            => await InitializeDataAsync(fieldDataBase);

        /// <summary>
        /// 現在ユーザーが承認待ちメンバーか (組み込み・外付けボタンの表示制御用。強制はサーバー)。
        /// Waiting = 本当に今待っている人だけ、が正規化で保証されているので単純に探すだけでよい。
        /// </summary>
        public bool CanApprove
            => FlowStatus == ApprovalFlowStatus.InProgress.ToDesignValue()
               && Steps.Where(e => e.StepType == ApprovalStepType.Approval.ToDesignValue())
                   .SelectMany(e => e.Members)
                   .Any(e => e.Status == ApprovalMemberStatus.Waiting.ToDesignValue()
                       && e.UserId == Services.AppInfoService.CurrentUserId);

        /// <summary>現在ユーザーに未確認の回覧があるか (表示制御用)。</summary>
        public bool CanConfirm
            => Steps.Where(e => e.StepType == ApprovalStepType.Confirmation.ToDesignValue())
                .SelectMany(e => e.Members)
                .Any(e => e.Status == ApprovalMemberStatus.Waiting.ToDesignValue()
                    && e.UserId == Services.AppInfoService.CurrentUserId);

        /// <summary>現在ユーザーが申請者か (表示制御用。強制はサーバー)。</summary>
        public bool IsApplicant
            => !string.IsNullOrEmpty(ApplicantUserId) && ApplicantUserId == Services.AppInfoService.CurrentUserId;

        /// <summary>取り下げできるか (申請者・進行中・WithdrawPolicy の範囲内。表示制御用。強制はサーバー)。</summary>
        public bool CanWithdraw
            => IsApplicant && FlowStatus == ApprovalFlowStatus.InProgress.ToDesignValue()
               && (Design.WithdrawPolicy == ApprovalWithdrawPolicy.Anytime
                   || !Steps.Any(s => s.StepType == ApprovalStepType.Approval.ToDesignValue()
                       && s.Members.Any(m => m.Status == ApprovalMemberStatus.Approved.ToDesignValue())));

        /// <summary>現在の差し戻し許可範囲 (現在ステップのスナップショット値)。</summary>
        [ScriptHide]
        public string CurrentReturnScope { get; private set; } = ApprovalReturnScope.ApplicantOnly.ToDesignValue();

        /// <summary>現在ステップの却下・差し戻しコメント必須設定。</summary>
        [ScriptHide]
        public bool IsCommentRequiredOnReject { get; private set; } = true;


        /// <summary>申請ボタンを出せるか (未申請・OnBuildRoute 設定済み。表示制御用)。</summary>
        public bool CanSubmit
            => !IsSubmitted && !string.IsNullOrEmpty(Design.OnBuildRoute);

        /// <summary>再申請ボタンを出せるか (再申請可能状態・申請者・OnBuildRoute 設定済み。表示制御用)。</summary>
        public bool CanResubmitNow
            => ApprovalFlowStatusLogic.CanResubmit(FlowStatus) && IsApplicant && !string.IsNullOrEmpty(Design.OnBuildRoute);

        /// <summary>経路の組み立てを開始する (スクリプト用)。</summary>
        [ScriptName("NewRoute")]
        public ApprovalRouteData NewRoute(string name) => new() { Name = name };

        /// <summary>
        /// 経路マスタから経路を読み込む (デザインの RouteModuleName が必要)。
        /// マスタはただのユーザー定義モジュールで、フィールド名は経路契約で解決する。
        /// 見つからない・契約が解決できない場合は null (OnBuildRoute で返せば申請中止)。
        /// 読み込んだ経路は AddStep / AddMember で加工してから申請してもよい。
        /// </summary>
        [ScriptName("LoadRoute")]
        public async Task<ApprovalRouteData?> LoadRouteAsync(string routeName)
        {
            if (Services.AppInfoService.IsDesignMode) return null;

            var modules = Services.AppInfoService.GetDesignData().Modules;
            var routeModule = modules.Find(Design.RouteModuleName);
            var routeNames = ApprovalContracts.Route(routeModule);
            var stepModule = modules.Find(ApprovalContracts.GetRouteStepModuleName(routeModule));
            var stepNames = ApprovalContracts.RouteStep(stepModule);
            if (routeModule == null || routeNames == null || stepModule == null || stepNames == null)
            {
                return null;
            }

            //承認者モジュール (Members 役割) はシンプル構成 (ステップ直付け ApproverUser) では使わない
            var memberModule = string.IsNullOrEmpty(stepNames.Members)
                ? null : modules.Find(ApprovalContracts.GetRouteStepMemberModuleName(stepModule));
            var memberNames = ApprovalContracts.RouteStepMember(memberModule);
            if (!string.IsNullOrEmpty(stepNames.Members) && (memberModule == null || memberNames == null))
            {
                return null;
            }

            //経路行 (経路名で1件)
            var routeRows = await GetMasterListAsync(new SearchCondition
            {
                ModuleName = routeModule.Name,
                Condition = new FieldValueMatchCondition
                {
                    SearchTargetVariable = $"{routeNames.RouteName}.Value",
                    Comparison = MatchComparison.Equal,
                    Value = MultiTypeValue.Create(routeName),
                },
                SelectFields = [SystemFieldNames.Id],
            });
            var routeId = routeRows.Count == 0 ? null : ApprovalRouteMasterLogic.GetId(routeRows[0]);
            if (string.IsNullOrEmpty(routeId)) return null;

            //ステップ行 (StepNo 順)。空の役割 (= 使わない) は取得列に入れない
            var stepSelect = new[]
                {
                    SystemFieldNames.Id, stepNames.StepNo, stepNames.StepName, stepNames.StepType,
                    stepNames.CompletionPolicy, stepNames.IsCommentRequiredOnReject, stepNames.ReturnScope,
                    stepNames.ApproverUser,
                }
                .Where(e => !string.IsNullOrEmpty(e)).Distinct().ToList();
            var stepRows = await GetMasterListAsync(new SearchCondition
            {
                ModuleName = stepModule.Name,
                Condition = new FieldValueMatchCondition
                {
                    SearchTargetVariable = $"{stepNames.Route}.Value",
                    Comparison = MatchComparison.Equal,
                    Value = MultiTypeValue.Create(routeId),
                },
                SortConditions = string.IsNullOrEmpty(stepNames.StepNo)
                    ? [] : [new SortCondition { Variable = $"{stepNames.StepNo}.Value" }],
                SelectFields = stepSelect,
            });

            //承認者行 (対象ステップ一括)。シンプル構成 (Members 役割なし) では読まない
            var memberRows = new List<ModuleData>();
            if (memberModule != null && memberNames != null && stepRows.Count > 0)
            {
                memberRows = await GetMasterListAsync(new SearchCondition
                {
                    ModuleName = memberModule.Name,
                    Condition = new FieldValueMatchCondition
                    {
                        SearchTargetVariable = $"{memberNames.Step}.Value",
                        Comparison = MatchComparison.In,
                        Value = MultiTypeValue.Create(stepRows.Select(ApprovalRouteMasterLogic.GetId).OfType<string>().ToList()),
                    },
                    SelectFields = [SystemFieldNames.Id, memberNames.Step, memberNames.ApproverUser, memberNames.IsRequired],
                });
            }

            return ApprovalRouteMasterLogic.Build(routeName, stepNames, memberNames ?? new(), stepRows, memberRows);
        }

        async Task<List<ModuleData>> GetMasterListAsync(SearchCondition condition)
            => (await Services.ModuleDataService.GetListAsync([new GetListRequest { Condition = condition }]))
                .FirstOrDefault()?.Items ?? [];

        /// <summary>申請する。経路は OnBuildRoute スクリプトが組み立てる (組み込み申請ボタンと同じ経路)。</summary>
        [ScriptName("Submit")]
        public async Task<ApprovalActionResult> SubmitAsync()
        {
            var route = await BuildRouteByScriptAsync();
            if (route == null) return ApprovalActionResult.Failure(string.Empty); //中止 (エラー表示はスクリプト側の自由)
            return await SubmitCoreAsync(route, isResubmit: false);
        }

        /// <summary>再申請する。経路は OnBuildRoute スクリプトが組み立てる。</summary>
        [ScriptName("Resubmit")]
        public async Task<ApprovalActionResult> ResubmitAsync()
        {
            var route = await BuildRouteByScriptAsync();
            if (route == null) return ApprovalActionResult.Failure(string.Empty);
            return await SubmitCoreAsync(route, isResubmit: true);
        }

        async Task<ApprovalRouteData?> BuildRouteByScriptAsync()
        {
            if (Module == null || string.IsNullOrEmpty(Design.OnBuildRoute)) return null;
            return await Module.ExecuteScriptAsync(Design.OnBuildRoute) as ApprovalRouteData;
        }

        /// <summary>
        /// 申請する (申請書の保存と同一トランザクション)。成功時は確定したレコードへ遷移する。
        /// 経路はスクリプトで組み立てたものを渡す。
        /// </summary>
        [ScriptName("SubmitWithRoute")]
        public async Task<ApprovalActionResult> SubmitWithRouteAsync(ApprovalRouteData route)
            => await SubmitCoreAsync(route, isResubmit: false);

        /// <summary>再申請する (却下・差し戻し・取り戻し後)。経路は再度組み立てて渡す。</summary>
        [ScriptName("ResubmitWithRoute")]
        public async Task<ApprovalActionResult> ResubmitWithRouteAsync(ApprovalRouteData route)
            => await SubmitCoreAsync(route, isResubmit: true);

        /// <summary>承認する。</summary>
        [ScriptName("Approve")]
        public async Task<ApprovalActionResult> ApproveAsync(string comment)
            => await ExecuteCoreAsync(ApprovalAction.Approve.ToDesignValue(), comment, null);

        /// <summary>却下する。</summary>
        [ScriptName("Reject")]
        public async Task<ApprovalActionResult> RejectAsync(string comment)
            => await ExecuteCoreAsync(ApprovalAction.Reject.ToDesignValue(), comment, null);

        /// <summary>申請者へ差し戻す。</summary>
        [ScriptName("ReturnToApplicant")]
        public async Task<ApprovalActionResult> ReturnToApplicantAsync(string comment)
            => await ExecuteCoreAsync(ApprovalAction.Return.ToDesignValue(), comment, null);

        /// <summary>過去のステップへ差し戻す (ステップ設定の ReturnScope が許す場合)。</summary>
        [ScriptName("ReturnToStep")]
        public async Task<ApprovalActionResult> ReturnToStepAsync(int stepNo, string comment)
            => await ExecuteCoreAsync(ApprovalAction.Return.ToDesignValue(), comment, stepNo);

        /// <summary>取り下げる (申請者。承認が始まる前のみ。編集して再申請できる)。</summary>
        [ScriptName("Withdraw")]
        public async Task<ApprovalActionResult> WithdrawAsync(string comment)
            => await ExecuteCoreAsync(ApprovalAction.Withdraw.ToDesignValue(), comment, null);

        /// <summary>回覧を確認済みにする。</summary>
        [ScriptName("Confirm")]
        public async Task<ApprovalActionResult> ConfirmAsync(string comment)
            => await ExecuteCoreAsync(ApprovalAction.Confirm.ToDesignValue(), comment, null);

        async Task<ApprovalActionResult> SubmitCoreAsync(ApprovalRouteData route, bool isResubmit)
        {
            if (Services.AppInfoService.IsDesignMode) return ApprovalActionResult.Failure(string.Empty);
            if (Module == null || _isBusy) return ApprovalActionResult.Failure("The field is not ready.");
            if (!await Module.ValidateInput()) return ApprovalActionResult.Failure(Properties.Resources.ApprovalInputInvalid);

            _isBusy = true;
            NotifyViewStateChanged();
            try
            {
                var request = new ApprovalSubmitRequest
                {
                    TargetModuleName = Module.Design.Name,
                    FieldName = Design.Name,
                    TargetSubmitData = Module.GetSubmitData(),
                    Route = route,
                    Comment = Comment,
                    FlowId = isResubmit ? _flowId : string.Empty,
                    ExpectedVersion = isResubmit ? Version : string.Empty,
                };
                var result = await ApprovalTransport.SubmitAsync(GetHttpService(), request);
                if (result.IsSuccess)
                {
                    Comment = string.Empty;
                    //再申請は同一 URL のため NavigateTo が no-op になる。フィールド表示は自前で最新化する
                    if (isResubmit) await ReloadAsync();
                    //保存が確定したレコードへ遷移して再初期化する (FK・編集ロック状態を含めて最新化)
                    Services.NavigationService.NavigateTo(
                        Services.NavigationService.GetModuleDataUrl(Module.Design.Name, result.TargetId));
                }
                return result;
            }
            finally
            {
                _isBusy = false;
                NotifyViewStateChanged();
            }
        }

        async Task<ApprovalActionResult> ExecuteCoreAsync(string action, string comment, int? targetStepNo)
        {
            if (Services.AppInfoService.IsDesignMode) return ApprovalActionResult.Failure(string.Empty);
            if (Module == null || _isBusy || !IsSubmitted) return ApprovalActionResult.Failure("The field is not ready.");

            _isBusy = true;
            NotifyViewStateChanged();
            try
            {
                var request = new ApprovalActionRequest
                {
                    TargetModuleName = Module.Design.Name,
                    FieldName = Design.Name,
                    FlowId = _flowId,
                    ExpectedVersion = Version,
                    Comment = comment,
                    TargetStepNo = targetStepNo,
                };
                var result = await ApprovalTransport.ExecuteAsync(GetHttpService(), action, request);
                if (result.IsSuccess)
                {
                    Comment = string.Empty;
                    await ReloadAsync();
                }
                return result;
            }
            finally
            {
                _isBusy = false;
                NotifyViewStateChanged();
            }
        }

        /// <summary>フロー・メンバー・履歴の表示データを読み込む (未申請なら何もしない)。</summary>
        [ScriptName("Reload")]
        public async Task ReloadAsync()
        {
            _isLoaded = true;
            FlowStatus = string.Empty;
            Steps.Clear();
            History.Clear();
            AttemptNo = 0;
            CurrentStepNo = 0;
            Version = string.Empty;
            ApplicantUserId = string.Empty;

            if (!IsSubmitted || Services.AppInfoService.IsDesignMode)
            {
                NotifyViewStateChanged();
                return;
            }

            var requests = new List<GetListRequest>
            {
                new() { Condition = CreateFlowCondition() },
                new() { Condition = CreateMemberCondition() },
                new() { Condition = CreateHistoryCondition() },
            };
            var pages = await Services.ModuleDataService.GetListAsync(requests);
            if (pages.Count != 3) { NotifyViewStateChanged(); return; }

            var flow = pages[0].Items.FirstOrDefault();
            if (flow != null)
            {
                FlowStatus = GetString(flow, FlowNames.Status) ?? string.Empty;
                AttemptNo = GetInt(flow, FlowNames.AttemptNo);
                CurrentStepNo = GetInt(flow, FlowNames.CurrentStepNo);
                Version = (flow.Fields.GetValueOrDefault(SystemFieldNames.OptimisticLocking) as OptimisticLockingFieldData)
                    ?.GetValue()?.ToString() ?? string.Empty;
            }

            BuildStepViews(pages[1].Items);
            BuildHistoryViews(pages[2].Items);
            NotifyViewStateChanged();
        }

        SearchCondition CreateFlowCondition() => new()
        {
            ModuleName = Design.FlowModuleName,
            Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = $"{SystemFieldNames.Id}.Value",
                Comparison = MatchComparison.Equal,
                Value = MultiTypeValue.Create(_flowId),
            },
            SelectFields =
            [
                SystemFieldNames.Id, SystemFieldNames.OptimisticLocking,
                FlowNames.Status, FlowNames.AttemptNo,
                FlowNames.CurrentStepNo,
            ],
        };

        SearchCondition CreateMemberCondition() => new()
        {
            ModuleName = MemberModuleName,
            Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = $"{MemberNames.Flow}.Value",
                Comparison = MatchComparison.Equal,
                Value = MultiTypeValue.Create(_flowId),
            },
            SortConditions =
            [
                new SortCondition { Variable = $"{MemberNames.StepNo}.Value" },
                new SortCondition { Variable = $"{SystemFieldNames.Id}.Value" },
            ],
            SelectFields =
            [
                SystemFieldNames.Id,
                MemberNames.AttemptNo, MemberNames.StepNo,
                MemberNames.StepName, MemberNames.StepType,
                MemberNames.ReturnScope, MemberNames.IsCommentRequiredOnReject,
                MemberNames.ApproverUser, MemberNames.IsRequired,
                MemberNames.Status, MemberNames.ActedAt,
            ],
        };

        SearchCondition CreateHistoryCondition() => new()
        {
            ModuleName = HistoryModuleName,
            Condition = new FieldValueMatchCondition
            {
                SearchTargetVariable = $"{HistoryNames.Flow}.Value",
                Comparison = MatchComparison.Equal,
                Value = MultiTypeValue.Create(_flowId),
            },
            SortConditions = [new SortCondition { Variable = $"{SystemFieldNames.Id}.Value", IsDescending = true }],
            SelectFields =
            [
                SystemFieldNames.Id,
                HistoryNames.AttemptNo, HistoryNames.Action,
                HistoryNames.ActorUser, HistoryNames.Comment,
                HistoryNames.ActedAt,
            ],
        };

        void BuildStepViews(List<ModuleData> members)
        {
            //現在の試行のみ表示 (過去の試行は履歴で追える)
            foreach (var member in members.Where(e => GetInt(e, MemberNames.AttemptNo) == AttemptNo))
            {
                var stepNo = GetInt(member, MemberNames.StepNo);
                var step = Steps.FirstOrDefault(e => e.StepNo == stepNo);
                if (step == null)
                {
                    step = new ApprovalStepView
                    {
                        StepNo = stepNo,
                        StepName = GetString(member, MemberNames.StepName) ?? string.Empty,
                        StepType = GetString(member, MemberNames.StepType) ?? ApprovalStepType.Approval.ToDesignValue(),
                        IsCurrent = FlowStatus == ApprovalFlowStatus.InProgress.ToDesignValue() && stepNo == CurrentStepNo,
                    };
                    Steps.Add(step);
                }

                var approver = member.Fields.GetValueOrDefault(MemberNames.ApproverUser) as LinkFieldData;
                step.Members.Add(new ApprovalMemberView
                {
                    MemberId = GetString(member, SystemFieldNames.Id) ?? string.Empty,
                    UserId = approver?.Value ?? string.Empty,
                    UserDisplayText = string.IsNullOrEmpty(approver?.DisplayText) ? approver?.Value ?? string.Empty : approver.DisplayText,
                    IsRequired = GetBool(member, MemberNames.IsRequired),
                    Status = GetString(member, MemberNames.Status) ?? ApprovalMemberStatus.Waiting.ToDesignValue(),
                    ActedAt = GetDateTime(member, MemberNames.ActedAt),
                });

                if (step.IsCurrent)
                {
                    CurrentReturnScope = GetString(member, MemberNames.ReturnScope) ?? ApprovalReturnScope.ApplicantOnly.ToDesignValue();
                    IsCommentRequiredOnReject = GetBool(member, MemberNames.IsCommentRequiredOnReject);
                }
            }
        }

        void BuildHistoryViews(List<ModuleData> histories)
        {
            foreach (var history in histories)
            {
                var actor = history.Fields.GetValueOrDefault(HistoryNames.ActorUser) as LinkFieldData;
                var entry = new ApprovalHistoryView
                {
                    AttemptNo = GetInt(history, HistoryNames.AttemptNo),
                    Action = GetString(history, HistoryNames.Action) ?? string.Empty,
                    ActorDisplayText = string.IsNullOrEmpty(actor?.DisplayText) ? actor?.Value ?? string.Empty : actor.DisplayText,
                    Comment = GetString(history, HistoryNames.Comment) ?? string.Empty,
                    ActedAt = GetDateTime(history, HistoryNames.ActedAt),
                };
                History.Add(entry);

                //申請者 = 最初の Submit の実行者 (履歴は新しい順で読むので最後に見つかったものが最古)
                if (entry.Action == ApprovalAction.Submit.ToDesignValue()) ApplicantUserId = actor?.Value ?? string.Empty;
            }
        }

        /// <summary>
        /// 承認表示のサテライトフィールド (ApprovalHistoryField 等) が購読する再描画通知。
        /// FieldBase の StateChangedReceiver は自コンポーネント占有のため別口で公開する。
        /// </summary>
        internal event Action? ViewStateChanged;

        void NotifyViewStateChanged()
        {
            NotifyStateChanged();
            ViewStateChanged?.Invoke();
        }

        IHttpService? GetHttpService() => Services.Provider?.GetService<IHttpService>();

        static string? GetString(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as ValueFieldDataBase<string>)?.Value;

        static int GetInt(ModuleData data, string fieldName)
            => (int)((data.Fields.GetValueOrDefault(fieldName) as NumberFieldData)?.Value ?? 0);

        static bool GetBool(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as BooleanFieldData)?.Value == true;

        static DateTime? GetDateTime(ModuleData data, string fieldName)
            => (data.Fields.GetValueOrDefault(fieldName) as DateTimeFieldData)?.Value;
    }
}
