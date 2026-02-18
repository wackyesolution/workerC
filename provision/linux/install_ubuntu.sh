#!/usr/bin/env bash
set -euo pipefail

# Minimal installer for Ubuntu 22/24.
# Assumes you will:
# - install/configure cTrader CLI separately (or run the worker via Docker using Dockerfile.ctrader),
# - clone the repo into /opt/workerC (private repo needs deploy key/PAT),
# - then enable the systemd service.

sudo apt-get update
sudo apt-get install -y --no-install-recommends \
  git ca-certificates curl \
  python3 python3-venv python3-pip \
  jq

sudo useradd -m -s /bin/bash optimo || true

sudo mkdir -p /opt/workerC
sudo chown -R optimo:optimo /opt/workerC

if [[ ! -d /opt/workerC/.git ]]; then
  echo "Clone your repo into /opt/workerC as user 'optimo' (private repo needs SSH key/PAT)."
  echo "Example:"
  echo "  sudo -u optimo git clone <YOUR_GIT_URL> /opt/workerC"
fi

if [[ -f /opt/workerC/requirements.txt ]]; then
  sudo -u optimo python3 -m venv /opt/workerC/.venv
  sudo -u optimo /opt/workerC/.venv/bin/pip install --no-cache-dir -r /opt/workerC/requirements.txt
fi

sudo install -m 0644 /opt/workerC/provision/linux/optimo-worker.env.example /etc/optimo-worker.env
sudo install -m 0755 /opt/workerC/provision/linux/optimo-worker-update.sh /usr/local/bin/optimo-worker-update.sh

sudo mkdir -p /var/lib/optimo-worker/runs
sudo chown -R optimo:optimo /var/lib/optimo-worker

sudo install -m 0644 /opt/workerC/provision/linux/systemd/optimo-worker.service /etc/systemd/system/optimo-worker.service
sudo systemctl daemon-reload

echo "Next:"
echo "  1) Edit /etc/optimo-worker.env (set CTRADE_CLI_PATH etc.)"
echo "  2) sudo systemctl enable --now optimo-worker"
echo "  3) curl http://localhost:1112/status"
