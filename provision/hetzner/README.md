# Hetzner Cloud worker (Docker + snapshot)

Goal: run a worker on a plain Ubuntu VPS with **only Docker installed**, using the Spotware cTrader CLI image as the base.

This avoids installing cTrader “natively” on the host.

## One-time “golden VM” setup (then snapshot)

1) Create an Ubuntu VM on Hetzner (e.g. Ubuntu 24.04), attach your SSH key, allow inbound TCP `1112` (or restrict it to OptimoUI’s IP).

2) Install with one command (Docker + autostart + pull on reboot):
```bash
curl -fsSL https://raw.githubusercontent.com/<OWNER>/<REPO>/main/provision/hetzner/install.sh | sudo bash -s -- \
  --image ghcr.io/wackyesolution/workerc-ctrader:latest \
  --parallel auto
```

If the image is private, run `docker login ghcr.io` first (or pass `--ghcr-user/--ghcr-token`).
Default with `--parallel auto`: effective slots = detected CPUs * `OPTIMO_WORKER_PARALLEL_PER_CORE` (default `2`).

Optional: Telegram “online” message (be aware tokens can end up in shell history; prefer setting env in `/etc/optimo-worker-docker.env`):
- via args: `--telegram-token ... --chat-id ... --public-url http://<IP>:1112`
- or via env (works great with `curl | bash`):
  - `sudo TELEGRAM_BOT_TOKEN=... CHAT_ID=... OPTIMO_WORKER_PUBLIC_URL=http://<IP>:1112 bash -s -- ...`

3) Verify:
```bash
curl http://127.0.0.1:1112/status
```

4) Take a Hetzner **snapshot** from this VM.

## Using the snapshot

- Create a new server from your snapshot (bigger/smaller type is fine if architecture matches).
- Ensure port `1112` is reachable.
- The systemd unit starts the container automatically.

## Register in OptimoUI

In OptimoUI → Worker Farm, add:
- `http://<WORKER_PUBLIC_IP>:1112`

Reminder: OptimoUI is configured to **refuse starting** distributed GA unless **all workers respond and are idle**.
