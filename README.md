# 🏛️ STUDIO CRIT // Architecture Peer Review (Pure WASM)

> **Serverless, zero-login peer critique application for architectural studios** built entirely in **F#** using **Bolero WebAssembly** (.NET 10). Designed for cohorts of 20–40 students to review each other sequentially after design presentations, with automated Hugging Face Dataset persistence and a dedicated faculty administration dashboard.

---

## 📐 Key Capabilities

1. **Pure Client-Side WebAssembly (No Backend Server Required)**:
   - Built with F# and Bolero, running directly in the browser via WebAssembly.
   - Hostable anywhere statically: Hugging Face Static Spaces, GitHub Pages, Netlify, or any static file host.

2. **Zero-Login Anonymous Reviewer Identity**:
   - Each student browser automatically generates a cryptographically random, persistent reviewer token stored in `localStorage`.
   - Students receive an architectural codename (e.g., *Tectonic Brise-Soleil #53*, *Brutalist Pavilion #24*, *Cantilever Tracery #81*).
   - The token enforces **strict single-submission per presenter** while keeping juror identity 100% anonymous.

3. **Dedicated URLs**:
   - `/` — **Student Critique Portal**: Live presentation stage beacon, progress tracker, cohort presentation roster, interactive rubric scoring sliders, and qualitative feedback cards.
   - `/admin` — **Faculty & Juror Administration**: Secure console (default passcode: `studio2026`) for live spotlight switching, rubric customization, roster management, and live score matrix.
   - `/review/{presenter-id}` — Direct link to grade a specific presenter.

4. **Dynamic Rubric & Mark Customization**:
   - Admin can add, edit, or remove rubric criteria dimensions, customize descriptions, and set max points.
   - Pre-seeded with architectural standards (Concept & Contextual Premise, Spatial Experience & Program Flow, Tectonics & Materiality, Graphic & Model Defense).

5. **20–40 Student Roster Manager**:
   - Bulk-paste full studio cohorts in `Name - Project Title` format.
   - Add, edit, or reorder presenters dynamically.

6. **Serverless Hugging Face Dataset Hub Integration**:
   - **Retrieval**: Direct browser `fetch()` of `studio_state.json` from the Hugging Face raw dataset repository (CORS enabled).
   - **Storage**: Commits reviews, updated rubrics, and rosters directly via the Hugging Face Hub REST Commit API (`POST https://huggingface.co/api/datasets/{repo}/commit/main`).
   - Automatically generates and commits:
     - `studio_state.json`: Master state.
     - `studio_reviews.csv`: Tidy evaluation matrix with all numerical dimensions and comments.
     - `CRIT_REPORT.md`: Comprehensive Markdown report ready for studio pin-up and faculty records.
   - Instant local fallback: All actions are automatically mirrored in browser `localStorage`.

---

## 🚀 Running Locally

From the project root:

```bash
cd C:\Users\vykru\.gemini\antigravity-ide\scratch\studio-peer-review
dotnet run --project src/StudioReview.Client
```

The application will launch at:
- **Student View**: [http://localhost:5098/](http://localhost:5098/)
- **Admin Console**: [http://localhost:5098/admin](http://localhost:5098/admin) (Passcode: `studio2026`)

---

## 🌐 Deploying to Hugging Face Spaces (Static Web)

Because this is a pure WebAssembly application with zero server dependencies, you can deploy it to a free Hugging Face Static Space in 2 minutes:

1. Create a new Space on [Hugging Face](https://huggingface.co/new-space) and select **Static HTML**.
2. Run the release build locally:
   ```bash
   dotnet publish src/StudioReview.Client/StudioReview.Client.fsproj -c Release -o dist
   ```
3. Upload the contents of `dist/wwwroot` to the root of your Hugging Face Space repository.
4. Share the URL with your studio cohort!

---

## 📁 Project Architecture

```
studio-peer-review/
├── StudioReview.sln
├── src/
│   └── StudioReview.Client/
│       ├── StudioReview.Client.fsproj  # Bolero BlazorWebAssembly .NET 10 project
│       ├── StudioModel.fs              # Domain types, Elmish endpoints & routing
│       ├── HfStorage.fs                # HF Hub REST API & browser localStorage sync
│       ├── Main.fs                     # Elmish MVU (Model, Update, View, Admin)
│       ├── Startup.fs                  # WebAssemblyHostBuilder bootstrap
│       └── wwwroot/
│           ├── index.html              # HTML5 host shell
│           ├── studio.js               # Anonymous identity & download helpers
│           └── css/
│               └── index.css           # Architectural Swiss dark theme design system
└── README.md
```
