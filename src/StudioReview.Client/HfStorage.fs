namespace StudioReview.Client

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.JSInterop

module HfStorage =

    let jsonOptions =
        let opt = JsonSerializerOptions(WriteIndented = true)
        opt.Converters.Add(JsonFSharpConverter())
        opt

    let defaultRubric : RubricCriterion list =
        [
            {
                Id = "crit_concept"
                Title = "1. Concept & Contextual Premise"
                Description = "Clarity of theoretical stance, urban/landscape dialogue, and architectural diagram."
                MaxScore = 25
                Weight = 1.0
            }
            {
                Id = "crit_spatial"
                Title = "2. Spatial Experience & Program Flow"
                Description = "Sectional choreography, natural daylighting, proportion, and programmatic hierarchy."
                MaxScore = 25
                Weight = 1.0
            }
            {
                Id = "crit_tectonics"
                Title = "3. Tectonics, Materiality & Climate Systems"
                Description = "Structural logic, envelope articulation, passive environmental strategies, and detail."
                MaxScore = 25
                Weight = 1.0
            }
            {
                Id = "crit_craft"
                Title = "4. Drawing Craft, Models & Oral Defense"
                Description = "Precision of orthographic drawings, physical model craft, and response to jury questions."
                MaxScore = 25
                Weight = 1.0
            }
        ]

    let defaultPresenters : Presenter list =
        [
            { Id = "pres_1"; Name = "Liam Vance"; ProjectTitle = "Mass-Timber Urban Infill & Civic Archive"; Order = 1 }
            { Id = "pres_2"; Name = "Maya Lin-Davies"; ProjectTitle = "Geothermal Subterranean Bathhouse"; Order = 2 }
            { Id = "pres_3"; Name = "Marcus Zhao"; ProjectTitle = "Kinetic Tidal Research Station & Pier"; Order = 3 }
            { Id = "pres_4"; Name = "Elena Rostova"; ProjectTitle = "Post-Industrial Ceramic Arts Guild"; Order = 4 }
            { Id = "pres_5"; Name = "Kai Tanaka"; ProjectTitle = "Adaptive CLT Housing Cooperative"; Order = 5 }
            { Id = "pres_6"; Name = "Priya Patel"; ProjectTitle = "Mycelium Biomaterial Fabrication Lab"; Order = 6 }
            { Id = "pres_7"; Name = "Jordan Hayes"; ProjectTitle = "Floating Wetland Climate Observatory"; Order = 7 }
            { Id = "pres_8"; Name = "Chloe Dubois"; ProjectTitle = "Monolithic Rammed-Earth Winery"; Order = 8 }
            { Id = "pres_9"; Name = "Mateo Silva"; ProjectTitle = "Urban Micro-Mobility Interchange Canopy"; Order = 9 }
            { Id = "pres_10"; Name = "Amara Okafor"; ProjectTitle = "Afro-Futurist Cultural Exchange Courtyard"; Order = 10 }
            { Id = "pres_11"; Name = "Soren Lindqvist"; ProjectTitle = "Arctic Permafrost Field Station"; Order = 11 }
            { Id = "pres_12"; Name = "Aisha Al-Mansoor"; ProjectTitle = "Wind-Tower Passive Cooling Institute"; Order = 12 }
            { Id = "pres_13"; Name = "Felix Berg"; ProjectTitle = "Tensile Cloud Botany Conservatory"; Order = 13 }
            { Id = "pres_14"; Name = "Zara Qureshi"; ProjectTitle = "Waterfront Acoustic Performance Vessel"; Order = 14 }
            { Id = "pres_15"; Name = "Julian Vance"; ProjectTitle = "Vertical Aeroponic Farm & Market"; Order = 15 }
            { Id = "pres_16"; Name = "Hana Mori"; ProjectTitle = "Modular Bamboo Disaster Resilience Pods"; Order = 16 }
            { Id = "pres_17"; Name = "Lucas Rossi"; ProjectTitle = "Canal Ecological Remediation Center"; Order = 17 }
            { Id = "pres_18"; Name = "Freja Møller"; ProjectTitle = "Circular Economy Timber Mill & Workshop"; Order = 18 }
        ]

    let defaultSeedState : StudioState =
        let initialReviews : Review list =
            [
                {
                    Id = Guid.NewGuid().ToString("N")
                    ReviewerToken = "seed_rev_alpha"
                    ReviewerNickname = "Brutalist Pavilion #24"
                    PresenterId = "pres_1"
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-35.0)
                    Scores =
                        [
                            { CriterionId = "crit_concept"; Score = 24; Comment = "Compelling adaptive reuse thesis." }
                            { CriterionId = "crit_spatial"; Score = 23; Comment = "The double-height reading atrium is stunning." }
                            { CriterionId = "crit_tectonics"; Score = 22; Comment = "Glulam connections are well thought out." }
                            { CriterionId = "crit_craft"; Score = 24; Comment = "Exquisite 1:50 physical basswood model." }
                        ]
                    TotalScore = 93
                    GeneralFeedback = "Outstanding presentation. The sectional perspective clearly sells the light quality."
                    Strengths = "Daylight studies, structural articulation, thoughtful urban porch."
                    Improvements = "Consider emergency egress stair placement on the northeast facade."
                }
                {
                    Id = Guid.NewGuid().ToString("N")
                    ReviewerToken = "seed_rev_beta"
                    ReviewerNickname = "Parametric Truss #17"
                    PresenterId = "pres_1"
                    SubmittedAt = DateTime.UtcNow.AddMinutes(-20.0)
                    Scores =
                        [
                            { CriterionId = "crit_concept"; Score = 22; Comment = "Strong civic response to the neighborhood." }
                            { CriterionId = "crit_spatial"; Score = 21; Comment = "Circulation through the archive stacks feels fluid." }
                            { CriterionId = "crit_tectonics"; Score = 24; Comment = "Excellent acoustic baffle integration in timber ceilings." }
                            { CriterionId = "crit_craft"; Score = 21; Comment = "Axonometric drawing is very crisp." }
                        ]
                    TotalScore = 88
                    GeneralFeedback = "Great clarity of intent and confident delivery during Q&A."
                    Strengths = "Timber structural grid rhythm and public threshold."
                    Improvements = "Detail the weather-seal connection between existing masonry and timber insert."
                }
            ]

        {
            StudioName = "ARCH 401: Advanced Spatial Design Studio"
            StudioCode = "STUDIO-CRIT-WASM"
            ActivePresenterId = Some "pres_1"
            Presenters = defaultPresenters
            Rubric = defaultRubric
            Reviews = initialReviews
            HfConfig =
                Some
                    {
                        RepoId = "vykrum/peer-review"
                        ApiToken = ""
                        AutoSync = true
                        LastSyncTime = None
                        LastSyncStatus = Some "Ready to connect"
                        SpaceUrl = Some "https://vykrum-peer-review-sync.hf.space"
                    }
            LastUpdated = DateTime.UtcNow
        }

    let loadFromLocalStorage (js: IJSRuntime) : Async<StudioState> =
        async {
            try
                let! json = js.InvokeAsync<string>("studioReview.getLocalStorage", "studio_wasm_state").AsTask() |> Async.AwaitTask
                if String.IsNullOrWhiteSpace(json) then
                    return defaultSeedState
                else
                    return JsonSerializer.Deserialize<StudioState>(json, jsonOptions)
            with _ ->
                return defaultSeedState
        }

    let saveToLocalStorage (js: IJSRuntime) (state: StudioState) : Async<unit> =
        async {
            try
                let json = JsonSerializer.Serialize(state, jsonOptions)
                do! js.InvokeVoidAsync("studioReview.setLocalStorage", "studio_wasm_state", json).AsTask() |> Async.AwaitTask
            with _ -> ()
        }

    let computeSummaries (state: StudioState) : PresenterSummary list =
        let maxPossible = state.Rubric |> List.sumBy (fun c -> c.MaxScore)
        state.Presenters
        |> List.sortBy (fun p -> p.Order)
        |> List.map (fun presenter ->
            let reviews = state.Reviews |> List.filter (fun r -> r.PresenterId = presenter.Id)
            let count = reviews.Length
            let avgTotal =
                if count = 0 then 0.0
                else (reviews |> List.sumBy (fun r -> float r.TotalScore)) / float count

            let critStats =
                state.Rubric
                |> List.map (fun crit ->
                    let critScores =
                        reviews
                        |> List.choose (fun r ->
                            r.Scores
                            |> List.tryFind (fun s -> s.CriterionId = crit.Id)
                            |> Option.map (fun s -> float s.Score)
                        )
                    let avg =
                        if critScores.IsEmpty then 0.0
                        else (critScores |> List.sum) / float critScores.Length
                    {
                        CriterionId = crit.Id
                        Title = crit.Title
                        AverageScore = Math.Round(avg, 1)
                        MaxScore = crit.MaxScore
                    }
                )

            let comments =
                reviews
                |> List.map (fun r -> r.GeneralFeedback)
                |> List.filter (not << String.IsNullOrWhiteSpace)

            let strengths =
                reviews
                |> List.map (fun r -> r.Strengths)
                |> List.filter (not << String.IsNullOrWhiteSpace)

            let improvements =
                reviews
                |> List.map (fun r -> r.Improvements)
                |> List.filter (not << String.IsNullOrWhiteSpace)

            {
                Presenter = presenter
                ReviewCount = count
                AverageTotalScore = Math.Round(avgTotal, 1)
                MaxPossibleScore = maxPossible
                CriterionStats = critStats
                FeedbackComments = comments
                StrengthsList = strengths
                ImprovementsList = improvements
            }
        )

    let generateCsv (state: StudioState) : string =
        let sb = StringBuilder()
        let critHeaders = state.Rubric |> List.map (fun c -> $"\"{c.Title} (Max {c.MaxScore})\"") |> String.concat ","
        let maxPossible = state.Rubric |> List.sumBy (fun c -> c.MaxScore)
        sb.AppendLine($"\"Presenter Name\",\"Project Title\",\"Order\",\"Review Count\",\"Average Total (Max {maxPossible})\",{critHeaders},\"Strengths\",\"Improvements\",\"General Critique\"") |> ignore

        let sanitize (s: string) =
            if String.IsNullOrWhiteSpace(s) then ""
            else s.Replace("\"", "\"\"").Replace("\n", " ; ")

        for presenter in state.Presenters |> List.sortBy (fun p -> p.Order) do
            let reviews = state.Reviews |> List.filter (fun r -> r.PresenterId = presenter.Id)
            let count = reviews.Length
            let avgTotal =
                if count = 0 then 0.0
                else (reviews |> List.sumBy (fun r -> float r.TotalScore)) / float count

            let critAvgs =
                state.Rubric
                |> List.map (fun crit ->
                    let scores =
                        reviews
                        |> List.choose (fun r ->
                            r.Scores
                            |> List.tryFind (fun s -> s.CriterionId = crit.Id)
                            |> Option.map (fun s -> float s.Score)
                        )
                    if scores.IsEmpty then "0.0"
                    else (Math.Round((scores |> List.sum) / float scores.Length, 1)).ToString()
                )
                |> String.concat ","

            let allStrengths = reviews |> List.map (fun r -> sanitize r.Strengths) |> List.filter (not << String.IsNullOrWhiteSpace) |> String.concat " | "
            let allImprovements = reviews |> List.map (fun r -> sanitize r.Improvements) |> List.filter (not << String.IsNullOrWhiteSpace) |> String.concat " | "
            let allGeneral = reviews |> List.map (fun r -> sanitize r.GeneralFeedback) |> List.filter (not << String.IsNullOrWhiteSpace) |> String.concat " | "

            sb.AppendLine($"\"{sanitize presenter.Name}\",\"{sanitize presenter.ProjectTitle}\",{presenter.Order},{count},{Math.Round(avgTotal, 1)},{critAvgs},\"{allStrengths}\",\"{allImprovements}\",\"{allGeneral}\"") |> ignore

        sb.ToString()

    let generateMarkdown (state: StudioState) : string =
        let sb = StringBuilder()
        let maxPossible = state.Rubric |> List.sumBy (fun c -> c.MaxScore)
        let timeStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
        sb.AppendLine("# " + state.StudioName + " - Studio Peer Review Summary") |> ignore
        sb.AppendLine("Generated on: " + timeStr + " UTC | Studio Code: `" + state.StudioCode + "`") |> ignore
        sb.AppendLine("Total Reviews Recorded: **" + string state.Reviews.Length + "** across **" + string state.Presenters.Length + "** students.\n") |> ignore

        sb.AppendLine("## Studio Scoreboard") |> ignore
        sb.AppendLine("| Order | Presenter | Project Title | Reviews | Average Score | Status |") |> ignore
        sb.AppendLine("| :--- | :--- | :--- | :--- | :--- | :--- |") |> ignore

        for presenter in state.Presenters |> List.sortBy (fun p -> p.Order) do
            let reviews = state.Reviews |> List.filter (fun r -> r.PresenterId = presenter.Id)
            let count = reviews.Length
            let avgTotal =
                if count = 0 then 0.0
                else (reviews |> List.sumBy (fun r -> float r.TotalScore)) / float count
            let status = if count >= 10 then "Completed" elif count > 0 then "In Progress" else "Pending"
            let avgStr = (Math.Round(avgTotal, 1)).ToString("F1")
            sb.AppendLine("| " + string presenter.Order + " | **" + presenter.Name + "** | *" + presenter.ProjectTitle + "* | " + string count + " | **" + avgStr + "** / " + string maxPossible + " | " + status + " |") |> ignore

        sb.AppendLine("\n---\n") |> ignore
        sb.AppendLine("## Qualitative Juror Feedback By Presenter\n") |> ignore

        for presenter in state.Presenters |> List.sortBy (fun p -> p.Order) do
            let reviews = state.Reviews |> List.filter (fun r -> r.PresenterId = presenter.Id)
            sb.AppendLine("### " + string presenter.Order + ". " + presenter.Name + " — *" + presenter.ProjectTitle + "*") |> ignore
            if reviews.IsEmpty then
                sb.AppendLine("*No peer reviews submitted yet.*\n") |> ignore
            else
                let avgTotal = (reviews |> List.sumBy (fun r -> float r.TotalScore)) / float reviews.Length
                let avgStr = (Math.Round(avgTotal, 1)).ToString("F1")
                sb.AppendLine("- **Average Total:** " + avgStr + " / " + string maxPossible + " (" + string reviews.Length + " peer reviews)") |> ignore
                sb.AppendLine("- **Rubric Breakdown:**") |> ignore
                for crit in state.Rubric do
                    let scores =
                        reviews
                        |> List.choose (fun r ->
                            r.Scores
                            |> List.tryFind (fun s -> s.CriterionId = crit.Id)
                            |> Option.map (fun s -> float s.Score)
                        )
                    let avg = if scores.IsEmpty then 0.0 else (scores |> List.sum) / float scores.Length
                    let cAvgStr = (Math.Round(avg, 1)).ToString("F1")
                    sb.AppendLine("  - " + crit.Title + ": **" + cAvgStr + "** / " + string crit.MaxScore) |> ignore

                sb.AppendLine("\n**Highlighted Strengths:**") |> ignore
                for r in reviews |> List.filter (fun r -> not (String.IsNullOrWhiteSpace r.Strengths)) do
                    sb.AppendLine("- *" + r.ReviewerNickname + "*: " + r.Strengths) |> ignore

                sb.AppendLine("\n**Constructive Critique & Areas for Iteration:**") |> ignore
                for r in reviews |> List.filter (fun r -> not (String.IsNullOrWhiteSpace r.Improvements)) do
                    sb.AppendLine("- *" + r.ReviewerNickname + "*: " + r.Improvements) |> ignore

                sb.AppendLine("\n**General Critique Notes:**") |> ignore
                for r in reviews |> List.filter (fun r -> not (String.IsNullOrWhiteSpace r.GeneralFeedback)) do
                    sb.AppendLine("> \"" + r.GeneralFeedback + "\" — *" + r.ReviewerNickname + "*") |> ignore
                sb.AppendLine() |> ignore

        sb.ToString()

    /// Fetch latest StudioState from Hugging Face Dataset (Raw JSON endpoint)
    let fetchFromHuggingFace (http: HttpClient) (repoId: string) (tokenOpt: string option) : Async<Result<StudioState, string>> =
        async {
            if String.IsNullOrWhiteSpace(repoId) then
                return Error "Hugging Face Repo ID is required."
            else
                try
                    let cleanRepo = repoId.Trim().Trim('/')
                    let cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    let url = $"https://huggingface.co/datasets/{cleanRepo}/raw/main/studio_state.json?_t={cacheBuster}"
                    use req = new HttpRequestMessage(HttpMethod.Get, url)
                    req.Headers.CacheControl <- CacheControlHeaderValue(NoCache = true, NoStore = true)
                    match tokenOpt with
                    | Some token when not (String.IsNullOrWhiteSpace token) ->
                        req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", token.Trim())
                    | _ -> ()

                    let! resp = http.SendAsync(req) |> Async.AwaitTask
                    if resp.IsSuccessStatusCode then
                        let! json = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        let loadedState = JsonSerializer.Deserialize<StudioState>(json, jsonOptions)
                        return Ok loadedState
                    else
                        let! err = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return Error $"Hugging Face returned HTTP {(int)resp.StatusCode}: {err}. (If the dataset was just created, push initial state first)."
                with ex ->
                    return Error $"Failed to fetch from Hugging Face: {ex.Message}"
        }

    /// Commit StudioState, CSV, and Markdown to Hugging Face Dataset via Commit API
    let commitToHuggingFace (http: HttpClient) (repoId: string) (apiToken: string) (state: StudioState) : Async<Result<string, string>> =
        async {
            if String.IsNullOrWhiteSpace(repoId) || String.IsNullOrWhiteSpace(apiToken) then
                return Error "Both Hugging Face Repo ID (e.g. 'username/studio-reviews') and API Token are required."
            else
                try
                    let cleanRepo = repoId.Trim().Trim('/')
                    let commitUrl = $"https://huggingface.co/api/datasets/{cleanRepo}/commit/main"

                    let stateJson = JsonSerializer.Serialize(state, jsonOptions)
                    let csvData = generateCsv state
                    let mdData = generateMarkdown state

                    let timeNow = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                    let readmeText =
                        "---\n" +
                        "title: Architecture Studio Peer Review Dataset\n" +
                        "tags:\n- architecture\n- peer-review\n- design-studio\n---\n" +
                        "# " + state.StudioName + "\n\n" +
                        "Live anonymous peer review data from the architecture studio critique.\n\n" +
                        "- Studio Code: `" + state.StudioCode + "`\n" +
                        "- Last Synced: " + timeNow + " UTC\n" +
                        "- Total Presenters: " + string state.Presenters.Length + "\n" +
                        "- Total Reviews: " + string state.Reviews.Length + "\n"

                    let headerLine =
                        let h =
                            {|
                                key = "header"
                                value =
                                    {|
                                        summary = $"Studio Peer Review: {state.Reviews.Length} reviews recorded"
                                        description = "Automated commit from Bolero WASM Architecture Studio Peer Review Client"
                                    |}
                            |}
                        JsonSerializer.Serialize(h)

                    let makeFileLine (path: string) (contentBytes: byte[]) =
                        let f =
                            {|
                                key = "file"
                                value =
                                    {|
                                        content = Convert.ToBase64String(contentBytes)
                                        path = path
                                        encoding = "base64"
                                    |}
                            |}
                        JsonSerializer.Serialize(f)

                    let stateLine = makeFileLine "studio_state.json" (Encoding.UTF8.GetBytes(stateJson))
                    let csvLine = makeFileLine "studio_reviews.csv" (Encoding.UTF8.GetBytes(csvData))
                    let mdLine = makeFileLine "CRIT_REPORT.md" (Encoding.UTF8.GetBytes(mdData))
                    let readmeLine = makeFileLine "README.md" (Encoding.UTF8.GetBytes(readmeText))

                    let ndjsonPayload = String.concat "\n" [ headerLine; stateLine; csvLine; mdLine; readmeLine ]

                    use req = new HttpRequestMessage(HttpMethod.Post, commitUrl)
                    req.Headers.Authorization <- AuthenticationHeaderValue("Bearer", apiToken.Trim())
                    req.Content <- new StringContent(ndjsonPayload, Encoding.UTF8, "application/x-ndjson")

                    let! resp = http.SendAsync(req) |> Async.AwaitTask
                    if resp.IsSuccessStatusCode then
                        let liveUrl = $"https://huggingface.co/datasets/{cleanRepo}"
                        return Ok $"Successfully synced to Hugging Face! Live dataset available at: {liveUrl}"
                    else
                        let! err = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return Error $"Hugging Face Commit API returned HTTP {(int)resp.StatusCode}: {err}. (Ensure the dataset repository exists at https://huggingface.co/datasets/{cleanRepo} and your token has 'Write' permission)."
                with ex ->
                    return Error $"Commit failed: {ex.Message}"
        }

    /// Fetch latest StudioState from Hugging Face Space backend (Zero Token REST endpoint)
    let fetchFromSpace (http: HttpClient) (spaceUrl: string) : Async<Result<StudioState, string>> =
        async {
            if String.IsNullOrWhiteSpace spaceUrl then
                return Error "Space URL is required."
            else
                try
                    let cleanUrl = spaceUrl.Trim().TrimEnd('/')
                    let cacheBuster = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    let url = $"{cleanUrl}/state?_t={cacheBuster}"
                    use req = new HttpRequestMessage(HttpMethod.Get, url)
                    req.Headers.CacheControl <- CacheControlHeaderValue(NoCache = true, NoStore = true)
                    let! resp = http.SendAsync(req) |> Async.AwaitTask
                    if resp.IsSuccessStatusCode then
                        let! json = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        let loadedState = JsonSerializer.Deserialize<StudioState>(json, jsonOptions)
                        return Ok loadedState
                    else
                        let! err = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return Error $"Space returned HTTP {(int)resp.StatusCode}: {err}"
                with ex ->
                    return Error $"Failed to reach Hugging Face Space at {spaceUrl}: {ex.Message}"
        }

    /// Sync full StudioState (roster, rubric, stage presenter) to Hugging Face Space (Zero Token)
    let syncStateToSpace (http: HttpClient) (spaceUrl: string) (state: StudioState) : Async<Result<string, string>> =
        async {
            if String.IsNullOrWhiteSpace spaceUrl then
                return Error "Space URL is required."
            else
                try
                    let cleanUrl = spaceUrl.Trim().TrimEnd('/')
                    let url = $"{cleanUrl}/state"
                    let json = JsonSerializer.Serialize(state, jsonOptions)
                    use content = new StringContent(json, Encoding.UTF8, "application/json")
                    let! resp = http.PostAsync(url, content) |> Async.AwaitTask
                    if resp.IsSuccessStatusCode then
                        return Ok $"Successfully synced state to Space ({cleanUrl})!"
                    else
                        let! err = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return Error $"Space returned HTTP {(int)resp.StatusCode}: {err}"
                with ex ->
                    return Error $"Failed to sync to Space: {ex.Message}"
        }

    /// Submit student review to Space backend (Zero Token)
    let submitReviewToSpace (http: HttpClient) (spaceUrl: string) (newReview: Review) (localStudio: StudioState) : Async<Result<StudioState, string>> =
        async {
            if String.IsNullOrWhiteSpace spaceUrl then
                return Ok localStudio
            else
                try
                    let cleanUrl = spaceUrl.Trim().TrimEnd('/')
                    let url = $"{cleanUrl}/review"
                    let json = JsonSerializer.Serialize(newReview, jsonOptions)
                    use content = new StringContent(json, Encoding.UTF8, "application/json")
                    let! resp = http.PostAsync(url, content) |> Async.AwaitTask
                    if resp.IsSuccessStatusCode then
                        let! fetchRes = fetchFromSpace http spaceUrl
                        match fetchRes with
                        | Ok remState -> return Ok remState
                        | Error _ ->
                            let mergedReviews =
                                newReview :: (localStudio.Reviews |> List.filter (fun r -> not (r.ReviewerToken = newReview.ReviewerToken && r.PresenterId = newReview.PresenterId)))
                            return Ok { localStudio with Reviews = mergedReviews; LastUpdated = DateTime.UtcNow }
                    else
                        let! err = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
                        return Error $"Space returned HTTP {(int)resp.StatusCode}: {err}"
                with ex ->
                    return Error $"Failed to submit review to Space: {ex.Message}"
        }

    /// Fetches latest state from Hugging Face, appends/merges the student's review, and pushes the combined state
    let submitReviewAndSync (http: HttpClient) (repoId: string) (apiToken: string) (newReview: Review) (localStudio: StudioState) : Async<Result<StudioState, string>> =
        async {
            if String.IsNullOrWhiteSpace(repoId) || String.IsNullOrWhiteSpace(apiToken) then
                // If HF is not configured, just return local state
                return Ok localStudio
            else
                try
                    let! remoteRes = fetchFromHuggingFace http repoId (Some apiToken)
                    let baseState =
                        match remoteRes with
                        | Ok rem -> rem
                        | Error _ -> localStudio

                    // Merge review: remove any prior review by this reviewer for this presenter, then prepend new review
                    let mergedReviews =
                        newReview :: (baseState.Reviews |> List.filter (fun r -> not (r.ReviewerToken = newReview.ReviewerToken && r.PresenterId = newReview.PresenterId)))

                    let updatedState =
                        { baseState with
                            Reviews = mergedReviews
                            LastUpdated = DateTime.UtcNow
                            // Preserve active presenter from remote if available
                            ActivePresenterId = match baseState.ActivePresenterId with Some _ as p -> p | None -> localStudio.ActivePresenterId
                        }

                    let! commitRes = commitToHuggingFace http repoId apiToken updatedState
                    match commitRes with
                    | Ok _ -> return Ok updatedState
                    | Error err -> return Error err
                with ex ->
                    return Error ex.Message
        }

    /// Generate shareable student link (Zero-token Space URL or Direct Dataset)
    let generateShareableStudentLink (baseUrl: string) (spaceUrl: string) (repoId: string) (token: string) : string =
        let rawBase = if baseUrl.EndsWith("/") then baseUrl else baseUrl + "/"
        let cleanBase =
            if rawBase.EndsWith("/admin/") then rawBase.Substring(0, rawBase.Length - 6)
            elif rawBase.EndsWith("/admin") then rawBase.Substring(0, rawBase.Length - 5) + "/"
            else rawBase
        if not (String.IsNullOrWhiteSpace spaceUrl) then
            let cleanSpace = spaceUrl.Trim().TrimEnd('/')
            let payload = {| space = cleanSpace |}
            let json = JsonSerializer.Serialize(payload)
            let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            let encoded = Uri.EscapeDataString(b64)
            $"{cleanBase}?sync={encoded}"
        elif not (String.IsNullOrWhiteSpace repoId) then
            let cleanRepo = repoId.Trim().Trim('/')
            let payload = {| repo = cleanRepo; token = token.Trim() |}
            let json = JsonSerializer.Serialize(payload)
            let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            let encoded = Uri.EscapeDataString(b64)
            $"{cleanBase}?sync={encoded}"
        else
            cleanBase

    /// Generate shareable faculty admin link (Zero-token Space URL or Direct Dataset)
    let generateShareableAdminLink (baseUrl: string) (spaceUrl: string) (repoId: string) (token: string) : string =
        let rawBase = if baseUrl.EndsWith("/") then baseUrl else baseUrl + "/"
        let cleanBase =
            if rawBase.EndsWith("/admin/") then rawBase
            elif rawBase.EndsWith("/admin") then rawBase + "/"
            else rawBase + "admin"
        if not (String.IsNullOrWhiteSpace spaceUrl) then
            let cleanSpace = spaceUrl.Trim().TrimEnd('/')
            let payload = {| space = cleanSpace |}
            let json = JsonSerializer.Serialize(payload)
            let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            let encoded = Uri.EscapeDataString(b64)
            $"{cleanBase}#admin_sync={encoded}"
        elif not (String.IsNullOrWhiteSpace repoId) then
            let cleanRepo = repoId.Trim().Trim('/')
            let payload = {| repo = cleanRepo; token = token.Trim() |}
            let json = JsonSerializer.Serialize(payload)
            let b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            let encoded = Uri.EscapeDataString(b64)
            $"{cleanBase}#admin_sync={encoded}"
        else
            cleanBase

    /// Parse sync parameter JSON from URL (?sync=... or #admin_sync=...)
    let parseSyncParam (syncJson: string) : HuggingFaceConfig option =
        if String.IsNullOrWhiteSpace(syncJson) then None
        else
            try
                use doc = JsonDocument.Parse(syncJson)
                let root = doc.RootElement
                if root.TryGetProperty("space", ref (Unchecked.defaultof<JsonElement>)) then
                    let spaceProp = root.GetProperty("space")
                    let space = spaceProp.GetString()
                    if not (String.IsNullOrWhiteSpace space) then
                        Some
                            {
                                RepoId = "vykrum/peer-review"
                                ApiToken = ""
                                AutoSync = true
                                LastSyncTime = None
                                LastSyncStatus = Some "Connected via Zero-Token Space"
                                SpaceUrl = Some (space.Trim())
                            }
                    else None
                elif root.TryGetProperty("repo", ref (Unchecked.defaultof<JsonElement>)) then
                    let repo = root.GetProperty("repo").GetString()
                    let token = if root.TryGetProperty("token", ref (Unchecked.defaultof<JsonElement>)) then root.GetProperty("token").GetString() else ""
                    if not (String.IsNullOrWhiteSpace repo) then
                        Some
                            {
                                RepoId = repo.Trim()
                                ApiToken = if isNull token then "" else token.Trim()
                                AutoSync = true
                                LastSyncTime = None
                                LastSyncStatus = Some "Connected via 1-Click Link"
                                SpaceUrl = None
                            }
                    else None
                else None
            with _ -> None


