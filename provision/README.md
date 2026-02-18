# Worker provisioning (no Docker)

This repo includes a **FastAPI worker** at `backend/worker/main.py` that OptimoUI can use as a remote “agent”.

## Important: where does cTrader run?

The worker ultimately shells out to the cTrader CLI (`CTRADE_CLI_PATH`).

If your cTrader CLI is **macOS-only** (`cTrader.Mac`) or **Windows-only**, then:

- A **Linux** VM can still run the FastAPI worker, but it **cannot** run cTrader backtests unless you provide a Linux-compatible CLI (or use Wine, which is fragile).
- In practice, run workers on an OS where your cTrader CLI is runnable (often **Windows**), and keep OptimoUI (coordinator) anywhere.

If you want Linux workers, Spotware also publishes a **Linux Docker image** for cTrader CLI.
In that case you can run the worker itself as a container based on the cTrader CLI image (see below).

## Docker (Linux) worker with cTrader CLI inside

Files:
- `backend/worker/Dockerfile.ctrader`
- `docker-compose.worker-ctrader.yml`

Local run:
- `docker compose -f docker-compose.worker-ctrader.yml up --build`
- `curl http://localhost:1112/status`

## Ubuntu (systemd) setup

Files:
- `backend/worker/provision/linux/install_ubuntu.sh`
- `backend/worker/provision/linux/systemd/optimo-worker.service`
- `backend/worker/provision/linux/optimo-worker.env.example`
- `backend/worker/provision/linux/optimo-worker-update.sh`

What you get:
- `optimo-worker` systemd service on port `1112`
- On every boot/service start, it runs `optimo-worker-update.sh` to pull updates from GitHub (best-effort) and then starts the worker.

## Snapshot workflow (Hetzner Cloud)

Typical flow:
1. Create VM, configure it (cTrader CLI + worker + firewall + keys).
2. Verify `GET /status` works.
3. Take a **snapshot** image.
4. Create/destroy VMs from that snapshot as needed.

## Hetzner Cloud (recommended: Docker host + snapshot)

If you use the Spotware Linux cTrader CLI Docker image, the simplest Hetzner setup is:

1. Create an Ubuntu VM (temporary “golden” host).
2. Install Docker and login to GHCR once (`docker login ghcr.io`).
3. Install a systemd unit that pulls and runs the worker container on boot.
4. Test `GET /status`.
5. Take a snapshot from this configured VM.
6. Create new worker VMs from the snapshot whenever you need capacity.

Provisioning templates:
- `backend/worker/provision/hetzner/README.md`
- `backend/worker/provision/hetzner/optimo-worker-docker.service`
- `backend/worker/provision/hetzner/optimo-worker-docker.env.example`
