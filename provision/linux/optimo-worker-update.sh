#!/usr/bin/env bash
set -euo pipefail

REPO_DIR="${OPTIMO_WORKER_REPO_DIR:-/opt/optimo/BravoOPTIMO_UI}"
REMOTE="${OPTIMO_WORKER_GIT_REMOTE:-origin}"
BRANCH="${OPTIMO_WORKER_GIT_BRANCH:-main}"
LOG="${OPTIMO_WORKER_UPDATE_LOG:-/var/log/optimo-worker-update.log}"

mkdir -p "$(dirname "$LOG")"
touch "$LOG"

{
  echo "[update] $(date -u +"%Y-%m-%dT%H:%M:%SZ")"
  if [[ ! -d "$REPO_DIR/.git" ]]; then
    echo "[update] repo not found at $REPO_DIR; skipping"
    exit 0
  fi

  cd "$REPO_DIR"
  git fetch --prune "$REMOTE" || { echo "[update] git fetch failed; keeping current"; exit 0; }

  CURRENT="$(git rev-parse HEAD)"
  TARGET="$(git rev-parse "$REMOTE/$BRANCH")"
  if [[ "$CURRENT" == "$TARGET" ]]; then
    echo "[update] already up to date ($CURRENT)"
    exit 0
  fi

  echo "[update] updating $CURRENT -> $TARGET"
  git reset --hard "$REMOTE/$BRANCH"

  if [[ -x "$REPO_DIR/.venv/bin/python" ]]; then
    "$REPO_DIR/.venv/bin/pip" install --no-cache-dir -r "$REPO_DIR/backend/requirements.txt"
  else
    echo "[update] venv missing; skipping pip"
  fi
} >>"$LOG" 2>&1

