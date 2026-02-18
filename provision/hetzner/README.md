# Hetzner Cloud worker (Docker + snapshot)

Goal: run a worker on a plain Ubuntu VPS with **only Docker installed**, using the Spotware cTrader CLI image as the base.

This avoids installing cTrader “natively” on the host.

## One-time “golden VM” setup (then snapshot)

1) Create an Ubuntu VM on Hetzner (e.g. Ubuntu 24.04), attach your SSH key, allow inbound TCP `1112` (or restrict it to OptimoUI’s IP).

2) Install Docker:
```bash
curl -fsSL https://get.docker.com | sh
```

3) Login to GHCR (private image pull):
```bash
docker login ghcr.io
```

4) Copy templates onto the VM and configure:
- `/etc/optimo-worker-docker.env` (based on `optimo-worker-docker.env.example`)
- `/etc/systemd/system/optimo-worker-docker.service` (based on `optimo-worker-docker.service`)

5) Enable/start:
```bash
systemctl daemon-reload
systemctl enable --now optimo-worker-docker
curl http://127.0.0.1:1112/status
```

6) Take a Hetzner **snapshot** from this VM.

## Using the snapshot

- Create a new server from your snapshot (bigger/smaller type is fine if architecture matches).
- Ensure port `1112` is reachable.
- The systemd unit starts the container automatically.

## Register in OptimoUI

In OptimoUI → Worker Farm, add:
- `http://<WORKER_PUBLIC_IP>:1112`

Reminder: OptimoUI is configured to **refuse starting** distributed GA unless **all workers respond and are idle**.

