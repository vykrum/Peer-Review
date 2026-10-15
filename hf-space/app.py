import os
import io
import json
import csv
from datetime import datetime
from typing import Dict, Any, List, Optional
from fastapi import FastAPI, Request, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import JSONResponse
import gradio as gr
from huggingface_hub import HfApi, CommitOperationAdd

# ZeroGPU compatibility handler
try:
    import spaces
    @spaces.GPU(duration=10)
    def gpu_init():
        return "ZeroGPU Initialized"
    gpu_init()
except Exception:
    pass

# Configuration
REPO_ID = os.environ.get("HF_DATASET_REPO", "vykrum/peer-review")
HF_TOKEN = os.environ.get("HF_TOKEN", "")

# In-memory cached state
CURRENT_STATE: Optional[Dict[str, Any]] = None

def get_hf_api() -> Optional[HfApi]:
    token = HF_TOKEN.strip() if HF_TOKEN else None
    if not token:
        # Check standard default token env var
        token = os.environ.get("HUGGING_FACE_HUB_TOKEN", "").strip() or None
    return HfApi(token=token) if token else HfApi()

def generate_csv(state: Dict[str, Any]) -> str:
    output = io.StringIO()
    presenters = state.get("Presenters", [])
    reviews = state.get("Reviews", [])
    rubric = state.get("Rubric", [])
    
    headers = ["Presenter Name", "Project Title", "Order", "Review Count", "Average Total Score"]
    for crit in rubric:
        headers.append(f"{crit.get('Title', '')} (Max {crit.get('MaxScore', 25)})")
    headers.extend(["Strengths", "Improvements", "General Critique"])
    
    writer = csv.writer(output)
    writer.writerow(headers)
    
    for p in sorted(presenters, key=lambda x: x.get("Order", 0)):
        p_id = p.get("Id")
        p_reviews = [r for r in reviews if r.get("PresenterId") == p_id]
        count = len(p_reviews)
        avg_score = round(sum(r.get("TotalScore", 0) for r in p_reviews) / count, 1) if count > 0 else 0.0
        
        crit_avgs = []
        for crit in rubric:
            c_id = crit.get("Id")
            scores = []
            for r in p_reviews:
                for s in r.get("Scores", []):
                    if s.get("CriterionId") == c_id:
                        scores.append(s.get("Score", 0))
            crit_avgs.append(round(sum(scores) / len(scores), 1) if scores else 0.0)
            
        strengths = " | ".join(r.get("Strengths", "") for r in p_reviews if r.get("Strengths"))
        improvements = " | ".join(r.get("Improvements", "") for r in p_reviews if r.get("Improvements"))
        general = " | ".join(r.get("GeneralFeedback", "") for r in p_reviews if r.get("GeneralFeedback"))
        
        row = [p.get("Name"), p.get("ProjectTitle"), p.get("Order"), count, avg_score] + crit_avgs + [strengths, improvements, general]
        writer.writerow(row)
        
    return output.getvalue()

def generate_markdown(state: Dict[str, Any]) -> str:
    presenters = state.get("Presenters", [])
    reviews = state.get("Reviews", [])
    rubric = state.get("Rubric", [])
    max_score = sum(c.get("MaxScore", 25) for c in rubric)
    
    lines = [
        f"# {state.get('StudioName', 'Architecture Studio')} — Peer Review Summary",
        f"**Generated:** {datetime.utcnow().strftime('%Y-%m-%d %H:%M:%S UTC')} | **Studio Code:** `{state.get('StudioCode', 'STUDIO-CRIT')}`",
        f"Total Reviews: **{len(reviews)}** across **{len(presenters)}** presenters.\n",
        "## Studio Scoreboard",
        "| Order | Presenter | Project Title | Reviews | Average Total |",
        "| :--- | :--- | :--- | :--- | :--- |"
    ]
    for p in sorted(presenters, key=lambda x: x.get("Order", 0)):
        p_reviews = [r for r in reviews if r.get("PresenterId") == p.get("Id")]
        count = len(p_reviews)
        avg_total = round(sum(r.get("TotalScore", 0) for r in p_reviews) / count, 1) if count > 0 else 0.0
        lines.append(f"| #{p.get('Order', 0):02d} | **{p.get('Name')}** | *{p.get('ProjectTitle')}* | {count} | **{avg_total}** / {max_score} |")
        
    return "\n".join(lines)

def push_to_dataset(state: Dict[str, Any]) -> bool:
    token = HF_TOKEN.strip() or os.environ.get("HUGGING_FACE_HUB_TOKEN", "").strip()
    if not token:
        print("[Notice] HF_TOKEN secret not set in Space. State preserved in-memory.")
        return False
    try:
        api = HfApi(token=token)
        state_json = json.dumps(state, indent=2).encode("utf-8")
        csv_data = generate_csv(state).encode("utf-8")
        md_data = generate_markdown(state).encode("utf-8")
        
        operations = [
            CommitOperationAdd(path_in_repo="studio_state.json", path_or_fileobj=state_json),
            CommitOperationAdd(path_in_repo="studio_reviews.csv", path_or_fileobj=csv_data),
            CommitOperationAdd(path_in_repo="CRIT_REPORT.md", path_or_fileobj=md_data),
        ]
        
        api.create_commit(
            repo_id=REPO_ID,
            repo_type="dataset",
            operations=operations,
            commit_message=f"Sync: {len(state.get('Reviews', []))} reviews, stage spotlight on {state.get('ActivePresenterId')}"
        )
        print(f"[Success] Pushed updated state to {REPO_ID}")
        return True
    except Exception as e:
        print(f"[Error] Failed to push commit to {REPO_ID}: {e}")
        return False

# Initialize FastAPI app
app = FastAPI(title="Studio Peer Review Cloud Sync", version="1.0.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/health")
def health():
    return {
        "status": "healthy",
        "dataset_repo": REPO_ID,
        "has_token": bool(HF_TOKEN or os.environ.get("HUGGING_FACE_HUB_TOKEN")),
        "cached_reviews": len(CURRENT_STATE.get("Reviews", [])) if CURRENT_STATE else 0
    }

@app.get("/state")
@app.get("/api/state")
def get_state():
    global CURRENT_STATE
    if CURRENT_STATE is not None:
        return CURRENT_STATE
        
    # Attempt to load from remote dataset
    token = HF_TOKEN.strip() or None
    try:
        import urllib.request
        url = f"https://huggingface.co/datasets/{REPO_ID}/raw/main/studio_state.json?t={int(datetime.utcnow().timestamp())}"
        req = urllib.request.Request(url)
        if token:
            req.add_header("Authorization", f"Bearer {token}")
        with urllib.request.urlopen(req, timeout=10) as response:
            if response.status == 200:
                data = json.loads(response.read().decode())
                CURRENT_STATE = data
                return data
    except Exception as e:
        print(f"[Notice] Could not load state from dataset ({e}). Returning empty default.")
        
    return {"StudioName": "Architecture Studio", "Presenters": [], "Reviews": [], "Rubric": []}

@app.post("/state")
@app.post("/api/state")
async def update_state(request: Request):
    global CURRENT_STATE
    body = await request.json()
    CURRENT_STATE = body
    CURRENT_STATE["LastUpdated"] = datetime.utcnow().isoformat() + "Z"
    
    # Push to Hugging Face dataset in background
    pushed = push_to_dataset(CURRENT_STATE)
    return {
        "status": "ok",
        "pushed_to_hf": pushed,
        "presenters_count": len(CURRENT_STATE.get("Presenters", [])),
        "reviews_count": len(CURRENT_STATE.get("Reviews", []))
    }

@app.post("/review")
@app.post("/api/review")
async def submit_review(request: Request):
    global CURRENT_STATE
    body = await request.json()
    new_review = body.get("review", body)
    
    if CURRENT_STATE is None:
        get_state()
        if CURRENT_STATE is None:
            CURRENT_STATE = {"StudioName": "Architecture Studio", "Presenters": [], "Reviews": [], "Rubric": []}
            
    # Merge review: replace any prior review from same reviewer for same presenter
    reviews = CURRENT_STATE.get("Reviews", [])
    rev_token = new_review.get("ReviewerToken")
    pres_id = new_review.get("PresenterId")
    
    updated_reviews = [r for r in reviews if not (r.get("ReviewerToken") == rev_token and r.get("PresenterId") == pres_id)]
    updated_reviews.insert(0, new_review)
    CURRENT_STATE["Reviews"] = updated_reviews
    CURRENT_STATE["LastUpdated"] = datetime.utcnow().isoformat() + "Z"
    
    pushed = push_to_dataset(CURRENT_STATE)
    return {
        "status": "ok",
        "pushed_to_hf": pushed,
        "total_reviews": len(updated_reviews)
    }

@app.post("/reset")
@app.post("/api/reset")
def reset_reviews():
    global CURRENT_STATE
    if CURRENT_STATE:
        CURRENT_STATE["Reviews"] = []
        CURRENT_STATE["LastUpdated"] = datetime.utcnow().isoformat() + "Z"
        pushed = push_to_dataset(CURRENT_STATE)
        return {"status": "ok", "pushed_to_hf": pushed, "message": "All reviews cleared."}
    return {"status": "ok", "message": "State was empty."}

# Mount Gradio dashboard for easy visual monitoring
with gr.Blocks(title="Studio Crit Sync Status") as demo:
    gr.Markdown("# 🏛️ Studio Crit // Live Cloud Sync Dashboard")
    gr.Markdown(f"Connected Dataset: [`{REPO_ID}`](https://huggingface.co/datasets/{REPO_ID})")
    
    with gr.Row():
        status_box = gr.Textbox(label="Cloud Sync Status", value="🟢 Backend Online & Serving REST API", interactive=False)
        token_status = gr.Textbox(label="HF Token Configured", value="✅ Connected via Space Secret" if HF_TOKEN else "⚠️ Secret HF_TOKEN not set yet", interactive=False)
        
    refresh_btn = gr.Button("🔄 Refresh Status")
    
    def refresh_ui():
        has_t = bool(HF_TOKEN or os.environ.get("HUGGING_FACE_HUB_TOKEN"))
        revs = len(CURRENT_STATE.get("Reviews", [])) if CURRENT_STATE else 0
        return f"🟢 Online | {revs} reviews tracked", "✅ Valid" if has_t else "⚠️ Set HF_TOKEN in Space Settings"
        
    refresh_btn.click(fn=refresh_ui, outputs=[status_box, token_status])
app = gr.mount_gradio_app(app, demo, path="/")

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="0.0.0.0", port=7860)
