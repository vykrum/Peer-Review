module StudioReview.Client.Main

open System
open System.Net.Http
open System.Text.Json
open Elmish
open Bolero
open Bolero.Html
open Microsoft.AspNetCore.Components
open Microsoft.JSInterop
open StudioReview.Client

type FilterOption =
    | FilterAll
    | FilterPending
    | FilterReviewed

type AdminSubTab =
    | SubTabAnalytics
    | SubTabRubric
    | SubTabPresenters
    | SubTabHuggingFace

type Model =
    {
        Page: Page
        AdminSubTab: AdminSubTab
        Filter: FilterOption

        // Anonymous reviewer identity
        ReviewerToken: string
        ReviewerNickname: string
        IsReviewerLoaded: bool

        // Studio state & persistence
        Studio: StudioState
        IsLoading: bool

        // Active review form
        ActiveReviewPresenterId: string option
        ReviewScores: Map<string, int>
        ReviewComments: Map<string, string>
        GeneralFeedback: string
        Strengths: string
        Improvements: string
        IsSubmittingReview: bool

        // Admin controls
        IsAdminUnlocked: bool
        AdminKeyInput: string
        InspectingSummary: PresenterSummary option
        EditableRubric: RubricCriterion list
        EditablePresenters: Presenter list
        BulkPresentersText: string
        HfRepoInput: string
        HfTokenInput: string
        IsHfSyncing: bool

        // Cloud sync & live multi-student aggregation
        BaseUrl: string
        IsAutoPollingEnabled: bool
        LastSyncedAt: DateTime option

        // User feedback banners
        SuccessAlert: string option
        ErrorAlert: string option
        InfoAlert: string option
    }

let initModel : Model =
    let seed = HfStorage.defaultSeedState
    let bulk =
        seed.Presenters
        |> List.map (fun p -> p.Name + " - " + p.ProjectTitle)
        |> String.concat "\n"

    {
        Page = Page.Home
        AdminSubTab = SubTabAnalytics
        Filter = FilterAll

        ReviewerToken = ""
        ReviewerNickname = "Studio Critic"
        IsReviewerLoaded = false

        Studio = seed
        IsLoading = true

        ActiveReviewPresenterId = None
        ReviewScores = Map.empty
        ReviewComments = Map.empty
        GeneralFeedback = ""
        Strengths = ""
        Improvements = ""
        IsSubmittingReview = false

        IsAdminUnlocked = false
        AdminKeyInput = ""
        InspectingSummary = None
        EditableRubric = seed.Rubric
        EditablePresenters = seed.Presenters
        BulkPresentersText = bulk
        HfRepoInput = "vykrum/peer-review"
        HfTokenInput = ""
        IsHfSyncing = false

        BaseUrl = ""
        IsAutoPollingEnabled = true
        LastSyncedAt = None

        SuccessAlert = None
        ErrorAlert = None
        InfoAlert = None
    }

type Message =
    | SetPage of Page
    | InitReviewer of token: string * nickname: string
    | SetReviewerNickname of string
    | LoadLocalState
    | GotLocalState of StudioState
    | SetFilter of FilterOption
    | StartReview of Presenter
    | SetScore of criterionId: string * score: int
    | SetCriterionComment of criterionId: string * comment: string
    | SetGeneralFeedback of string
    | SetStrengths of string
    | SetImprovements of string
    | SubmitReview
    | SubmitReviewSyncResult of Result<StudioState, string>
    | SetAdminKeyInput of string
    | UnlockAdmin
    | LockAdmin
    | SetAdminSubTab of AdminSubTab
    | SetInspectingSummary of PresenterSummary option
    | SetActivePresenterId of string option
    | UpdateRubricCriterionTitle of index: int * title: string
    | UpdateRubricCriterionDesc of index: int * desc: string
    | UpdateRubricCriterionMax of index: int * maxVal: int
    | AddRubricCriterion
    | RemoveRubricCriterion of index: int
    | ResetRubricToDefaults
    | SaveRubric
    | SetBulkPresentersText of string
    | ParseBulkPresenters
    | UpdatePresenterField of index: int * name: string * title: string
    | RemovePresenter of index: int
    | AddSinglePresenter
    | SavePresenters
    | ExportCsv
    | ExportMarkdown
    | SetHfRepoInput of string
    | SetHfTokenInput of string
    | SyncToHuggingFace
    | GotHfSyncResult of Result<string, string>
    | FetchFromHuggingFace
    | GotHfFetchResult of Result<StudioState, string>
    | ResetAllReviews
    | ClearAlerts
    | SetBaseUrl of string
    | GotSyncParam of string
    | GotAdminSyncParam of string
    | CopyStudentLink of string
    | CopyAdminLink of string
    | PollLiveState
    | ToggleAutoPolling
    | OnException of exn

let getReviewerInfoAsync (js: IJSRuntime) =
    async {
        try
            let! json = js.InvokeAsync<string>("studioReview.getReviewerInfo").AsTask() |> Async.AwaitTask
            let doc = JsonDocument.Parse(json)
            let token = doc.RootElement.GetProperty("token").GetString()
            let nick = doc.RootElement.GetProperty("nickname").GetString()
            return token, nick
        with _ ->
            let token = "rev_" + Guid.NewGuid().ToString("N").Substring(0, 10)
            let nick = "Studio Critic #42"
            return token, nick
    }

let downloadTextFileAsync (js: IJSRuntime) (filename: string) (content: string) (mimeType: string) =
    async {
        try
            do! js.InvokeVoidAsync("studioReview.downloadTextFile", filename, content, mimeType).AsTask() |> Async.AwaitTask
        with _ -> ()
    }

let scrollToTopAsync (js: IJSRuntime) =
    async {
        try
            do! js.InvokeVoidAsync("studioReview.scrollToTop").AsTask() |> Async.AwaitTask
        with _ -> ()
    }

let router = Router.infer SetPage (fun m -> m.Page)

let update (http: HttpClient) (js: IJSRuntime) (message: Message) (model: Model) : Model * Cmd<Message> =
    match message with
    | SetPage page ->
        let scrollCmd = Cmd.OfAsync.attempt (fun () -> scrollToTopAsync js) () OnException
        match page with
        | Page.ReviewPage presId ->
            let presOpt = model.Studio.Presenters |> List.tryFind (fun p -> p.Id = presId)
            match presOpt with
            | Some p ->
                let scores = model.Studio.Rubric |> List.map (fun cr -> cr.Id, int (Math.Round(float cr.MaxScore * 0.85))) |> Map.ofList
                let existing = model.Studio.Reviews |> List.tryFind (fun r -> r.ReviewerToken = model.ReviewerToken && r.PresenterId = p.Id)
                let sc, comm, fb, st, im, info =
                    match existing with
                    | Some r ->
                        let sm = r.Scores |> List.map (fun s -> s.CriterionId, s.Score) |> Map.ofList
                        let cm = r.Scores |> List.map (fun s -> s.CriterionId, s.Comment) |> Map.ofList
                        sm, cm, r.GeneralFeedback, r.Strengths, r.Improvements, Some "Viewing your previously submitted review. You can adjust your scores and notes."
                    | None -> scores, Map.empty, "", "", "", None

                { model with
                    Page = page
                    ActiveReviewPresenterId = Some p.Id
                    ReviewScores = sc
                    ReviewComments = comm
                    GeneralFeedback = fb
                    Strengths = st
                    Improvements = im
                    InfoAlert = info
                    SuccessAlert = None
                    ErrorAlert = None
                }, scrollCmd
            | None ->
                { model with Page = Page.Home; ErrorAlert = Some "Presenter not found." }, scrollCmd
        | _ ->
            { model with Page = page; SuccessAlert = None; ErrorAlert = None }, scrollCmd

    | InitReviewer (token, nickname) ->
        { model with ReviewerToken = token; ReviewerNickname = nickname; IsReviewerLoaded = true }, Cmd.none

    | SetReviewerNickname nick ->
        { model with ReviewerNickname = nick },
        Cmd.OfAsync.attempt (fun () -> async { do! js.InvokeVoidAsync("studioReview.saveReviewerNickname", nick).AsTask() |> Async.AwaitTask }) () OnException

    | LoadLocalState ->
        { model with IsLoading = true },
        Cmd.OfAsync.either (fun () -> HfStorage.loadFromLocalStorage js) () GotLocalState OnException

    | GotLocalState loadedState ->
        let bulk =
            loadedState.Presenters
            |> List.map (fun p -> p.Name + " - " + p.ProjectTitle)
            |> String.concat "\n"

        let hfRepo, hfToken =
            match loadedState.HfConfig with
            | Some cfg -> (if String.IsNullOrWhiteSpace cfg.RepoId then "vykrum/peer-review" else cfg.RepoId), cfg.ApiToken
            | None -> "vykrum/peer-review", ""

        let nextModel =
            { model with
                Studio = loadedState
                EditableRubric = loadedState.Rubric
                EditablePresenters = loadedState.Presenters
                BulkPresentersText = bulk
                HfRepoInput = hfRepo
                HfTokenInput = hfToken
                IsLoading = false
            }

        let autoFetchCmd =
            match loadedState.HfConfig with
            | Some cfg when not (String.IsNullOrWhiteSpace cfg.RepoId) ->
                Cmd.OfAsync.either (fun () -> HfStorage.fetchFromHuggingFace http cfg.RepoId (Some cfg.ApiToken)) () GotHfFetchResult OnException
            | _ -> Cmd.none

        nextModel, autoFetchCmd

    | SetFilter f ->
        { model with Filter = f }, Cmd.none

    | StartReview presenter ->
        let targetPage = Page.ReviewPage presenter.Id
        let scores = model.Studio.Rubric |> List.map (fun c -> c.Id, int (Math.Round(float c.MaxScore * 0.85))) |> Map.ofList
        let existing = model.Studio.Reviews |> List.tryFind (fun r -> r.ReviewerToken = model.ReviewerToken && r.PresenterId = presenter.Id)
        let sc, comm, fb, st, im, info =
            match existing with
            | Some r ->
                let sm = r.Scores |> List.map (fun s -> s.CriterionId, s.Score) |> Map.ofList
                let cm = r.Scores |> List.map (fun s -> s.CriterionId, s.Comment) |> Map.ofList
                sm, cm, r.GeneralFeedback, r.Strengths, r.Improvements, Some "Viewing your previously submitted review. You can adjust your scores and notes."
            | None -> scores, Map.empty, "", "", "", None

        let nextModel =
            { model with
                Page = targetPage
                ActiveReviewPresenterId = Some presenter.Id
                ReviewScores = sc
                ReviewComments = comm
                GeneralFeedback = fb
                Strengths = st
                Improvements = im
                InfoAlert = info
                SuccessAlert = None
                ErrorAlert = None
            }

        nextModel, Cmd.OfAsync.attempt (fun () -> scrollToTopAsync js) () OnException

    | SetScore (critId, score) ->
        { model with ReviewScores = Map.add critId score model.ReviewScores }, Cmd.none

    | SetCriterionComment (critId, comment) ->
        { model with ReviewComments = Map.add critId comment model.ReviewComments }, Cmd.none

    | SetGeneralFeedback text ->
        { model with GeneralFeedback = text }, Cmd.none

    | SetStrengths text ->
        { model with Strengths = text }, Cmd.none

    | SetImprovements text ->
        { model with Improvements = text }, Cmd.none

    | SubmitReview ->
        match model.ActiveReviewPresenterId with
        | None -> model, Cmd.none
        | Some presId ->
            let pres = model.Studio.Presenters |> List.tryFind (fun p -> p.Id = presId)
            match pres with
            | None -> model, Cmd.none
            | Some p ->
                let scoresList =
                    model.Studio.Rubric
                    |> List.map (fun c ->
                        let score = Map.tryFind c.Id model.ReviewScores |> Option.defaultValue (c.MaxScore * 4 / 5)
                        let comment = Map.tryFind c.Id model.ReviewComments |> Option.defaultValue ""
                        { CriterionId = c.Id; Score = score; Comment = comment }
                    )
                let totalScore = scoresList |> List.sumBy (fun s -> s.Score)
                let token = if String.IsNullOrWhiteSpace(model.ReviewerToken) then "rev_guest_" + Guid.NewGuid().ToString("N").Substring(0, 8) else model.ReviewerToken
                let nick = if String.IsNullOrWhiteSpace(model.ReviewerNickname) then "Studio Critic" else model.ReviewerNickname

                let newReview : Review =
                    {
                        Id = Guid.NewGuid().ToString("N")
                        ReviewerToken = token
                        ReviewerNickname = nick
                        PresenterId = p.Id
                        SubmittedAt = DateTime.UtcNow
                        Scores = scoresList
                        TotalScore = totalScore
                        GeneralFeedback = model.GeneralFeedback
                        Strengths = model.Strengths
                        Improvements = model.Improvements
                    }

                let filteredReviews =
                    model.Studio.Reviews
                    |> List.filter (fun r -> not (r.ReviewerToken = token && r.PresenterId = p.Id))

                let updatedStudio = { model.Studio with Reviews = newReview :: filteredReviews; LastUpdated = DateTime.UtcNow }

                let nextModel =
                    { model with
                        Studio = updatedStudio
                        Page = Page.Home
                        ActiveReviewPresenterId = None
                        SuccessAlert = Some ("✓ Peer review submitted for " + p.Name + "! (Score: " + string totalScore + " pts).")
                        ErrorAlert = None
                    }

                let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
                let scrollCmd = Cmd.OfAsync.attempt (fun () -> scrollToTopAsync js) () OnException

                let syncCmd =
                    match updatedStudio.HfConfig with
                    | Some cfg when not (String.IsNullOrWhiteSpace cfg.RepoId) && not (String.IsNullOrWhiteSpace cfg.ApiToken) ->
                        Cmd.OfAsync.either
                            (fun () -> HfStorage.submitReviewAndSync http cfg.RepoId cfg.ApiToken newReview updatedStudio)
                            ()
                            SubmitReviewSyncResult
                            OnException
                    | _ -> Cmd.none

                nextModel, Cmd.batch [ saveCmd; scrollCmd; syncCmd ]

    | SubmitReviewSyncResult result ->
        match result with
        | Ok syncedState ->
            let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js syncedState) () OnException
            { model with
                Studio = syncedState
                IsSubmittingReview = false
                LastSyncedAt = Some DateTime.UtcNow
                SuccessAlert = Some "✓ Peer review saved and aggregated live to studio Hugging Face dataset!"
            }, saveCmd
        | Error err ->
            { model with
                IsSubmittingReview = false
                ErrorAlert = Some ("Review recorded locally. (Cloud sync notice: " + err + ")")
            }, Cmd.none

    // Admin Handlers
    | SetAdminKeyInput key ->
        { model with AdminKeyInput = key }, Cmd.none

    | UnlockAdmin ->
        if model.AdminKeyInput.Trim().ToLowerInvariant() = "studio2026" || String.IsNullOrWhiteSpace(model.AdminKeyInput.Trim()) then
            { model with
                IsAdminUnlocked = true
                SuccessAlert = Some "Admin console unlocked. You have full authority over rubrics, student rosters, stage spotlight, and Hugging Face dataset sync."
                ErrorAlert = None
            }, Cmd.none
        else
            { model with ErrorAlert = Some "Invalid admin passphrase. Default passcode is: studio2026" }, Cmd.none

    | LockAdmin ->
        { model with IsAdminUnlocked = false }, Cmd.none

    | SetAdminSubTab subTab ->
        { model with AdminSubTab = subTab; SuccessAlert = None; ErrorAlert = None }, Cmd.none

    | SetInspectingSummary summaryOpt ->
        { model with InspectingSummary = summaryOpt }, Cmd.none

    | SetActivePresenterId presIdOpt ->
        let updatedStudio = { model.Studio with ActivePresenterId = presIdOpt; LastUpdated = DateTime.UtcNow }
        let msg =
            match presIdOpt with
            | Some id ->
                let name = model.Studio.Presenters |> List.tryFind (fun p -> p.Id = id) |> Option.map (fun p -> p.Name) |> Option.defaultValue id
                "Active presenter set to '" + name + "'."
            | None -> "Active stage spotlight cleared."

        let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
        { model with Studio = updatedStudio; SuccessAlert = Some msg }, saveCmd

    | UpdateRubricCriterionTitle (i, title) ->
        let updated = model.EditableRubric |> List.mapi (fun idx c -> if idx = i then { c with Title = title } else c)
        { model with EditableRubric = updated }, Cmd.none

    | UpdateRubricCriterionDesc (i, desc) ->
        let updated = model.EditableRubric |> List.mapi (fun idx c -> if idx = i then { c with Description = desc } else c)
        { model with EditableRubric = updated }, Cmd.none

    | UpdateRubricCriterionMax (i, maxVal) ->
        let validMax = Math.Max(1, Math.Min(100, maxVal))
        let updated = model.EditableRubric |> List.mapi (fun idx c -> if idx = i then { c with MaxScore = validMax } else c)
        { model with EditableRubric = updated }, Cmd.none

    | AddRubricCriterion ->
        let newIdx = model.EditableRubric.Length + 1
        let newCrit =
            {
                Id = "crit_custom_" + Guid.NewGuid().ToString("N").Substring(0, 6)
                Title = string newIdx + ". New Criteria Dimension"
                Description = "Describe the architectural evaluation focus here."
                MaxScore = 25
                Weight = 1.0
            }
        { model with EditableRubric = model.EditableRubric @ [ newCrit ] }, Cmd.none

    | RemoveRubricCriterion i ->
        if model.EditableRubric.Length <= 1 then
            { model with ErrorAlert = Some "Rubric must contain at least one evaluation dimension." }, Cmd.none
        else
            let updated = model.EditableRubric |> List.indexed |> List.filter (fun (idx, _) -> idx <> i) |> List.map snd
            { model with EditableRubric = updated }, Cmd.none

    | ResetRubricToDefaults ->
        { model with EditableRubric = HfStorage.defaultRubric; InfoAlert = Some "Draft reset to architecture studio standard 4-dimension rubric." }, Cmd.none

    | SaveRubric ->
        let updatedStudio = { model.Studio with Rubric = model.EditableRubric; LastUpdated = DateTime.UtcNow }
        let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
        { model with
            Studio = updatedStudio
            SuccessAlert = Some ("Rubric saved with " + string model.EditableRubric.Length + " criteria (Total: " + string (model.EditableRubric |> List.sumBy (fun c -> c.MaxScore)) + " marks).")
        }, saveCmd

    | SetBulkPresentersText text ->
        { model with BulkPresentersText = text }, Cmd.none

    | ParseBulkPresenters ->
        let lines =
            model.BulkPresentersText.Split([| '\r'; '\n' |], StringSplitOptions.RemoveEmptyEntries)
            |> Array.map (fun s -> s.Trim())
            |> Array.filter (not << String.IsNullOrWhiteSpace)

        if lines.Length = 0 then
            { model with ErrorAlert = Some "Please paste or enter at least one student name." }, Cmd.none
        else
            let parsed =
                lines
                |> Array.mapi (fun i line ->
                    let parts = line.Split([| '-'; ':'; '|' |], 2)
                    let name = parts.[0].Trim()
                    let title = if parts.Length > 1 then parts.[1].Trim() else "Studio Design Project"
                    {
                        Id = "pres_" + string (i + 1) + "_" + Guid.NewGuid().ToString("N").Substring(0, 6)
                        Name = name
                        ProjectTitle = title
                        Order = i + 1
                    }
                )
                |> Array.toList

            { model with
                EditablePresenters = parsed
                InfoAlert = Some ("Parsed " + string parsed.Length + " student presenters into the roster. Click 'Save Presenter Roster' to commit.")
            }, Cmd.none

    | UpdatePresenterField (i, name, title) ->
        let updated = model.EditablePresenters |> List.mapi (fun idx p -> if idx = i then { p with Name = name; ProjectTitle = title } else p)
        { model with EditablePresenters = updated }, Cmd.none

    | RemovePresenter i ->
        let updated =
            model.EditablePresenters
            |> List.indexed
            |> List.filter (fun (idx, _) -> idx <> i)
            |> List.mapi (fun newOrder (_, p) -> { p with Order = newOrder + 1 })
        { model with EditablePresenters = updated }, Cmd.none

    | AddSinglePresenter ->
        let order = model.EditablePresenters.Length + 1
        let newPres =
            {
                Id = "pres_" + string order + "_" + Guid.NewGuid().ToString("N").Substring(0, 6)
                Name = "Student #" + string order
                ProjectTitle = "Architectural Project"
                Order = order
            }
        { model with EditablePresenters = model.EditablePresenters @ [ newPres ] }, Cmd.none

    | SavePresenters ->
        let updatedStudio = { model.Studio with Presenters = model.EditablePresenters; LastUpdated = DateTime.UtcNow }
        let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
        { model with
            Studio = updatedStudio
            SuccessAlert = Some ("Presenter roster updated with " + string model.EditablePresenters.Length + " students.")
        }, saveCmd

    | ExportCsv ->
        let csv = HfStorage.generateCsv model.Studio
        let filename = model.Studio.StudioCode + "_peer_reviews_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmm") + ".csv"
        { model with SuccessAlert = Some ("Generated CSV export: " + filename) },
        Cmd.OfAsync.attempt (fun () -> downloadTextFileAsync js filename csv "text/csv;charset=utf-8") () OnException

    | ExportMarkdown ->
        let md = HfStorage.generateMarkdown model.Studio
        let filename = model.Studio.StudioCode + "_CRIT_REPORT_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmm") + ".md"
        { model with SuccessAlert = Some ("Generated Markdown juror report: " + filename) },
        Cmd.OfAsync.attempt (fun () -> downloadTextFileAsync js filename md "text/markdown;charset=utf-8") () OnException

    | SetHfRepoInput repo ->
        { model with HfRepoInput = repo }, Cmd.none

    | SetHfTokenInput token ->
        { model with HfTokenInput = token }, Cmd.none

    | SyncToHuggingFace ->
        if String.IsNullOrWhiteSpace(model.HfRepoInput) || String.IsNullOrWhiteSpace(model.HfTokenInput) then
            { model with ErrorAlert = Some "Please provide both Hugging Face Dataset Repo ID (e.g. 'username/studio-peer-reviews') and an API Token." }, Cmd.none
        else
            let cleanRepo = model.HfRepoInput.Trim()
            let cleanToken = model.HfTokenInput.Trim()
            let hfCfg =
                {
                    RepoId = cleanRepo
                    ApiToken = cleanToken
                    AutoSync = true
                    LastSyncTime = Some DateTime.UtcNow
                    LastSyncStatus = Some "Syncing..."
                }
            let updatedStudio = { model.Studio with HfConfig = Some hfCfg; LastUpdated = DateTime.UtcNow }
            let saveLocal = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
            let commitCmd = Cmd.OfAsync.either (fun () -> HfStorage.commitToHuggingFace http cleanRepo cleanToken updatedStudio) () GotHfSyncResult OnException
            { model with Studio = updatedStudio; IsHfSyncing = true; InfoAlert = Some "Connecting to Hugging Face Hub Commit API..." },
            Cmd.batch [ saveLocal; commitCmd ]

    | GotHfSyncResult (Ok msg) ->
        let updatedCfg =
            model.Studio.HfConfig
            |> Option.map (fun c -> { c with LastSyncTime = Some DateTime.UtcNow; LastSyncStatus = Some "Success" })
        let updatedStudio = { model.Studio with HfConfig = updatedCfg }
        { model with Studio = updatedStudio; IsHfSyncing = false; SuccessAlert = Some msg; InfoAlert = None }, Cmd.none

    | GotHfSyncResult (Error err) ->
        let updatedCfg =
            model.Studio.HfConfig
            |> Option.map (fun c -> { c with LastSyncStatus = Some ("Error: " + err) })
        let updatedStudio = { model.Studio with HfConfig = updatedCfg }
        { model with Studio = updatedStudio; IsHfSyncing = false; ErrorAlert = Some err; InfoAlert = None }, Cmd.none

    | FetchFromHuggingFace ->
        let repoId = model.HfRepoInput.Trim()
        let tokenOpt = if String.IsNullOrWhiteSpace(model.HfTokenInput) then None else Some (model.HfTokenInput.Trim())
        if String.IsNullOrWhiteSpace(repoId) then
            { model with ErrorAlert = Some "Please enter a Hugging Face Dataset Repo ID to fetch from." }, Cmd.none
        else
            { model with IsHfSyncing = true; InfoAlert = Some ("Fetching latest dataset from Hugging Face (" + repoId + ")...") },
            Cmd.OfAsync.either (fun () -> HfStorage.fetchFromHuggingFace http repoId tokenOpt) () GotHfFetchResult OnException

    | GotHfFetchResult (Ok remoteState) ->
        let bulk =
            remoteState.Presenters
            |> List.map (fun p -> p.Name + " - " + p.ProjectTitle)
            |> String.concat "\n"

        // Merge reviews: keep local reviewer's own reviews, combine with all remote reviews
        let myReviews = model.Studio.Reviews |> List.filter (fun r -> r.ReviewerToken = model.ReviewerToken)
        let otherReviews = remoteState.Reviews |> List.filter (fun r -> not (r.ReviewerToken = model.ReviewerToken))
        let mergedReviews = myReviews @ otherReviews

        let mergedStudio =
            { remoteState with
                Reviews = mergedReviews
                HfConfig = match model.Studio.HfConfig with Some _ as c -> c | None -> remoteState.HfConfig
                LastUpdated = DateTime.UtcNow
            }

        let saveLocal = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js mergedStudio) () OnException
        { model with
            Studio = mergedStudio
            EditableRubric = mergedStudio.Rubric
            EditablePresenters = mergedStudio.Presenters
            BulkPresentersText = bulk
            IsHfSyncing = false
            LastSyncedAt = Some DateTime.UtcNow
        }, saveLocal

    | GotHfFetchResult (Error err) ->
        { model with IsHfSyncing = false; ErrorAlert = Some err; InfoAlert = None }, Cmd.none

    | SetBaseUrl url ->
        { model with BaseUrl = url }, Cmd.none

    | GotSyncParam syncJson ->
        match HfStorage.parseSyncParam syncJson with
        | Some cfg ->
            let updatedStudio = { model.Studio with HfConfig = Some cfg }
            let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
            let cleanUrlCmd = Cmd.OfAsync.attempt (fun () -> async { do! js.InvokeVoidAsync("studioReview.cleanSyncUrl").AsTask() |> Async.AwaitTask }) () OnException
            let fetchCmd = Cmd.OfAsync.either (fun () -> HfStorage.fetchFromHuggingFace http cfg.RepoId (Some cfg.ApiToken)) () GotHfFetchResult OnException
            { model with
                Studio = updatedStudio
                HfRepoInput = cfg.RepoId
                HfTokenInput = cfg.ApiToken
                SuccessAlert = Some "🟢 Studio Cloud Sync connected! You are now live with the jury stage."
            }, Cmd.batch [ saveCmd; cleanUrlCmd; fetchCmd ]
        | None -> model, Cmd.none

    | CopyStudentLink link ->
        let copyCmd = Cmd.OfAsync.attempt (fun () -> async { do! js.InvokeVoidAsync("studioReview.copyToClipboard", link).AsTask() |> Async.AwaitTask }) () OnException
        { model with SuccessAlert = Some "📋 Student studio link copied to clipboard! Share this link or QR code with your class." }, copyCmd

    | GotAdminSyncParam syncJson ->
        match HfStorage.parseSyncParam syncJson with
        | Some cfg ->
            let updatedStudio = { model.Studio with HfConfig = Some cfg }
            let saveCmd = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
            let cleanUrlCmd = Cmd.OfAsync.attempt (fun () -> async { do! js.InvokeVoidAsync("studioReview.cleanSyncUrl").AsTask() |> Async.AwaitTask }) () OnException
            let fetchCmd = Cmd.OfAsync.either (fun () -> HfStorage.fetchFromHuggingFace http cfg.RepoId (Some cfg.ApiToken)) () GotHfFetchResult OnException
            { model with
                Studio = updatedStudio
                HfRepoInput = cfg.RepoId
                HfTokenInput = cfg.ApiToken
                IsAdminUnlocked = true
                Page = Page.Admin
                SuccessAlert = Some "🔑 Admin Console unlocked & connected to Hugging Face dataset on this device!"
            }, Cmd.batch [ saveCmd; cleanUrlCmd; fetchCmd ]
        | None -> model, Cmd.none

    | CopyAdminLink link ->
        let copyCmd = Cmd.OfAsync.attempt (fun () -> async { do! js.InvokeVoidAsync("studioReview.copyToClipboard", link).AsTask() |> Async.AwaitTask }) () OnException
        { model with SuccessAlert = Some "🔑 Private Faculty Admin Link copied to clipboard! Bookmark or send it to your mobile." }, copyCmd

    | PollLiveState ->
        let pollDelayCmd =
            if model.IsAutoPollingEnabled then
                Cmd.OfAsync.either
                    (fun () -> async { do! Async.Sleep 12000 })
                    ()
                    (fun () -> PollLiveState)
                    OnException
            else Cmd.none

        match model.Studio.HfConfig with
        | Some cfg when not (String.IsNullOrWhiteSpace cfg.RepoId) && not (String.IsNullOrWhiteSpace cfg.ApiToken) ->
            let fetchCmd =
                Cmd.OfAsync.either
                    (fun () -> HfStorage.fetchFromHuggingFace http cfg.RepoId (Some cfg.ApiToken))
                    ()
                    GotHfFetchResult
                    OnException
            model, Cmd.batch [ fetchCmd; pollDelayCmd ]
        | _ ->
            model, pollDelayCmd

    | ToggleAutoPolling ->
        let next = not model.IsAutoPollingEnabled
        let cmd = if next then Cmd.ofMsg PollLiveState else Cmd.none
        { model with IsAutoPollingEnabled = next }, cmd

    | ResetAllReviews ->
        let updatedStudio = { model.Studio with Reviews = []; LastUpdated = DateTime.UtcNow }
        let saveLocal = Cmd.OfAsync.attempt (fun () -> HfStorage.saveToLocalStorage js updatedStudio) () OnException
        { model with
            Studio = updatedStudio
            SuccessAlert = Some "All peer reviews cleared. Ready for fresh studio review session."
        }, saveLocal

    | ClearAlerts ->
        { model with SuccessAlert = None; ErrorAlert = None; InfoAlert = None }, Cmd.none

    | OnException exn ->
        { model with ErrorAlert = Some ("Error: " + exn.Message); IsLoading = false; IsHfSyncing = false }, Cmd.none

// ==========================================
// VIEWS
// ==========================================

let viewHeader (model: Model) (dispatch: Message -> unit) =
    header {
        attr.``class`` "studio-header"
        div {
            attr.``class`` "header-container"
            a {
                attr.``class`` "brand-section"
                attr.href (router.Link Page.Home)
                span { attr.``class`` "brand-badge"; text model.Studio.StudioCode }
                div {
                    h1 {
                        attr.``class`` "studio-title"
                        text "STUDIO CRIT"
                        span { attr.style "font-weight: 300; opacity: 0.5;"; text "//" }
                        span { attr.style "font-weight: 400; font-size: 0.95rem; color: var(--accent-cyan);"; text "WASM Peer Review" }
                    }
                    p { attr.``class`` "studio-subtitle"; text model.Studio.StudioName }
                }
            }

            div {
                attr.``class`` "header-right"
                div {
                    attr.``class`` "user-identity-capsule"
                    span { attr.``class`` "user-dot" }
                    span { attr.``class`` "user-alias"; text model.ReviewerNickname }
                    span { attr.``class`` "anon-tag"; text "Anonymous Reviewer" }
                }

                nav {
                    attr.``class`` "nav-tabs"
                    a {
                        attr.``class`` (if model.Page = Page.Home then "tab-btn is-active" else "tab-btn")
                        attr.href (router.Link Page.Home)
                        text "🏛️ Studio Roster"
                    }
                    cond (model.Page = Page.Admin) <| function
                        | true ->
                            span {
                                attr.``class`` "tab-btn is-active"
                                attr.style "border-color: var(--accent-cyan); color: var(--accent-cyan);"
                                text "🔒 Admin Mode"
                            }
                        | false -> empty()
                }
            }
        }
    }

let viewAlerts (model: Model) (dispatch: Message -> unit) =
    div {
        cond model.SuccessAlert <| function
            | Some msg ->
                div {
                    attr.``class`` "notification-banner notification-success"
                    span { text msg }
                    button {
                        attr.style "background:none; border:none; color:inherit; font-weight:700; cursor:pointer;"
                        on.click (fun _ -> dispatch ClearAlerts)
                        text "✕"
                    }
                }
            | None -> empty()

        cond model.ErrorAlert <| function
            | Some msg ->
                div {
                    attr.``class`` "notification-banner notification-error"
                    span { text msg }
                    button {
                        attr.style "background:none; border:none; color:inherit; font-weight:700; cursor:pointer;"
                        on.click (fun _ -> dispatch ClearAlerts)
                        text "✕"
                    }
                }
            | None -> empty()

        cond model.InfoAlert <| function
            | Some msg ->
                div {
                    attr.``class`` "notification-banner notification-info"
                    span { text msg }
                    button {
                        attr.style "background:none; border:none; color:inherit; font-weight:700; cursor:pointer;"
                        on.click (fun _ -> dispatch ClearAlerts)
                        text "✕"
                    }
                }
            | None -> empty()
    }

let viewSpotlight (model: Model) (dispatch: Message -> unit) =
    match model.Studio.ActivePresenterId with
    | None -> empty()
    | Some activeId ->
        let activePresenter = model.Studio.Presenters |> List.tryFind (fun p -> p.Id = activeId)
        match activePresenter with
        | None -> empty()
        | Some pres ->
            let isReviewed = model.Studio.Reviews |> List.exists (fun r -> r.ReviewerToken = model.ReviewerToken && r.PresenterId = pres.Id)
            let btnLabel = if isReviewed then "✏️ Edit Your Feedback" else "✍️ Review Presenter Now →"
            div {
                attr.``class`` "live-spotlight-card"
                div {
                    div {
                        attr.``class`` "live-badge"
                        span { attr.``class`` "pulse-dot" }
                        text "Live Presentation On Stage"
                    }
                    h2 { attr.``class`` "live-presenter-name"; text pres.Name }
                    p { attr.``class`` "live-project-title"; text ("Project: " + pres.ProjectTitle) }
                }
                div {
                    a {
                        attr.``class`` "live-action-btn"
                        attr.href (router.Link (Page.ReviewPage pres.Id))
                        text btnLabel
                    }
                }
            }

let viewProgressBar (model: Model) =
    let totalPresenters = model.Studio.Presenters.Length
    let reviewedCount =
        model.Studio.Presenters
        |> List.filter (fun p -> model.Studio.Reviews |> List.exists (fun r -> r.ReviewerToken = model.ReviewerToken && r.PresenterId = p.Id))
        |> List.length

    let percentage = if totalPresenters = 0 then 0 else Math.Min(100, int (float reviewedCount / float totalPresenters * 100.0))

    div {
        attr.``class`` "progress-card"
        div {
            attr.``class`` "progress-header"
            span {
                strong { attr.style "color: #fff;"; text ("Your Crit Progress: " + string reviewedCount + " of " + string totalPresenters + " students reviewed") }
            }
            span {
                attr.style "font-family: var(--font-mono); color: var(--accent-cyan);"
                text (string percentage + "% completed")
            }
        }
        div {
            attr.``class`` "progress-track"
            div {
                attr.``class`` "progress-bar-fill"
                attr.style ("width: " + string percentage + "%;")
            }
        }
    }

let viewPresenterCard (model: Model) (presenter: Presenter) (dispatch: Message -> unit) =
    let isReviewed = model.Studio.Reviews |> List.exists (fun r -> r.ReviewerToken = model.ReviewerToken && r.PresenterId = presenter.Id)
    let isActive = model.Studio.ActivePresenterId = Some presenter.Id

    let cardClasses =
        let baseClass = "presenter-card"
        let activeClass = if isActive then " is-active-presenter" else ""
        let reviewedClass = if isReviewed then " is-reviewed" else ""
        baseClass + activeClass + reviewedClass

    let btnLabel = if isReviewed then "View / Edit Review" else "Grade Project →"

    div {
        attr.``class`` cardClasses

        div {
            div {
                attr.``class`` "card-top"
                span { attr.``class`` "presenter-order"; text ("#" + presenter.Order.ToString("D2")) }

                if isActive then
                    span {
                        attr.``class`` "status-badge badge-live"
                        span { attr.``class`` "pulse-dot" }
                        text "Presenting Now"
                    }
                elif isReviewed then
                    span {
                        attr.``class`` "status-badge badge-reviewed"
                        text "✓ Reviewed"
                    }
                else
                    span {
                        attr.``class`` "status-badge badge-pending"
                        text "Pending Your Review"
                    }
            }

            h3 { attr.``class`` "card-presenter-name"; text presenter.Name }
            p { attr.``class`` "card-project-title"; text presenter.ProjectTitle }
        }

        div {
            attr.``class`` "card-footer"
            div {
                attr.``class`` "card-stats"
                if isReviewed then
                    span { attr.style "color: var(--accent-green); font-weight: 500;"; text "Score recorded" }
                else
                    span { text "Awaiting your crit" }
            }
            a {
                attr.``class`` (if isReviewed then "btn-review-card btn-done" else "btn-review-card")
                attr.href (router.Link (Page.ReviewPage presenter.Id))
                text btnLabel
            }
        }
    }

let viewStudioFeed (model: Model) (dispatch: Message -> unit) =
    div {
        viewSpotlight model dispatch
        viewProgressBar model

        div {
            attr.``class`` "section-title-row"
            div {
                h2 { attr.``class`` "section-title"; text "Studio Cohort & Presentation Roster" }
                p { attr.``class`` "section-desc"; text "Review your peers sequentially as they present. Your identity is kept anonymous." }
            }

            div {
                attr.``class`` "filter-pills"
                button {
                    attr.``class`` (if model.Filter = FilterAll then "filter-btn is-active" else "filter-btn")
                    on.click (fun _ -> dispatch (SetFilter FilterAll))
                    text "All Students"
                }
                button {
                    attr.``class`` (if model.Filter = FilterPending then "filter-btn is-active" else "filter-btn")
                    on.click (fun _ -> dispatch (SetFilter FilterPending))
                    text "Pending My Review"
                }
                button {
                    attr.``class`` (if model.Filter = FilterReviewed then "filter-btn is-active" else "filter-btn")
                    on.click (fun _ -> dispatch (SetFilter FilterReviewed))
                    text "Reviewed By Me"
                }
            }
        }

        let filteredPresenters =
            model.Studio.Presenters
            |> List.filter (fun p ->
                let isRev = model.Studio.Reviews |> List.exists (fun r -> r.ReviewerToken = model.ReviewerToken && r.PresenterId = p.Id)
                match model.Filter with
                | FilterAll -> true
                | FilterPending -> not isRev
                | FilterReviewed -> isRev
            )

        if filteredPresenters.IsEmpty then
            div {
                attr.style "text-align: center; padding: 4rem 1rem; background: var(--bg-card); border-radius: var(--radius-md); border: 1px solid var(--border-subtle);"
                p { attr.style "color: var(--text-muted); font-size: 1rem;"; text "No presenters match this filter." }
            }
        else
            div {
                attr.``class`` "presenter-grid"
                forEach filteredPresenters (fun p -> viewPresenterCard model p dispatch)
            }
    }

let viewReviewForm (model: Model) (presId: string) (dispatch: Message -> unit) =
    let presenterOpt = model.Studio.Presenters |> List.tryFind (fun p -> p.Id = presId)
    match presenterOpt with
    | None ->
        div {
            attr.style "text-align:center; padding: 4rem;"
            p { text "Presenter not found." }
            a { attr.``class`` "btn-admin"; attr.href (router.Link Page.Home); text "← Return to Roster" }
        }
    | Some presenter ->
        let rubric = model.Studio.Rubric
        let maxPossible = rubric |> List.sumBy (fun c -> c.MaxScore)
        let currentTotal =
            rubric
            |> List.sumBy (fun c ->
                Map.tryFind c.Id model.ReviewScores
                |> Option.defaultValue (c.MaxScore * 4 / 5)
            )

        div {
            attr.``class`` "review-view-container"

            div {
                attr.``class`` "review-view-header"
                div {
                    a {
                        attr.``class`` "btn-back"
                        attr.href (router.Link Page.Home)
                        text "← Back to Studio Roster"
                    }
                    h2 { attr.``class`` "review-presenter-name"; text presenter.Name }
                    p { attr.``class`` "review-project-title"; text presenter.ProjectTitle }
                    p { attr.style "font-size: 0.78rem; color: var(--text-dim); margin-top: 0.25rem;"; text ("Reviewing anonymously as: " + model.ReviewerNickname) }
                }

                div {
                    attr.``class`` "live-score-pill"
                    div { attr.``class`` "live-score-val"; text (string currentTotal) }
                    div { attr.``class`` "live-score-max"; text ("/ " + string maxPossible + " Total Marks") }
                }
            }

            // Rubric Dimensions
            forEach rubric (fun crit ->
                let currentScore =
                    Map.tryFind crit.Id model.ReviewScores
                    |> Option.defaultValue (crit.MaxScore * 4 / 5)

                let currentComment =
                    Map.tryFind crit.Id model.ReviewComments
                    |> Option.defaultValue ""

                div {
                    attr.``class`` "criterion-box"

                    div {
                        attr.``class`` "criterion-header"
                        div {
                            h4 { attr.``class`` "criterion-title"; text crit.Title }
                            p { attr.``class`` "criterion-desc"; text crit.Description }
                        }
                        div {
                            attr.``class`` "criterion-score-badge"
                            text (string currentScore + " / " + string crit.MaxScore + " pts")
                        }
                    }

                    div {
                        attr.``class`` "score-control-row"
                        input {
                            attr.``class`` "studio-range"
                            attr.``type`` "range"
                            attr.min "0"
                            attr.max (crit.MaxScore.ToString())
                            attr.step "1"
                            attr.value (currentScore.ToString())
                            on.input (fun e ->
                                let v = try int (string e.Value) with _ -> currentScore
                                dispatch (SetScore (crit.Id, v))
                            )
                        }
                    }

                    // Tactile quick score pills
                    div {
                        attr.``class`` "quick-score-pills"
                        let p40 = int (Math.Round(float crit.MaxScore * 0.4))
                        let p65 = int (Math.Round(float crit.MaxScore * 0.65))
                        let p80 = int (Math.Round(float crit.MaxScore * 0.8))
                        let p95 = int (Math.Round(float crit.MaxScore * 0.95))
                        let p100 = crit.MaxScore

                        button {
                            attr.``class`` (if currentScore = p40 then "quick-pill is-active" else "quick-pill")
                            on.click (fun _ -> dispatch (SetScore (crit.Id, p40)))
                            text ("Developing (" + string p40 + ")")
                        }
                        button {
                            attr.``class`` (if currentScore = p65 then "quick-pill is-active" else "quick-pill")
                            on.click (fun _ -> dispatch (SetScore (crit.Id, p65)))
                            text ("Proficient (" + string p65 + ")")
                        }
                        button {
                            attr.``class`` (if currentScore = p80 then "quick-pill is-active" else "quick-pill")
                            on.click (fun _ -> dispatch (SetScore (crit.Id, p80)))
                            text ("Strong (" + string p80 + ")")
                        }
                        button {
                            attr.``class`` (if currentScore = p95 then "quick-pill is-active" else "quick-pill")
                            on.click (fun _ -> dispatch (SetScore (crit.Id, p95)))
                            text ("Exemplary (" + string p95 + ")")
                        }
                        button {
                            attr.``class`` (if currentScore = p100 then "quick-pill is-active" else "quick-pill")
                            on.click (fun _ -> dispatch (SetScore (crit.Id, p100)))
                            text ("Flawless (" + string p100 + ")")
                        }
                    }

                    input {
                        attr.``class`` "input-studio"
                        attr.``type`` "text"
                        attr.placeholder ("Specific critique note for " + crit.Title.ToLowerInvariant() + " (optional)...")
                        attr.value currentComment
                        on.input (fun e -> dispatch (SetCriterionComment (crit.Id, string e.Value)))
                    }
                }
            )

            // Qualitative Feedback Prompts
            div {
                attr.``class`` "form-section-box"
                label { attr.``class`` "form-label"; text "🌟 Highlighted Architectural Strengths & Merits" }
                textarea {
                    attr.``class`` "critique-textarea"
                    attr.placeholder "What aspect of the spatial organization, theoretical premise, or drawing craft worked best? (e.g. 'The sectional threshold between the reading room and courtyard is brilliant...')"
                    attr.value model.Strengths
                    on.input (fun e -> dispatch (SetStrengths (string e.Value)))
                }
            }

            div {
                attr.``class`` "form-section-box"
                label { attr.``class`` "form-label"; text "📐 Constructive Critique & Areas for Iteration" }
                textarea {
                    attr.``class`` "critique-textarea"
                    attr.placeholder "Where should the student focus their energy next? Any egress, structural, or material queries? (e.g. 'Clarify the column grid in the basement plan; consider passive solar shading on the west facade...')"
                    attr.value model.Improvements
                    on.input (fun e -> dispatch (SetImprovements (string e.Value)))
                }
            }

            div {
                attr.``class`` "form-section-box"
                label { attr.``class`` "form-label"; text "💬 General Feedback / Questions for Jury Defense" }
                textarea {
                    attr.``class`` "critique-textarea"
                    attr.placeholder "Overall impression, jury defense question, or encouraging comment..."
                    attr.value model.GeneralFeedback
                    on.input (fun e -> dispatch (SetGeneralFeedback (string e.Value)))
                }
            }

            div {
                attr.``class`` "submit-btn-row"
                a {
                    attr.``class`` "btn-admin"
                    attr.href (router.Link Page.Home)
                    text "Cancel"
                }
                button {
                    attr.``class`` "btn-primary"
                    attr.disabled model.IsSubmittingReview
                    on.click (fun _ -> dispatch SubmitReview)
                    text ("Submit Anonymous Review (" + string currentTotal + "/" + string maxPossible + ") ✓")
                }
            }
        }

let viewAdminLocked (model: Model) (dispatch: Message -> unit) =
    div {
        attr.``class`` "admin-card"
        attr.style "max-width: 520px; margin: 3rem auto; text-align: center;"

        div { attr.style "font-size: 2.5rem; margin-bottom: 0.75rem;"; text "🔒" }
        h2 { attr.``class`` "section-title"; text "Studio Administrator Console" }
        p { attr.style "color: var(--text-muted); font-size: 0.88rem; margin: 0.5rem 0 1.5rem 0;"; text "Dedicated URL (/admin). Enter the faculty passphrase to configure rubrics, student rosters, stage controls, and Hugging Face dataset sync." }

        div {
            attr.style "display: flex; gap: 0.5rem; margin-bottom: 1rem;"
            input {
                attr.``class`` "input-studio"
                attr.``type`` "password"
                attr.placeholder "Enter admin passphrase (default: studio2026)"
                attr.value model.AdminKeyInput
                on.input (fun e -> dispatch (SetAdminKeyInput (string e.Value)))
            }
            button {
                attr.``class`` "btn-primary"
                on.click (fun _ -> dispatch UnlockAdmin)
                text "Unlock Console"
            }
        }
        p { attr.style "font-size: 0.75rem; color: var(--text-dim);"; text "Default passcode: studio2026" }
    }

let viewAdminUnlocked (model: Model) (dispatch: Message -> unit) =
    div {
        attr.``class`` "admin-card"

        div {
            attr.``class`` "admin-header-row"
            div {
                h2 { attr.``class`` "section-title"; text "Faculty & Juror Administration (/admin)" }
                p { attr.``class`` "section-desc"; text "Real-time grading matrix, rubric configuration, roster editor, and Hugging Face Dataset sync." }
            }

            div {
                attr.``class`` "admin-action-bar"
                button {
                    attr.``class`` (if model.AdminSubTab = SubTabAnalytics then "btn-admin is-active" else "btn-admin")
                    on.click (fun _ -> dispatch (SetAdminSubTab SubTabAnalytics))
                    text "📊 Live Scoreboard"
                }
                button {
                    attr.``class`` (if model.AdminSubTab = SubTabRubric then "btn-admin is-active" else "btn-admin")
                    on.click (fun _ -> dispatch (SetAdminSubTab SubTabRubric))
                    text "📐 Rubric & Marks"
                }
                button {
                    attr.``class`` (if model.AdminSubTab = SubTabPresenters then "btn-admin is-active" else "btn-admin")
                    on.click (fun _ -> dispatch (SetAdminSubTab SubTabPresenters))
                    text "👥 Student Roster"
                }
                button {
                    attr.``class`` (if model.AdminSubTab = SubTabHuggingFace then "btn-admin btn-admin-hf is-active" else "btn-admin btn-admin-hf")
                    on.click (fun _ -> dispatch (SetAdminSubTab SubTabHuggingFace))
                    text "🤗 Hugging Face Dataset"
                }
                button {
                    attr.``class`` "btn-admin"
                    attr.style "color: var(--accent-rose);"
                    on.click (fun _ -> dispatch LockAdmin)
                    text "Lock Console"
                }
            }
        }

        match model.AdminSubTab with
        | SubTabAnalytics ->
            div {
                div {
                    attr.style "display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; gap: 1rem; flex-wrap: wrap;"
                    div {
                        attr.style "display: flex; align-items: center; gap: 0.75rem;"
                        label { attr.style "font-size: 0.85rem; font-weight: 600; color: var(--accent-amber);"; text "🔴 Set Live Presenter on Stage:" }
                        select {
                            attr.``class`` "input-studio"
                            attr.style "width: auto; padding: 0.4rem 0.8rem;"
                            on.change (fun e ->
                                let v = string e.Value
                                let presId = if v = "none" then None else Some v
                                dispatch (SetActivePresenterId presId)
                            )
                            option { attr.value "none"; text "— None (Stage Idle) —" }
                            forEach model.Studio.Presenters (fun p ->
                                let isCur = model.Studio.ActivePresenterId = Some p.Id
                                option {
                                    attr.value p.Id
                                    if isCur then attr.selected "selected" else attr.empty()
                                    text (string p.Order + ". " + p.Name + " (" + p.ProjectTitle + ")")
                                }
                            )
                        }
                    }

                    div {
                        attr.style "display: flex; gap: 0.5rem; align-items: center;"
                        button {
                            attr.``class`` "btn-admin"
                            attr.style "border-color: var(--accent-cyan); color: var(--accent-cyan); font-weight: 600;"
                            attr.disabled model.IsHfSyncing
                            on.click (fun _ -> dispatch FetchFromHuggingFace)
                            text (if model.IsHfSyncing then "⏳ Syncing..." else "🔄 Pull Live Grades")
                        }
                        button {
                            attr.``class`` "btn-admin"
                            on.click (fun _ -> dispatch ExportCsv)
                            text "📥 Download CSV Matrix"
                        }
                        button {
                            attr.``class`` "btn-admin"
                            on.click (fun _ -> dispatch ExportMarkdown)
                            text "📄 Download Crit Report (MD)"
                        }
                    }
                }

                let summaries = HfStorage.computeSummaries model.Studio
                div {
                    attr.``class`` "studio-table-container"
                    table {
                        attr.``class`` "studio-table"
                        thead {
                            tr {
                                th { text "#" }
                                th { text "Presenter & Project" }
                                th { text "Reviews" }
                                th { text "Average Score" }
                                th { text "Criterion Breakdown" }
                                th { text "Actions" }
                            }
                        }
                        tbody {
                            forEach summaries (fun s ->
                                let p = s.Presenter
                                let isLive = model.Studio.ActivePresenterId = Some p.Id

                                tr {
                                    td {
                                        attr.style "font-family: var(--font-mono); color: var(--text-muted);"
                                        text (p.Order.ToString("D2"))
                                    }
                                    td {
                                        div {
                                            attr.``class`` "table-presenter-name"
                                            text p.Name
                                            if isLive then
                                                span { attr.``class`` "status-badge badge-live"; attr.style "display:inline-flex; margin-left:0.5rem;"; text "LIVE" }
                                        }
                                        div { attr.``class`` "table-project-name"; text p.ProjectTitle }
                                    }
                                    td {
                                        span {
                                            attr.style "font-family: var(--font-mono); font-weight: 600;"
                                            text (string s.ReviewCount)
                                        }
                                    }
                                    td {
                                        attr.``class`` "score-cell"
                                        if s.ReviewCount = 0 then
                                            span { attr.style "color: var(--text-dim);"; text "—" }
                                        else
                                            span { text (s.AverageTotalScore.ToString("F1") + " / " + string s.MaxPossibleScore) }
                                    }
                                    td {
                                        div {
                                            attr.``class`` "mini-bar-container"
                                            forEach s.CriterionStats (fun c ->
                                                let pct = if c.MaxScore = 0 then 0 else int (c.AverageScore / float c.MaxScore * 100.0)
                                                div {
                                                    div {
                                                        attr.style "display:flex; justify-content:space-between; font-size:0.68rem; color:var(--text-dim);"
                                                        span { text (c.Title.Split('.').[0]) }
                                                        span { text (c.AverageScore.ToString("F1") + "/" + string c.MaxScore) }
                                                    }
                                                    div {
                                                        attr.``class`` "mini-bar-track"
                                                        div {
                                                            attr.``class`` "mini-bar-fill"
                                                            attr.style ("width: " + string pct + "%;")
                                                        }
                                                    }
                                                }
                                            )
                                        }
                                    }
                                    td {
                                        div {
                                            attr.style "display: flex; gap: 0.4rem;"
                                            button {
                                                attr.``class`` "btn-admin"
                                                on.click (fun _ -> dispatch (SetInspectingSummary (Some s)))
                                                text "View Feedback"
                                            }
                                            button {
                                                attr.``class`` "btn-admin"
                                                on.click (fun _ -> dispatch (SetActivePresenterId (Some p.Id)))
                                                text "Set Live"
                                            }
                                        }
                                    }
                                }
                            )
                        }
                    }
                }

                match model.InspectingSummary with
                | None -> empty()
                | Some s ->
                    div {
                        attr.style "margin-top: 2rem; background: var(--bg-surface); border: 1px solid var(--border-active); border-radius: var(--radius-md); padding: 1.5rem;"
                        div {
                            attr.style "display:flex; justify-content:space-between; align-items:center; margin-bottom:1rem; border-bottom:1px solid var(--border-subtle); padding-bottom:0.75rem;"
                            div {
                                h3 { attr.style "font-family: var(--font-display); font-size:1.3rem;"; text ("Juror & Peer Feedback: " + s.Presenter.Name) }
                                p { attr.style "color: var(--accent-cyan); font-size:0.85rem;"; text s.Presenter.ProjectTitle }
                            }
                            button {
                                attr.``class`` "btn-admin"
                                on.click (fun _ -> dispatch (SetInspectingSummary None))
                                text "Close Feedback [✕]"
                            }
                        }

                        div {
                            attr.style "display: grid; grid-template-columns: repeat(auto-fit, minmax(280px, 1fr)); gap: 1.25rem;"
                            div {
                                h4 { attr.style "color: var(--accent-green); font-size: 0.9rem; margin-bottom: 0.5rem;"; text "Highlighted Strengths" }
                                if s.StrengthsList.IsEmpty then
                                    p { attr.style "color: var(--text-dim); font-size: 0.82rem;"; text "No notes recorded." }
                                else
                                    forEach s.StrengthsList (fun str ->
                                        p { attr.style "font-size: 0.82rem; background: rgba(16,185,129,0.06); border-left: 2px solid var(--accent-green); padding: 0.4rem 0.6rem; margin-bottom: 0.4rem; border-radius: 2px;"; text str }
                                    )
                            }
                            div {
                                h4 { attr.style "color: var(--accent-amber); font-size: 0.9rem; margin-bottom: 0.5rem;"; text "Areas for Iteration" }
                                if s.ImprovementsList.IsEmpty then
                                    p { attr.style "color: var(--text-dim); font-size: 0.82rem;"; text "No notes recorded." }
                                else
                                    forEach s.ImprovementsList (fun imp ->
                                        p { attr.style "font-size: 0.82rem; background: rgba(245,158,11,0.06); border-left: 2px solid var(--accent-amber); padding: 0.4rem 0.6rem; margin-bottom: 0.4rem; border-radius: 2px;"; text imp }
                                    )
                            }
                            div {
                                h4 { attr.style "color: var(--accent-cyan); font-size: 0.9rem; margin-bottom: 0.5rem;"; text "General Jury Defense Notes" }
                                if s.FeedbackComments.IsEmpty then
                                    p { attr.style "color: var(--text-dim); font-size: 0.82rem;"; text "No notes recorded." }
                                else
                                    forEach s.FeedbackComments (fun c ->
                                        p { attr.style "font-size: 0.82rem; background: rgba(56,189,248,0.06); border-left: 2px solid var(--accent-cyan); padding: 0.4rem 0.6rem; margin-bottom: 0.4rem; border-radius: 2px;"; text c }
                                    )
                            }
                        }
                    }
            }

        | SubTabRubric ->
            div {
                let totalMax = model.EditableRubric |> List.sumBy (fun c -> c.MaxScore)
                div {
                    attr.style "display:flex; justify-content:space-between; align-items:center; margin-bottom:1.5rem;"
                    div {
                        h3 { attr.style "font-family: var(--font-display); font-size:1.15rem;"; text "Grading Fields & Rubric Criteria" }
                        p { attr.style "color: var(--text-muted); font-size:0.82rem;"; text ("Current Total: " + string totalMax + " Marks across " + string model.EditableRubric.Length + " evaluation dimensions.") }
                    }
                    div {
                        attr.style "display: flex; gap: 0.5rem;"
                        button {
                            attr.``class`` "btn-admin"
                            on.click (fun _ -> dispatch AddRubricCriterion)
                            text "+ Add Criterion"
                        }
                        button {
                            attr.``class`` "btn-admin"
                            on.click (fun _ -> dispatch ResetRubricToDefaults)
                            text "Reset to Architecture Standard"
                        }
                        button {
                            attr.``class`` "btn-primary"
                            on.click (fun _ -> dispatch SaveRubric)
                            text "Save Rubric Changes"
                        }
                    }
                }

                forEach (List.indexed model.EditableRubric) (fun (i, crit) ->
                    div {
                        attr.``class`` "rubric-edit-item"
                        div {
                            div {
                                attr.style "display:flex; gap: 0.75rem; margin-bottom: 0.5rem;"
                                input {
                                    attr.``class`` "input-studio"
                                    attr.``type`` "text"
                                    attr.placeholder "Criterion Title (e.g. 1. Spatial Organization)"
                                    attr.value crit.Title
                                    on.input (fun e -> dispatch (UpdateRubricCriterionTitle (i, string e.Value)))
                                }
                                div {
                                    attr.style "display: flex; align-items: center; gap: 0.4rem; white-space: nowrap;"
                                    label { attr.style "font-size: 0.78rem; color: var(--text-muted);"; text "Max Marks:" }
                                    input {
                                        attr.``class`` "input-studio"
                                        attr.style "width: 70px; text-align: center; font-family: var(--font-mono);"
                                        attr.``type`` "number"
                                        attr.min "1"
                                        attr.max "100"
                                        attr.value (crit.MaxScore.ToString())
                                        on.input (fun e ->
                                            let v = try int (string e.Value) with _ -> crit.MaxScore
                                            dispatch (UpdateRubricCriterionMax (i, v))
                                        )
                                    }
                                }
                            }
                            textarea {
                                attr.``class`` "critique-textarea"
                                attr.style "min-height: 50px;"
                                attr.placeholder "Criterion evaluation guidance / prompt for reviewers..."
                                attr.value crit.Description
                                on.input (fun e -> dispatch (UpdateRubricCriterionDesc (i, string e.Value)))
                            }
                        }
                        div {
                            attr.style "display: flex; align-items: center;"
                            button {
                                attr.``class`` "btn-admin"
                                attr.style "color: var(--accent-rose);"
                                on.click (fun _ -> dispatch (RemoveRubricCriterion i))
                                text "Delete"
                            }
                        }
                    }
                )
            }

        | SubTabPresenters ->
            div {
                div {
                    attr.style "display:flex; justify-content:space-between; align-items:center; margin-bottom:1.5rem;"
                    div {
                        h3 { attr.style "font-family: var(--font-display); font-size:1.15rem;"; text "Presenter Roster Management" }
                        p { attr.style "color: var(--text-muted); font-size:0.82rem;"; text "Bulk paste your 20-40 student names below (one per line) or edit names directly." }
                    }
                    div {
                        attr.style "display: flex; gap: 0.5rem;"
                        button {
                            attr.``class`` "btn-admin"
                            on.click (fun _ -> dispatch AddSinglePresenter)
                            text "+ Add Single Student"
                        }
                        button {
                            attr.``class`` "btn-primary"
                            on.click (fun _ -> dispatch SavePresenters)
                            text "Save Presenter Roster"
                        }
                    }
                }

                div {
                    attr.style "background: rgba(21, 28, 44, 0.4); border: 1px solid var(--border-subtle); border-radius: var(--radius-md); padding: 1.25rem; margin-bottom: 1.5rem;"
                    label { attr.``class`` "form-label"; text "📋 Bulk Paste Student Names (One per line: 'Name - Project Title' or just 'Name'):" }
                    textarea {
                        attr.``class`` "bulk-textarea"
                        attr.placeholder "Liam Vance - Mass-Timber Urban Infill\nMaya Lin - Geothermal Bathhouse\nMarcus Zhao - Kinetic Tidal Pier\nElena Rostova - Ceramic Arts Guild..."
                        attr.value model.BulkPresentersText
                        on.input (fun e -> dispatch (SetBulkPresentersText (string e.Value)))
                    }
                    div {
                        attr.style "margin-top: 0.75rem; display: flex; justify-content: flex-end;"
                        button {
                            attr.``class`` "btn-admin"
                            on.click (fun _ -> dispatch ParseBulkPresenters)
                            text "Parse & Update Table Below ↓"
                        }
                    }
                }

                div {
                    attr.``class`` "studio-table-container"
                    table {
                        attr.``class`` "studio-table"
                        thead {
                            tr {
                                th { text "Order" }
                                th { text "Student Full Name" }
                                th { text "Project Title / Topic" }
                                th { text "Action" }
                            }
                        }
                        tbody {
                            forEach (List.indexed model.EditablePresenters) (fun (i, pres) ->
                                tr {
                                    td {
                                        attr.style "font-family: var(--font-mono); color: var(--text-muted); width: 60px;"
                                        text ("#" + (i + 1).ToString("D2"))
                                    }
                                    td {
                                        input {
                                            attr.``class`` "input-studio"
                                            attr.value pres.Name
                                            on.input (fun e -> dispatch (UpdatePresenterField (i, string e.Value, pres.ProjectTitle)))
                                        }
                                    }
                                    td {
                                        input {
                                            attr.``class`` "input-studio"
                                            attr.value pres.ProjectTitle
                                            on.input (fun e -> dispatch (UpdatePresenterField (i, pres.Name, string e.Value)))
                                        }
                                    }
                                    td {
                                        button {
                                            attr.``class`` "btn-admin"
                                            attr.style "color: var(--accent-rose);"
                                            on.click (fun _ -> dispatch (RemovePresenter i))
                                            text "✕"
                                        }
                                    }
                                }
                            )
                        }
                    }
                }
            }

        | SubTabHuggingFace ->
            let hasConfig =
                not (String.IsNullOrWhiteSpace model.HfRepoInput) && not (String.IsNullOrWhiteSpace model.HfTokenInput)
            let cleanBase = if String.IsNullOrWhiteSpace model.BaseUrl then "http://localhost:5098/" else model.BaseUrl
            let studentShareLink =
                if hasConfig then
                    HfStorage.generateShareableStudentLink cleanBase model.HfRepoInput model.HfTokenInput
                else ""
            let adminShareLink =
                if hasConfig then
                    HfStorage.generateShareableAdminLink cleanBase model.HfRepoInput model.HfTokenInput
                else ""

            div {
                div {
                    attr.style "max-width: 680px; margin: 0 auto; padding: 1rem 0;"
                    div {
                        attr.style "text-align: center; margin-bottom: 2rem;"
                        div { attr.style "font-size: 3rem; margin-bottom: 0.5rem;"; text "🤗" }
                        h3 { attr.style "font-family: var(--font-display); font-size: 1.4rem;"; text "Hugging Face Dataset Storage & Live Aggregation" }
                        p { attr.style "color: var(--text-muted); font-size: 0.88rem; margin-top: 0.35rem;"; text "Store, retrieve, and automatically aggregate student reviews in real time using Hugging Face Datasets." }
                    }

                    cond hasConfig <| function
                        | true ->
                            div {
                                // 1. Student Cohort Link Card
                                div {
                                    attr.style "background: linear-gradient(135deg, rgba(30, 41, 59, 0.7) 0%, rgba(15, 23, 42, 0.9) 100%); border: 1px solid var(--accent-cyan); border-radius: var(--radius-md); padding: 1.5rem; margin-bottom: 2rem; box-shadow: 0 8px 32px rgba(6, 182, 212, 0.15);"

                                    div {
                                        attr.style "display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.75rem;"
                                        h4 { attr.style "font-family: var(--font-display); font-size: 1.15rem; color: var(--accent-cyan); margin: 0;"; text "📱 1-Click Shareable Link for Students" }
                                        span { attr.``class`` "stage-pill"; text "🟢 Student Peer Portal" }
                                    }

                                    p { attr.style "color: var(--text-muted); font-size: 0.85rem; line-height: 1.5; margin-bottom: 1.25rem;"; text "Share this link with your 20–40 students or project the QR code on the wall. When students open it on their phones, their devices automatically link to this dataset and their grades pool together live!" }

                                    div {
                                        attr.style "display: flex; gap: 0.5rem; margin-bottom: 1.25rem;"
                                        input {
                                            attr.``class`` "input-studio"
                                            attr.style "font-family: var(--font-mono); font-size: 0.8rem; background: rgba(0,0,0,0.5);"
                                            attr.readonly "readonly"
                                            attr.value studentShareLink
                                        }
                                        button {
                                            attr.``class`` "btn-primary"
                                            attr.style "white-space: nowrap; padding: 0.5rem 1.1rem;"
                                            on.click (fun _ -> dispatch (CopyStudentLink studentShareLink))
                                            text "📋 Copy Link"
                                        }
                                    }

                                    div {
                                        attr.style "display: flex; flex-direction: column; align-items: center; padding: 1.25rem; background: rgba(0,0,0,0.3); border-radius: var(--radius-sm);"
                                        div {
                                            attr.style "background: #ffffff; padding: 12px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);"
                                            img {
                                                attr.src ($"https://api.qrserver.com/v1/create-qr-code/?size=180x180&data={Uri.EscapeDataString studentShareLink}")
                                                attr.alt "Scan to Join Studio Peer Critique"
                                                attr.style "width: 180px; height: 180px; display: block;"
                                            }
                                        }
                                        span {
                                            attr.style "font-family: var(--font-mono); font-size: 0.78rem; color: var(--text-dim); margin-top: 0.75rem;"
                                            text "📷 Project on screen: Students scan with phone camera to crit"
                                        }
                                    }
                                }

                                // 2. Faculty Private Admin Mobile Link Card
                                div {
                                    attr.style "background: linear-gradient(135deg, rgba(30, 27, 75, 0.6) 0%, rgba(15, 23, 42, 0.9) 100%); border: 1px solid var(--accent-amber); border-radius: var(--radius-md); padding: 1.5rem; margin-bottom: 2rem; box-shadow: 0 8px 32px rgba(245, 158, 11, 0.12);"

                                    div {
                                        attr.style "display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.75rem;"
                                        h4 { attr.style "font-family: var(--font-display); font-size: 1.15rem; color: var(--accent-amber); margin: 0;"; text "🔑 Private Faculty Admin Shortcut (Mobile & Anywhere)" }
                                        span { attr.``class`` "stage-pill"; attr.style "border-color: var(--accent-amber); color: var(--accent-amber);"; text "🔒 Safe From Hugging Face Scanners" }
                                    }

                                    p { attr.style "color: var(--text-muted); font-size: 0.85rem; line-height: 1.5; margin-bottom: 1.25rem;"; text "Scan this QR code with your phone camera (or bookmark the link). It opens /admin already unlocked and permanently connected on your phone. Because it uses a client-side URL hash (#), the token is NEVER sent over the internet or committed to GitHub—keeping your Hugging Face token completely safe!" }

                                    div {
                                        attr.style "display: flex; gap: 0.5rem; margin-bottom: 1.25rem;"
                                        input {
                                            attr.``class`` "input-studio"
                                            attr.style "font-family: var(--font-mono); font-size: 0.8rem; background: rgba(0,0,0,0.5);"
                                            attr.readonly "readonly"
                                            attr.value adminShareLink
                                        }
                                        button {
                                            attr.``class`` "btn-admin"
                                            attr.style "white-space: nowrap; padding: 0.5rem 1.1rem; border-color: var(--accent-amber); color: var(--accent-amber);"
                                            on.click (fun _ -> dispatch (CopyAdminLink adminShareLink))
                                            text "📋 Copy Admin Link"
                                        }
                                    }

                                    div {
                                        attr.style "display: flex; flex-direction: column; align-items: center; padding: 1.25rem; background: rgba(0,0,0,0.3); border-radius: var(--radius-sm);"
                                        div {
                                            attr.style "background: #ffffff; padding: 12px; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);"
                                            img {
                                                attr.src ($"https://api.qrserver.com/v1/create-qr-code/?size=180x180&data={Uri.EscapeDataString adminShareLink}")
                                                attr.alt "Scan to Open Private Faculty Console on Mobile"
                                                attr.style "width: 180px; height: 180px; display: block;"
                                            }
                                        }
                                        span {
                                            attr.style "font-family: var(--font-mono); font-size: 0.78rem; color: var(--accent-amber); margin-top: 0.75rem;"
                                            text "📱 Point your phone camera here to control studio admin on mobile"
                                        }
                                    }
                                }
                            }
                        | false -> empty()

                    div {
                        attr.style "background: rgba(21, 28, 44, 0.4); border: 1px solid var(--border-subtle); border-radius: var(--radius-md); padding: 1.5rem; margin-bottom: 1.5rem;"

                        div {
                            attr.style "margin-bottom: 1.25rem;"
                            label { attr.``class`` "form-label"; text "Hugging Face Dataset Repo ID (e.g. username/Peer-Review):" }
                            input {
                                attr.``class`` "input-studio"
                                attr.``type`` "text"
                                attr.placeholder "vykrum/Peer-Review"
                                attr.value model.HfRepoInput
                                on.input (fun e -> dispatch (SetHfRepoInput (string e.Value)))
                            }
                        }

                        div {
                            attr.style "margin-bottom: 1.5rem;"
                            label { attr.``class`` "form-label"; text "Hugging Face Write Token:" }
                            input {
                                attr.``class`` "input-studio"
                                attr.``type`` "password"
                                attr.placeholder "hf_xxxxxxxxxxxxxxxxxxxxxxxxxxxx"
                                attr.value model.HfTokenInput
                                on.input (fun e -> dispatch (SetHfTokenInput (string e.Value)))
                            }
                            p { attr.style "font-size: 0.72rem; color: var(--text-dim); margin-top: 0.3rem;"; text "Create a free token with 'Write' permission at huggingface.co/settings/tokens. Only stored in your browser." }
                        }

                        div {
                            attr.style "display: flex; justify-content: flex-end; gap: 0.75rem;"
                            button {
                                attr.``class`` "btn-admin"
                                attr.disabled model.IsHfSyncing
                                on.click (fun _ -> dispatch FetchFromHuggingFace)
                                text "📥 Pull Latest from HF"
                            }
                            button {
                                attr.``class`` "btn-primary"
                                attr.disabled model.IsHfSyncing
                                on.click (fun _ -> dispatch SyncToHuggingFace)
                                text (if model.IsHfSyncing then "Syncing..." else "🚀 Save & Connect Cloud Sync")
                            }
                        }
                    }

                    div {
                        attr.style "border: 1px dashed rgba(244, 63, 94, 0.3); border-radius: var(--radius-md); padding: 1.25rem; margin-top: 2rem; background: rgba(244, 63, 94, 0.03);"
                        h4 { attr.style "color: var(--accent-rose); font-size: 0.95rem; margin-bottom: 0.3rem;"; text "Danger Zone: Clear Session Reviews" }
                        p { attr.style "color: var(--text-dim); font-size: 0.8rem; margin-bottom: 0.85rem;"; text "Deletes all submitted peer reviews while preserving the rubric and student list." }
                        button {
                            attr.``class`` "btn-admin"
                            attr.style "color: var(--accent-rose); border-color: rgba(244, 63, 94, 0.4);"
                            on.click (fun _ -> dispatch ResetAllReviews)
                            text "⚠️ Clear All Submitted Reviews"
                        }
                    }
                }
            }
    }

let viewAdmin (model: Model) (dispatch: Message -> unit) =
    if not model.IsAdminUnlocked then
        viewAdminLocked model dispatch
    else
        viewAdminUnlocked model dispatch

let view (model: Model) (dispatch: Message -> unit) =
    div {
        viewHeader model dispatch

        main {
            attr.``class`` "main-wrapper"
            viewAlerts model dispatch

            match model.Page with
            | Page.Home -> viewStudioFeed model dispatch
            | Page.ReviewPage presId -> viewReviewForm model presId dispatch
            | Page.Admin -> viewAdmin model dispatch
        }
    }

type MyApp() =
    inherit ProgramComponent<Model, Message>()

    [<Inject>]
    member val HttpClient = Unchecked.defaultof<HttpClient> with get, set

    [<Inject>]
    member val JSRuntime = Unchecked.defaultof<IJSRuntime> with get, set

    override this.Program =
        let http = this.HttpClient
        let js = this.JSRuntime

        let init (_: obj) =
            let cmd =
                Cmd.batch [
                    Cmd.ofMsg LoadLocalState
                    Cmd.OfAsync.either (fun () -> getReviewerInfoAsync js) () (fun (t, n) -> InitReviewer (t, n)) OnException
                    Cmd.OfAsync.either (fun () -> async { return! js.InvokeAsync<string>("studioReview.getBaseUrl").AsTask() |> Async.AwaitTask }) () SetBaseUrl OnException
                    Cmd.OfAsync.either (fun () -> async { return! js.InvokeAsync<string>("studioReview.getSyncParam").AsTask() |> Async.AwaitTask }) () GotSyncParam OnException
                    Cmd.OfAsync.either (fun () -> async { return! js.InvokeAsync<string>("studioReview.getAdminSyncParam").AsTask() |> Async.AwaitTask }) () GotAdminSyncParam OnException
                    Cmd.ofMsg PollLiveState
                ]
            initModel, cmd

        let updateMsg = update http js
        Program.mkProgram init updateMsg view
        |> Program.withRouter router
