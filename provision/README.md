# Worker provisioning

This repo includes a **FastAPI worker** at `main.py` that OptimoUI can use as a remote “agent”.

## Important: where does cTrader run?

The worker ultimately shells out to the cTrader CLI (`CTRADE_CLI_PATH`) via `subprocess`.

If your cTrader CLI is **macOS-only** (`cTrader.Mac`) or **Windows-only**, then:

- A **Linux** VM can still run the FastAPI worker, but it **cannot** run cTrader backtests unless you provide a Linux-compatible CLI (or use Wine, which is fragile).
- In practice, run workers on an OS where your cTrader CLI is runnable (often **Windows**), and keep OptimoUI (coordinator) anywhere.

If you want Linux workers, Spotware also publishes a **Linux Docker image** for cTrader CLI.
In that case you can run the worker itself as a container based on the cTrader CLI image (see below).

## Docker (Linux) worker with cTrader CLI inside

Files:
- `Dockerfile.ctrader`

Local run:
- `docker build -f Dockerfile.ctrader -t workerc-ctrader:latest .`
- `docker run --rm -p 1112:1112 -e OPTIMO_WORKER_PARALLEL=4 -v /var/lib/optimo-worker/runs:/data/worker_runs workerc-ctrader:latest`
- `curl http://127.0.0.1:1112/status`

## Ubuntu: auto-install Docker + auto-update image on every reboot

Files:
- `provision/linux/optimo-worker-docker-bootstrap.sh`
- `provision/linux/systemd/optimo-worker-docker-bootstrap.service`
- `provision/linux/optimo-worker-docker.env.example`

Typical install on the server:
- `sudo install -m 0755 provision/linux/optimo-worker-docker-bootstrap.sh /usr/local/bin/optimo-worker-docker-bootstrap.sh`
- `sudo install -m 0644 provision/linux/optimo-worker-docker.env.example /etc/optimo-worker-docker.env`
- `sudo install -m 0644 provision/linux/systemd/optimo-worker-docker-bootstrap.service /etc/systemd/system/optimo-worker-docker-bootstrap.service`
- `sudo systemctl daemon-reload`
- `sudo systemctl enable --now optimo-worker-docker-bootstrap`

## Ubuntu (systemd) setup

Files:
- `provision/linux/install_ubuntu.sh`
- `provision/linux/systemd/optimo-worker.service`
- `provision/linux/optimo-worker.env.example`
- `provision/linux/optimo-worker-update.sh`

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
- `provision/hetzner/README.md`
- `provision/hetzner/optimo-worker-docker.service`
- `provision/hetzner/optimo-worker-docker.env.example`

One-shot install (Hetzner/Ubuntu):
```bash
curl -fsSL https://raw.githubusercontent.com/<OWNER>/<REPO>/main/provision/hetzner/install.sh | sudo bash -s -- \
  --image ghcr.io/<owner>/<image>:latest \
  --parallel auto
```
With `--parallel auto`, effective worker slots = detected CPUs * `OPTIMO_WORKER_PARALLEL_PER_CORE` (default `2`).
dd
