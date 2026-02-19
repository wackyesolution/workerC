#!/usr/bin/env bash
set -euo pipefail

LOG="${OPTIMO_WORKER_BOOTSTRAP_LOG:-/var/log/optimo-worker-bootstrap.log}"
ENV_FILE="${OPTIMO_WORKER_DOCKER_ENV_FILE:-/etc/optimo-worker-docker.env}"

mkdir -p "$(dirname "$LOG")"
touch "$LOG"
chmod 0600 "$LOG" || true
exec >>"$LOG" 2>&1

ts() { date -u +"%Y-%m-%dT%H:%M:%SZ"; }
echo "[bootstrap] $(ts) starting"

if [[ -f "$ENV_FILE" ]]; then
  echo "[bootstrap] loading env $ENV_FILE"
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
else
  echo "[bootstrap] env file not found ($ENV_FILE); using defaults"
fi

OPTIMO_WORKER_IMAGE="${OPTIMO_WORKER_IMAGE:-ghcr.io/wackyesolution/workerc-ctrader:latest}"
OPTIMO_WORKER_CONTAINER_NAME="${OPTIMO_WORKER_CONTAINER_NAME:-optimo-worker}"
OPTIMO_WORKER_PORT="${OPTIMO_WORKER_PORT:-1112}"
OPTIMO_WORKER_ROOT="${OPTIMO_WORKER_ROOT:-/var/lib/optimo-worker/runs}"
OPTIMO_WORKER_PARALLEL="${OPTIMO_WORKER_PARALLEL:-4}"

need_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "[bootstrap] missing command: $1"
    return 1
  fi
}

install_docker_ubuntu() {
  echo "[bootstrap] installing docker (get.docker.com)"
  if ! command -v apt-get >/dev/null 2>&1; then
    echo "[bootstrap] apt-get not found; cannot auto-install docker on this OS"
    exit 1
  fi
  apt-get update
  apt-get install -y --no-install-recommends ca-certificates curl
  curl -fsSL https://get.docker.com | sh
}

start_docker_service() {
  if command -v systemctl >/dev/null 2>&1; then
    systemctl enable --now docker >/dev/null 2>&1 || true
    systemctl start docker >/dev/null 2>&1 || true
  else
    service docker start >/dev/null 2>&1 || true
  fi
}

if ! command -v docker >/dev/null 2>&1; then
  install_docker_ubuntu
fi

start_docker_service
need_cmd docker

mkdir -p "$OPTIMO_WORKER_ROOT"

echo "[bootstrap] pulling image $OPTIMO_WORKER_IMAGE"
docker pull "$OPTIMO_WORKER_IMAGE"

desired_image_id="$(docker image inspect -f '{{.Id}}' "$OPTIMO_WORKER_IMAGE")"
echo "[bootstrap] desired image id: $desired_image_id"

run_container() {
  echo "[bootstrap] (re)creating container $OPTIMO_WORKER_CONTAINER_NAME"
  docker rm -f "$OPTIMO_WORKER_CONTAINER_NAME" >/dev/null 2>&1 || true
  docker run -d --name "$OPTIMO_WORKER_CONTAINER_NAME" --restart=always \
    -p "${OPTIMO_WORKER_PORT}:1112" \
    -e OPTIMO_WORKER_PARALLEL="$OPTIMO_WORKER_PARALLEL" \
    -e OPTIMO_WORKER_ROOT=/data/worker_runs \
    -e CTRADE_CLI_PATH=ctrader-cli \
    -v "${OPTIMO_WORKER_ROOT}:/data/worker_runs" \
    "$OPTIMO_WORKER_IMAGE"
}

if ! docker container inspect "$OPTIMO_WORKER_CONTAINER_NAME" >/dev/null 2>&1; then
  run_container
  echo "[bootstrap] $(ts) done (created)"
  exit 0
fi

current_image_id="$(docker inspect -f '{{.Image}}' "$OPTIMO_WORKER_CONTAINER_NAME")"
running="$(docker inspect -f '{{.State.Running}}' "$OPTIMO_WORKER_CONTAINER_NAME" || echo "false")"
echo "[bootstrap] current container image id: $current_image_id"
echo "[bootstrap] container running: $running"

if [[ "$current_image_id" != "$desired_image_id" ]]; then
  echo "[bootstrap] image changed -> recreate"
  run_container
  echo "[bootstrap] $(ts) done (updated)"
  exit 0
fi

if [[ "$running" != "true" ]]; then
  echo "[bootstrap] starting existing container"
  docker start "$OPTIMO_WORKER_CONTAINER_NAME" >/dev/null
fi

echo "[bootstrap] $(ts) done (no change)"
