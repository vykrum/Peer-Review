namespace StudioReview.Client

open System
open Bolero

/// Navigation routing endpoints
type Page =
    | [<EndPoint "/">] Home
    | [<EndPoint "/admin">] Admin
    | [<EndPoint "/review/{id}">] ReviewPage of id: string

/// A rubric grading criterion (e.g. Concept, Spatial Organization, Tectonics, Craft)
type RubricCriterion =
    {
        Id: string
        Title: string
        Description: string
        MaxScore: int
        Weight: float
    }

/// A student presenter in the architecture studio
type Presenter =
    {
        Id: string
        Name: string
        ProjectTitle: string
        Order: int
    }

/// Score for an individual criterion
type CriterionScore =
    {
        CriterionId: string
        Score: int
        Comment: string
    }

/// A complete peer review submitted anonymously
type Review =
    {
        Id: string
        ReviewerToken: string
        ReviewerNickname: string
        PresenterId: string
        SubmittedAt: DateTime
        Scores: CriterionScore list
        TotalScore: int
        GeneralFeedback: string
        Strengths: string
        Improvements: string
    }

/// Criterion statistical breakdown
type CriterionStat =
    {
        CriterionId: string
        Title: string
        AverageScore: float
        MaxScore: int
    }

/// Statistical summary for a presenter
type PresenterSummary =
    {
        Presenter: Presenter
        ReviewCount: int
        AverageTotalScore: float
        MaxPossibleScore: int
        CriterionStats: CriterionStat list
        FeedbackComments: string list
        StrengthsList: string list
        ImprovementsList: string list
    }

/// Hugging Face dataset and space backend configuration
type HuggingFaceConfig =
    {
        RepoId: string
        ApiToken: string
        AutoSync: bool
        LastSyncTime: DateTime option
        LastSyncStatus: string option
        SpaceUrl: string option
    }

/// Complete studio state
type StudioState =
    {
        StudioName: string
        StudioCode: string
        ActivePresenterId: string option
        Presenters: Presenter list
        Rubric: RubricCriterion list
        Reviews: Review list
        HfConfig: HuggingFaceConfig option
        LastUpdated: DateTime
    }
