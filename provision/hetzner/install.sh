#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
install.sh (Hetzner/Ubuntu) - installs Docker + systemd autostart for Optimo worker container.

Required:
  --image <ghcr.io/...:tag>

Optional:
  --port <1112>
  --root <host_path_for_runs>
  --parallel <auto|N>
  --container-name <optimo-worker>
  --telegram-token <token>   (optional) Send "online" message from worker
  --chat-id <id>             (optional) Telegram chat id (or set CHAT_DANIEL in env)
  --public-url <url>         (optional) e.g. http://<IP>:1112 to avoid IP autodetect
  --ghcr-user <user>      If provided together with --ghcr-token, runs `docker login ghcr.io`.
  --ghcr-token <token>

Example:
  sudo bash install.sh --image ghcr.io/wackyesolution/workerc-ctrader:latest --parallel auto
EOF
}

IMAGE=""
PORT="1112"
ROOT="/var/lib/optimo-worker/runs"
PARALLEL="auto"
CONTAINER_NAME="optimo-worker"
CTRADE_CLI_PATH="ctrader-cli"
TELEGRAM_BOT_TOKEN="${TELEGRAM_BOT_TOKEN:-}"
CHAT_ID="${CHAT_ID:-${CHAT_DANIEL:-}}"
PUBLIC_URL="${PUBLIC_URL:-${OPTIMO_WORKER_PUBLIC_URL:-}}"
GHCR_USER="${GHCR_USERNAME:-}"
GHCR_TOKEN="${GHCR_TOKEN:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --image) IMAGE="${2:-}"; shift 2;;
    --port) PORT="${2:-}"; shift 2;;
    --root) ROOT="${2:-}"; shift 2;;
    --parallel) PARALLEL="${2:-}"; shift 2;;
    --container-name) CONTAINER_NAME="${2:-}"; shift 2;;
    --ctrade-cli-path) CTRADE_CLI_PATH="${2:-}"; shift 2;;
    --telegram-token) TELEGRAM_BOT_TOKEN="${2:-}"; shift 2;;
    --chat-id) CHAT_ID="${2:-}"; shift 2;;
    --public-url) PUBLIC_URL="${2:-}"; shift 2;;
    --ghcr-user) GHCR_USER="${2:-}"; shift 2;;
    --ghcr-token) GHCR_TOKEN="${2:-}"; shift 2;;
    -h|--help) usage; exit 0;;
    *) echo "Unknown arg: $1" >&2; usage; exit 2;;
  esac
done

if [[ -z "$IMAGE" ]]; then
  echo "Missing --image" >&2
  usage
  exit 2
fi

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run as root (use sudo)." >&2
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

apt-get update
apt-get install -y --no-install-recommends ca-certificates curl python3

if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
  sh /tmp/get-docker.sh
fi

systemctl enable --now docker

if [[ -n "$GHCR_USER" && -n "$GHCR_TOKEN" ]]; then
  echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
else
  echo "[note] Skipping GHCR login. If your image is private, run: docker login ghcr.io" >&2
fi

mkdir -p "$ROOT"

EXISTING_TELEGRAM_BOT_TOKEN=""
EXISTING_CHAT_ID=""
EXISTING_PUBLIC_URL=""
if [[ -f /etc/optimo-worker-docker.env ]]; then
  # shellcheck disable=SC1091
  source /etc/optimo-worker-docker.env || true
  EXISTING_TELEGRAM_BOT_TOKEN="${TELEGRAM_BOT_TOKEN:-}"
  EXISTING_CHAT_ID="${CHAT_ID:-${CHAT_DANIEL:-}}"
  EXISTING_PUBLIC_URL="${OPTIMO_WORKER_PUBLIC_URL:-}"
fi

if [[ -z "$TELEGRAM_BOT_TOKEN" ]]; then
  TELEGRAM_BOT_TOKEN="$EXISTING_TELEGRAM_BOT_TOKEN"
fi
if [[ -z "$CHAT_ID" ]]; then
  CHAT_ID="$EXISTING_CHAT_ID"
fi
if [[ -z "$PUBLIC_URL" ]]; then
  PUBLIC_URL="$EXISTING_PUBLIC_URL"
fi

cat >/etc/optimo-worker-docker.env <<EOF
OPTIMO_WORKER_IMAGE=$IMAGE
OPTIMO_WORKER_CONTAINER_NAME=$CONTAINER_NAME
OPTIMO_WORKER_PORT=$PORT
OPTIMO_WORKER_ROOT=$ROOT
OPTIMO_WORKER_PARALLEL=$PARALLEL
CTRADE_CLI_PATH=$CTRADE_CLI_PATH
TELEGRAM_BOT_TOKEN=$TELEGRAM_BOT_TOKEN
CHAT_ID=$CHAT_ID
OPTIMO_WORKER_PUBLIC_URL=$PUBLIC_URL
EOF

cat >/usr/local/bin/optimo-worker-docker-bootstrap.sh <<'EOF'
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
OPTIMO_WORKER_PARALLEL="${OPTIMO_WORKER_PARALLEL:-auto}"
CTRADE_CLI_PATH="${CTRADE_CLI_PATH:-ctrader-cli}"

TELEGRAM_BOT_TOKEN="${TELEGRAM_BOT_TOKEN:-}"
CHAT_ID="${CHAT_ID:-${CHAT_DANIEL:-}}"
OPTIMO_WORKER_PUBLIC_URL="${OPTIMO_WORKER_PUBLIC_URL:-}"

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
  curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
  sh /tmp/get-docker.sh
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
  tg_args=()
  if [[ -n "$TELEGRAM_BOT_TOKEN" ]]; then
    tg_args+=( -e "TELEGRAM_BOT_TOKEN=$TELEGRAM_BOT_TOKEN" )
  fi
  if [[ -n "$CHAT_ID" ]]; then
    tg_args+=( -e "CHAT_ID=$CHAT_ID" )
  fi
  if [[ -n "$OPTIMO_WORKER_PUBLIC_URL" ]]; then
    tg_args+=( -e "OPTIMO_WORKER_PUBLIC_URL=$OPTIMO_WORKER_PUBLIC_URL" )
  fi
  docker run -d --name "$OPTIMO_WORKER_CONTAINER_NAME" --restart=always \
    -p "${OPTIMO_WORKER_PORT}:1112" \
    -e OPTIMO_WORKER_PARALLEL="$OPTIMO_WORKER_PARALLEL" \
    -e OPTIMO_WORKER_ROOT=/data/worker_runs \
    -e CTRADE_CLI_PATH="$CTRADE_CLI_PATH" \
    "${tg_args[@]}" \
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
EOF

chmod +x /usr/local/bin/optimo-worker-docker-bootstrap.sh

cat >/etc/systemd/system/optimo-worker-docker-bootstrap.service <<'EOF'
[Unit]
Description=Optimo Worker (Docker bootstrap/update)
After=network-online.target docker.service
Wants=network-online.target
Requires=docker.service

[Service]
Type=oneshot
EnvironmentFile=-/etc/optimo-worker-docker.env
ExecStart=/usr/local/bin/optimo-worker-docker-bootstrap.sh
RemainAfterExit=yes

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable --now optimo-worker-docker-bootstrap.service

echo "OK"
echo "  Worker status: http://127.0.0.1:${PORT}/status"
echo "  Logs:          tail -n 200 /var/log/optimo-worker-bootstrap.log"
