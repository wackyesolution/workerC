#!/usr/bin/env bash
set -euo pipefail

# Minimal installer for Ubuntu 22/24.
# Assumes you will:
# - install/configure cTrader CLI separately (or on Windows instead of Linux),
# - clone the repo into /opt/optimo/BravoOPTIMO_UI (private repo needs deploy key/PAT),
# - then enable the systemd service.

sudo apt-get update
sudo apt-get install -y --no-install-recommends \
  git ca-certificates curl \
  python3 python3-venv python3-pip \
  jq

sudo useradd -m -s /bin/bash optimo || true

sudo mkdir -p /opt/optimo
sudo chown -R optimo:optimo /opt/optimo

if [[ ! -d /opt/optimo/BravoOPTIMO_UI/.git ]]; then
  echo "Clone your repo into /opt/optimo/BravoOPTIMO_UI as user 'optimo' (private repo needs SSH key/PAT)."
  echo "Example:"
  echo "  sudo -u optimo git clone <YOUR_GIT_URL> /opt/optimo/BravoOPTIMO_UI"
fi

if [[ -d /opt/optimo/BravoOPTIMO_UI/backend ]]; then
  sudo -u optimo python3 -m venv /opt/optimo/BravoOPTIMO_UI/.venv
  sudo -u optimo /opt/optimo/BravoOPTIMO_UI/.venv/bin/pip install --no-cache-dir -r /opt/optimo/BravoOPTIMO_UI/backend/requirements.txt
fi

sudo install -m 0644 /opt/optimo/BravoOPTIMO_UI/backend/worker/provision/linux/optimo-worker.env.example /etc/optimo-worker.env
sudo install -m 0755 /opt/optimo/BravoOPTIMO_UI/backend/worker/provision/linux/optimo-worker-update.sh /usr/local/bin/optimo-worker-update.sh

sudo mkdir -p /var/lib/optimo-worker/runs
sudo chown -R optimo:optimo /var/lib/optimo-worker

sudo install -m 0644 /opt/optimo/BravoOPTIMO_UI/backend/worker/provision/linux/systemd/optimo-worker.service /etc/systemd/system/optimo-worker.service
sudo systemctl daemon-reload

echo "Next:"
echo "  1) Edit /etc/optimo-worker.env (set CTRADE_CLI_PATH etc.)"
echo "  2) sudo systemctl enable --now optimo-worker"
echo "  3) curl http://localhost:1112/status"

