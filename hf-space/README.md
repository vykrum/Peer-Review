---
title: Studio Peer Review Sync Backend
emoji: 🏛️
colorFrom: blue
colorTo: indigo
sdk: gradio
sdk_version: 4.44.0
app_file: app.py
pinned: false
---

# Studio Crit Peer Review // Cloud Sync Backend

This Hugging Face Space acts as the secure, zero-token cloud bridge for the **Studio Crit Architecture WASM Client**.

### Secure Token Isolation
- The Hugging Face Write Token (`HF_TOKEN`) is stored safely as a **Space Secret** inside Hugging Face.
- Students and Faculty connect without typing, managing, or exposing any API tokens.
- All student reviews, stage spotlight shifts, and rubric updates are aggregated and pushed to the dataset: [`vykrum/peer-review`](https://huggingface.co/datasets/vykrum/peer-review).
