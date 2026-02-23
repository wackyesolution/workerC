from __future__ import annotations

import asyncio
import base64
from collections import deque
import json
import os
import shutil
import subprocess
import threading
import time
import uuid
import zipfile
import shlex
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Callable, Literal, Optional
from urllib.parse import urlencode
from urllib import request as urlrequest
from urllib import error as urlerror

from fastapi import FastAPI, HTTPException, Request
from pydantic import BaseModel, Field


CTRADE_BIN = os.environ.get(
    "CTRADE_CLI_PATH",
    "/Applications/cTrader.app/Contents/MacOS/cTrader.Mac",
)

WORKER_ROOT = Path(os.environ.get("OPTIMO_WORKER_ROOT", "./data/worker_runs")).expanduser().resolve()
WORKER_ROOT.mkdir(parents=True, exist_ok=True)

def _auto_parallel_default() -> int:
    try:
        return len(os.sched_getaffinity(0))  # type: ignore[attr-defined]
    except Exception:
        return int(os.cpu_count() or 1)


def _ctrader_like_parallel_default(cpu_cores: int) -> int:
    # Align auto-parallelism with cTrader's default allocated cores:
    # floor(cpu/2) + 1, capped to available cores.
    cores = max(1, int(cpu_cores or 1))
    return min(cores, (cores // 2) + 1)


def _int_env(name: str, default: int) -> int:
    raw = os.environ.get(name)
    if raw is None:
        return default
    raw = raw.strip()
    if not raw or raw.lower() == "auto":
        return default
    try:
        return int(raw)
    except Exception:
        return default


def _float_env(name: str, default: float) -> float:
    raw = os.environ.get(name)
    if raw is None:
        return float(default)
    raw = raw.strip()
    if not raw:
        return float(default)
    try:
        return float(raw)
    except Exception:
        return float(default)


def _bool_env(name: str, default: bool = False) -> bool:
    raw = os.environ.get(name)
    if raw is None:
        return bool(default)
    text = str(raw).strip().lower()
    if text in {"1", "true", "yes", "on"}:
        return True
    if text in {"0", "false", "no", "off"}:
        return False
    return bool(default)


def _guess_ctrade_cli_dir(cli_bin: str) -> str:
    value = str(cli_bin or "").strip()
    if not value:
        return "/app"
    try:
        p = Path(value).expanduser()
        if p.exists():
            if p.is_file():
                return str(p.resolve().parent)
            if p.is_dir():
                return str(p.resolve())
    except Exception:
        pass
    return "/app"


AUTO_PARALLEL_BASE = max(1, _auto_parallel_default())
AUTO_PARALLEL_CTRADER = _ctrader_like_parallel_default(AUTO_PARALLEL_BASE)
# Keep OPTIMO_WORKER_PARALLEL_PER_CORE for custom scaling; default is 1 to
# preserve cTrader-like auto settings out of the box.
PARALLEL_PER_CORE = max(1, _int_env("OPTIMO_WORKER_PARALLEL_PER_CORE", 1))
MAX_PARALLEL = max(1, _int_env("OPTIMO_WORKER_PARALLEL", AUTO_PARALLEL_CTRADER * PARALLEL_PER_CORE))
CUSTOM_CLI_PATCHED = _bool_env("OPTIMO_CUSTOM_CLI_PATCHED", True)
CLI_PATCHED_DOTNET_PATH = str(os.environ.get("OPTIMO_CLI_PATCHED_DOTNET_PATH", "dotnet")).strip() or "dotnet"
CLI_PATCHED_HOST_PATH = str(
    os.environ.get("OPTIMO_CLI_PATCHED_HOST_PATH", "/app/worker/cli_patched_host/Optimo.CliPatchedHost.dll")
).strip() or "/app/worker/cli_patched_host/Optimo.CliPatchedHost.dll"
CLI_PATCHED_CLI_DIR = str(os.environ.get("CTRADE_CLI_DIR", _guess_ctrade_cli_dir(CTRADE_BIN))).strip() or "/app"
CALLBACK_BATCH_SIZE = max(1, _int_env("OPTIMO_WORKER_CALLBACK_BATCH_SIZE", 10))
CALLBACK_BATCH_FLUSH_SECONDS = max(0.1, _float_env("OPTIMO_WORKER_CALLBACK_BATCH_FLUSH_SECONDS", 1.0))
CALLBACK_POST_TIMEOUT_SECONDS = max(3, _int_env("OPTIMO_WORKER_CALLBACK_TIMEOUT_SECONDS", 10))


def now_utc_iso() -> str:
    return datetime.utcnow().isoformat(timespec="seconds") + "Z"


def ensure_dir(p: Path) -> None:
    p.mkdir(parents=True, exist_ok=True)


def write_events(path: Path) -> None:
    # cTrader creates empty events.json files; keep it empty for compatibility
    path.write_text("", encoding="utf-8")


def write_cbotset(path: Path, params: dict[str, Any], symbol: str, period: str) -> None:
    payload = {
        "Chart": {"Symbol": symbol, "Period": period},
        "Parameters": params,
    }
    path.write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")


def parse_report(report_json: Path) -> dict[str, Any] | None:
    if not report_json.exists() or report_json.stat().st_size == 0:
        return None
    try:
        obj = json.loads(report_json.read_text(encoding="utf-8", errors="ignore"))
    except Exception:
        return None

    main = obj.get("main", {}) if isinstance(obj, dict) else {}
    trade = obj.get("tradeStatistics", {}) if isinstance(obj, dict) else {}
    equity = obj.get("equity", {}) if isinstance(obj, dict) else {}
    pf = trade.get("profitFactor") or {}
    total_trades = trade.get("totalTrades") or {}
    winning_trades = trade.get("winningTrades") or {}
    losing_trades = trade.get("losingTrades") or {}
    avg_trade = trade.get("averageTrade") or {}
    return {
        "main": main,
        "trade": trade,
        "equity": equity,
        "netProfit": main.get("netProfit") if main.get("netProfit") is not None else trade.get("netProfit"),
        "endingEquity": main.get("endingEquity"),
        "endingBalance": main.get("endingBalance"),
        "profitFactor": pf.get("all"),
        "totalTrades": total_trades.get("all"),
        "winningTrades": winning_trades.get("all"),
        "losingTrades": losing_trades.get("all"),
        "maxEquityDrawdownPercent": equity.get("maxEquityDrawdownPercent"),
        "maxBalanceDrawdownPercent": equity.get("maxBalanceDrawdownPercent"),
        "maxEquityDrawdownAbsolute": equity.get("maxEquityDrawdownAbsolute"),
        "maxBalanceDrawdownAbsolute": equity.get("maxBalanceDrawdownAbsolute"),
        "averageTrade": avg_trade.get("all") if isinstance(avg_trade, dict) else avg_trade,
    }


def zip_dir_to_b64(dir_path: Path) -> str:
    tmp_zip = dir_path.with_suffix(".zip")
    if tmp_zip.exists():
        tmp_zip.unlink()
    with zipfile.ZipFile(tmp_zip, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for child in dir_path.rglob("*"):
            if child.is_file():
                zf.write(child, arcname=child.relative_to(dir_path))
    data = tmp_zip.read_bytes()
    try:
        tmp_zip.unlink()
    except Exception:
        pass
    return base64.b64encode(data).decode("ascii")


def zip_pass_dirs_to_b64(run_dir: Path, pass_ids: list[int]) -> str | None:
    unique_ids: list[int] = []
    seen: set[int] = set()
    for raw in pass_ids:
        pid = int(raw or 0)
        if pid <= 0 or pid in seen:
            continue
        unique_ids.append(pid)
        seen.add(pid)
    if not unique_ids:
        return None

    tmp_zip = run_dir / f".callback_batch_{uuid.uuid4().hex}.zip"
    files_written = 0
    try:
        with zipfile.ZipFile(tmp_zip, "w", compression=zipfile.ZIP_DEFLATED) as zf:
            for pass_id in unique_ids:
                pass_dir = run_dir / str(pass_id)
                if not pass_dir.exists():
                    continue
                for child in pass_dir.rglob("*"):
                    if not child.is_file():
                        continue
                    rel = child.relative_to(pass_dir)
                    zf.write(child, arcname=str(Path(str(pass_id)) / rel))
                    files_written += 1
        if files_written <= 0:
            return None
        data = tmp_zip.read_bytes()
        return base64.b64encode(data).decode("ascii")
    finally:
        try:
            tmp_zip.unlink()
        except Exception:
            pass


def post_json(url: str, payload: dict[str, Any], timeout: int = 10) -> tuple[bool, str | None]:
    req = urlrequest.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json"},
        method="POST",
    )
    try:
        with urlrequest.urlopen(req, timeout=timeout) as resp:
            _ = resp.read()
        return True, None
    except urlerror.HTTPError as exc:
        try:
            detail = exc.read().decode("utf-8", errors="ignore")
        except Exception:
            detail = str(exc)
        return False, f"HTTP {exc.code}: {detail}"
    except Exception as exc:
        return False, str(exc)


def _detect_public_ip(timeout: int = 5) -> str | None:
    for u in ("https://api.ipify.org", "https://ifconfig.me/ip"):
        try:
            with urlrequest.urlopen(u, timeout=timeout) as resp:
                ip = resp.read().decode("utf-8", errors="ignore").strip()
            if ip:
                return ip
        except Exception:
            continue
    return None


def _telegram_send_message(token: str, chat_id: str, text: str, timeout: int = 10) -> None:
    url = f"https://api.telegram.org/bot{token}/sendMessage"
    data = urlencode({"chat_id": chat_id, "text": text}).encode("utf-8")
    req = urlrequest.Request(url, data=data, method="POST")
    with urlrequest.urlopen(req, timeout=timeout) as resp:
        _ = resp.read()


def run_backtest(
    algo_path: Path,
    cbotset_path: Path,
    start: str,
    end: str,
    data_mode: Literal["ticks", "m1"],
    ctid: str,
    pwd_file: Path,
    account: str,
    symbol: str,
    period: str,
    report_html: Path,
    report_json: Path,
    log_path: Path,
    timeout_seconds: int,
    balance: float | None,
    stop_requested: Optional[Callable[[], bool]] = None,
    on_proc_start: Optional[Callable[[subprocess.Popen], None]] = None,
    on_proc_end: Optional[Callable[[int], None]] = None,
) -> bool:
    cmd_prefix = _resolve_ctrade_cmd_prefix()
    cmd = [
        *cmd_prefix,
        "backtest",
        str(algo_path),
        str(cbotset_path),
        f"--start={start}",
        f"--end={end}",
        f"--data-mode={data_mode}",
        f"--ctid={ctid}",
        f"--pwd-file={str(pwd_file)}",
        f"--account={account}",
        f"--symbol={symbol}",
        f"--period={period}",
        f"--report={str(report_html)}",
        f"--report-json={str(report_json)}",
    ]
    if balance is not None:
        cmd.append(f"--balance={balance}")

    def reports_ready() -> bool:
        return (
            report_html.exists()
            and report_json.exists()
            and report_html.stat().st_size > 0
            and report_json.stat().st_size > 0
        )

    with log_path.open("w", encoding="utf-8") as logf:
        logf.write(f"[started_at_utc] {now_utc_iso()}\n")
        logf.write(f"[command] {' '.join(cmd)}\n\n")
        logf.flush()
        proc = subprocess.Popen(cmd, stdout=logf, stderr=subprocess.STDOUT, text=True)
        if on_proc_start:
            try:
                on_proc_start(proc)
            except Exception:
                pass

        start_ts = time.time()
        success = False
        outcome = "unknown"
        try:
            while True:
                if stop_requested and stop_requested():
                    try:
                        proc.terminate()
                        proc.wait(timeout=3)
                    except Exception:
                        try:
                            proc.kill()
                            proc.wait(timeout=1)
                        except Exception:
                            pass
                    outcome = "stopped_by_request"
                    break
                if reports_ready():
                    if proc.poll() is None:
                        try:
                            proc.wait(timeout=2)
                        except Exception:
                            try:
                                proc.terminate()
                                proc.wait(timeout=2)
                            except Exception:
                                try:
                                    proc.kill()
                                    proc.wait(timeout=1)
                                except Exception:
                                    pass
                    success = True
                    outcome = "reports_ready"
                    break
                if proc.poll() is not None:
                    outcome = f"process_exited_rc_{proc.returncode}"
                    break
                if timeout_seconds and (time.time() - start_ts) >= timeout_seconds:
                    try:
                        proc.terminate()
                        proc.wait(timeout=3)
                    except Exception:
                        try:
                            proc.kill()
                            proc.wait(timeout=1)
                        except Exception:
                            pass
                    outcome = "timeout"
                    break
                time.sleep(1)
        finally:
            if on_proc_end:
                try:
                    on_proc_end(int(proc.pid))
                except Exception:
                    pass
            elapsed_total = round(max(0.0, time.time() - start_ts), 3)
            logf.write(f"\n[finished_at_utc] {now_utc_iso()}\n")
            logf.write(f"[elapsed_seconds_total] {elapsed_total}\n")
            logf.write(f"[outcome] {outcome}\n")
            logf.flush()

    return success or reports_ready()


class _PatchedCliClient:
    def __init__(
        self,
        slot_index: int,
        on_proc_start: Optional[Callable[[subprocess.Popen[str]], None]] = None,
        on_proc_end: Optional[Callable[[int], None]] = None,
    ):
        self.slot_index = int(slot_index)
        self._seq = 0
        self._on_proc_start = on_proc_start
        self._on_proc_end = on_proc_end
        self._lock = threading.RLock()
        self._cv = threading.Condition(self._lock)
        self._responses: dict[str, dict[str, Any]] = {}
        self._stderr_tail: deque[str] = deque(maxlen=200)
        self._closed = False
        self._generation = 0
        self.proc: subprocess.Popen[str] | None = None
        self._stdout_thread: threading.Thread | None = None
        self._stderr_thread: threading.Thread | None = None
        self._start_process()

    @property
    def pid(self) -> int:
        if self.proc and self.proc.pid:
            return int(self.proc.pid)
        return 0

    def _start_process(self) -> None:
        with self._lock:
            self._closed = False
            self._responses.clear()
            self._generation += 1
        cmd = [CLI_PATCHED_DOTNET_PATH, CLI_PATCHED_HOST_PATH, "--cli-dir", CLI_PATCHED_CLI_DIR]
        proc = subprocess.Popen(
            cmd,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            bufsize=1,
        )
        self.proc = proc
        if self._on_proc_start:
            try:
                self._on_proc_start(proc)
            except Exception:
                pass
        self._stdout_thread = threading.Thread(target=self._stdout_reader, daemon=True)
        self._stderr_thread = threading.Thread(target=self._stderr_reader, daemon=True)
        self._stdout_thread.start()
        self._stderr_thread.start()

    def _stdout_reader(self) -> None:
        proc = self.proc
        if proc is None or proc.stdout is None:
            return
        for raw in proc.stdout:
            line = raw.strip()
            if not line:
                continue
            try:
                payload = json.loads(line)
            except Exception:
                with self._lock:
                    self._stderr_tail.append(f"[patched-host-stdout] {line}")
                    self._cv.notify_all()
                continue
            req_id = str(payload.get("id") or "").strip()
            if not req_id:
                with self._lock:
                    self._stderr_tail.append(f"[patched-host-stdout-id-missing] {line}")
                    self._cv.notify_all()
                continue
            with self._lock:
                self._responses[req_id] = payload
                self._cv.notify_all()
        with self._lock:
            self._cv.notify_all()

    def _stderr_reader(self) -> None:
        proc = self.proc
        if proc is None or proc.stderr is None:
            return
        for raw in proc.stderr:
            line = raw.rstrip("\n")
            if not line:
                continue
            with self._lock:
                self._stderr_tail.append(line)
                self._cv.notify_all()
        with self._lock:
            self._cv.notify_all()

    def _stderr_snapshot(self, max_lines: int = 20) -> str:
        with self._lock:
            lines = list(self._stderr_tail)[-max_lines:]
        return "\n".join(lines).strip()

    def execute(self, args: list[str], timeout_seconds: int) -> dict[str, Any]:
        timeout = max(1, int(timeout_seconds))
        with self._lock:
            if self._closed:
                raise RuntimeError("patched CLI client is closed")
            if self.proc is None:
                raise RuntimeError("patched CLI host is not started")
            if self.proc.poll() is not None:
                detail = self._stderr_snapshot()
                raise RuntimeError(f"patched CLI host is not running (rc={self.proc.returncode}). {detail}".strip())
            if self.proc.stdin is None:
                raise RuntimeError("patched CLI host stdin is unavailable")

            self._seq += 1
            generation = int(self._generation)
            req_id = f"{self.slot_index}-{self._seq}"
            payload = json.dumps({"id": req_id, "args": list(args)}, ensure_ascii=False)
            self.proc.stdin.write(payload + "\n")
            self.proc.stdin.flush()

            deadline = time.time() + timeout
            while req_id not in self._responses:
                remaining = deadline - time.time()
                if remaining <= 0:
                    raise TimeoutError(f"patched CLI command timeout after {timeout}s")
                if self._closed:
                    raise RuntimeError("patched CLI client closed during command execution")
                if generation != self._generation:
                    raise RuntimeError("patched CLI host restarted during command execution")
                if self.proc.poll() is not None:
                    detail = self._stderr_snapshot()
                    raise RuntimeError(
                        f"patched CLI host exited during command (rc={self.proc.returncode}). {detail}".strip()
                    )
                self._cv.wait(timeout=min(remaining, 0.5))
            return dict(self._responses.pop(req_id))

    def reset_process(self) -> None:
        self.close()
        self._start_process()

    def close(self) -> None:
        with self._lock:
            self._closed = True
            proc = self.proc
            self.proc = None
            self._responses.clear()
            self._cv.notify_all()
        if not proc:
            return
        try:
            if proc.poll() is None:
                try:
                    proc.terminate()
                    proc.wait(timeout=3)
                except Exception:
                    try:
                        proc.kill()
                        proc.wait(timeout=1)
                    except Exception:
                        pass
        except Exception:
            pass
        for stream in (proc.stdin, proc.stdout, proc.stderr):
            try:
                if stream is not None:
                    stream.close()
            except Exception:
                pass
        if self._on_proc_end:
            try:
                self._on_proc_end(int(proc.pid or 0))
            except Exception:
                pass


def _create_patched_cli_client(run: "_RunState", worker_index: int) -> _PatchedCliClient:
    if not Path(CLI_PATCHED_HOST_PATH).exists():
        raise RuntimeError(f"patched CLI host not found at {CLI_PATCHED_HOST_PATH}")
    try:
        client = _PatchedCliClient(
            worker_index,
            on_proc_start=lambda proc: _track_run_proc_start(run, proc),
            on_proc_end=lambda pid: _track_run_proc_end(run, pid),
        )
    except Exception as exc:
        raise RuntimeError(f"failed to start patched CLI host for slot {worker_index}: {exc}") from exc

    _log_event(
        "INFO",
        f"patched CLI host started for slot {worker_index} (pid={client.pid})",
        kind="run",
        extra={"run_id": run.run_id, "worker_slot": worker_index, "phase": "patched_cli_started", "pid": client.pid},
    )
    return client


def run_backtest_with_patched_cli(
    cli_client: _PatchedCliClient,
    algo_path: Path,
    cbotset_path: Path,
    start: str,
    end: str,
    data_mode: Literal["ticks", "m1"],
    ctid: str,
    pwd_file: Path,
    account: str,
    symbol: str,
    period: str,
    report_html: Path,
    report_json: Path,
    log_path: Path,
    timeout_seconds: int,
    balance: float | None,
    stop_requested: Optional[Callable[[], bool]] = None,
) -> bool:
    args = [
        "backtest",
        str(algo_path),
        str(cbotset_path),
        f"--start={start}",
        f"--end={end}",
        f"--data-mode={data_mode}",
        f"--ctid={ctid}",
        f"--pwd-file={str(pwd_file)}",
        f"--account={account}",
        f"--symbol={symbol}",
        f"--period={period}",
        f"--report={str(report_html)}",
        f"--report-json={str(report_json)}",
    ]
    if balance is not None:
        args.append(f"--balance={balance}")

    cmd_display = [f"{CLI_PATCHED_DOTNET_PATH} {CLI_PATCHED_HOST_PATH}", *args]
    stop_flag = threading.Event()
    done = threading.Event()
    result_box: dict[str, Any] = {}
    error_box: dict[str, Exception] = {}

    def reports_ready() -> bool:
        return (
            report_html.exists()
            and report_json.exists()
            and report_html.stat().st_size > 0
            and report_json.stat().st_size > 0
        )

    def _invoke() -> None:
        try:
            result_box["response"] = cli_client.execute(args, timeout_seconds=max(5, int(timeout_seconds)) + 30)
        except Exception as exc:
            error_box["error"] = exc
        finally:
            done.set()

    worker_thread = threading.Thread(target=_invoke, daemon=True)
    start_ts = time.time()
    outcome = "unknown"
    success = False

    with log_path.open("w", encoding="utf-8") as logf:
        logf.write(f"[started_at_utc] {now_utc_iso()}\n")
        logf.write(f"[command] {' '.join(shlex.quote(x) for x in cmd_display)}\n")
        logf.write(f"[execution] patched_cli_host pid={cli_client.pid}\n\n")
        logf.flush()
        worker_thread.start()
        while not done.wait(timeout=0.5):
            if stop_requested and stop_requested():
                stop_flag.set()
                outcome = "stopped_by_request"
                try:
                    cli_client.reset_process()
                except Exception:
                    pass
                break
            if timeout_seconds and (time.time() - start_ts) >= timeout_seconds:
                stop_flag.set()
                outcome = "timeout"
                try:
                    cli_client.reset_process()
                except Exception:
                    pass
                break

        if done.is_set() and not stop_flag.is_set():
            err = error_box.get("error")
            if err is not None:
                outcome = f"patched_host_error_{type(err).__name__}"
                logf.write(f"[patched_host_error] {err}\n")
            else:
                response = result_box.get("response") or {}
                raw_exit_code = response.get("exit_code")
                if raw_exit_code is None:
                    raw_exit_code = response.get("exitCode")
                if raw_exit_code is None:
                    raw_exit_code = 1
                exit_code = int(raw_exit_code)
                stdout = str(response.get("stdout") or "")
                stderr = str(response.get("stderr") or "")
                if stdout:
                    logf.write("\n[patched_host_stdout]\n")
                    logf.write(stdout)
                    if not stdout.endswith("\n"):
                        logf.write("\n")
                if stderr:
                    logf.write("\n[patched_host_stderr]\n")
                    logf.write(stderr)
                    if not stderr.endswith("\n"):
                        logf.write("\n")
                if exit_code == 0 and reports_ready():
                    success = True
                    outcome = "reports_ready"
                else:
                    outcome = f"process_exited_rc_{exit_code}"

        worker_thread.join(timeout=2.0)
        elapsed_total = round(max(0.0, time.time() - start_ts), 3)
        logf.write(f"\n[finished_at_utc] {now_utc_iso()}\n")
        logf.write(f"[elapsed_seconds_total] {elapsed_total}\n")
        logf.write(f"[outcome] {outcome}\n")
        logf.flush()

    return success or reports_ready()


def _resolve_ctrade_cmd_prefix() -> list[str]:
    raw = str(CTRADE_BIN or "").strip() or "ctrader-cli"
    try:
        parts = shlex.split(raw)
    except Exception:
        parts = [raw]
    if not parts:
        parts = ["ctrader-cli"]

    exe = str(parts[0]).strip()
    if exe:
        if Path(exe).expanduser().exists():
            return parts
        if shutil.which(exe):
            return parts

    dll_candidates = [
        Path("/app/ctrader-cli.dll"),
        Path("/opt/worker/ctrader-cli.dll"),
        Path("/opt/workerC/ctrader-cli.dll"),
    ]
    dotnet = shutil.which("dotnet")
    for dll in dll_candidates:
        if dotnet and dll.exists():
            return [dotnet, str(dll)]

    return parts


class WorkerStatus(BaseModel):
    ok: bool = True
    busy: bool
    queued: int
    running: int
    max_parallel: int
    cpu_cores: int
    current_run_id: Optional[str] = None
    started_at_utc: str


class RunStartRequest(BaseModel):
    # Identifiers (optional but recommended)
    bot_name: Optional[str] = None
    bot_version: Optional[str] = None

    # Required run info
    symbol: str
    period: str
    start: str
    end: str
    data_mode: Literal["ticks", "m1"]
    ctid: str
    account: str
    balance: Optional[float] = None

    # Credentials / files
    pwd_b64: Optional[str] = None
    pwd_text: Optional[str] = None
    algo_b64: Optional[str] = None

    # Callback (OptimoUI collector)
    callback_url: Optional[str] = None

    # Execution policy
    timeout_seconds: int = 28800
    include_artifacts: bool = True


class RunStartResponse(BaseModel):
    run_id: str
    max_parallel: int
    workdir: str


class PassJob(BaseModel):
    pass_id: int
    parameters: dict[str, Any] = Field(default_factory=dict)


class AssignPassesRequest(BaseModel):
    passes: list[PassJob]


class AssignPassesResponse(BaseModel):
    run_id: str
    accepted: int
    queued: int


class PassResult(BaseModel):
    run_id: str
    pass_id: int
    status: Literal["Completed", "Failed", "Skipped"]
    started_at_utc: str
    finished_at_utc: str
    elapsed_seconds_total: Optional[float] = None
    metrics: dict[str, Any] = Field(default_factory=dict)
    artifacts_zip_b64: Optional[str] = None
    error: Optional[str] = None


class RunResultsResponse(BaseModel):
    run_id: str
    completed: int
    total_enqueued: int
    results: list[PassResult]


@dataclass
class _RunState:
    run_id: str
    workdir: Path
    started_at_utc: str
    config: RunStartRequest
    algo_path: Path
    pwd_path: Path
    queue: asyncio.Queue[PassJob]
    stop: asyncio.Event
    in_flight: int = 0
    enqueued_total: int = 0
    results: list[PassResult] = None  # type: ignore[assignment]
    active_procs: dict[int, subprocess.Popen] = None  # type: ignore[assignment]
    callback_queue: asyncio.Queue[PassResult] | None = None
    callback_task: asyncio.Task[Any] | None = None

    def __post_init__(self):
        if self.results is None:
            self.results = []
        if self.active_procs is None:
            self.active_procs = {}
        if self.callback_queue is None and self.config.callback_url and CALLBACK_BATCH_SIZE > 1:
            self.callback_queue = asyncio.Queue()


APP_STARTED_AT = now_utc_iso()
STATE_LOCK = threading.Lock()
CURRENT_RUN: _RunState | None = None
LOG_LOCK = threading.Lock()
LOG_SEQ = 0
LOG_BUFFER: deque[dict[str, Any]] = deque(maxlen=max(500, _int_env("OPTIMO_WORKER_LOG_MAX_LINES", 2000)))

app = FastAPI(title="Bravo OPTIMO Worker", version="0.1.0")


def _log_event(
    level: str,
    message: str,
    *,
    kind: str = "app",
    extra: Optional[dict[str, Any]] = None,
) -> dict[str, Any]:
    global LOG_SEQ
    entry = {
        "id": 0,
        "ts": now_utc_iso(),
        "level": str(level or "INFO").upper(),
        "kind": str(kind or "app"),
        "message": str(message or ""),
    }
    if isinstance(extra, dict) and extra:
        entry["extra"] = extra
    with LOG_LOCK:
        LOG_SEQ += 1
        entry["id"] = LOG_SEQ
        LOG_BUFFER.append(entry)
    return entry


def _guess_bind_info() -> tuple[str, int]:
    host = str(os.environ.get("OPTIMO_WORKER_HOST") or os.environ.get("HOST") or "0.0.0.0").strip() or "0.0.0.0"
    raw_port = str(
        os.environ.get("OPTIMO_WORKER_PORT")
        or os.environ.get("PORT")
        or os.environ.get("UVICORN_PORT")
        or "1112"
    ).strip()
    try:
        port = int(raw_port)
    except Exception:
        port = 1112
    return host, port


@app.middleware("http")
async def _request_access_log(request: Request, call_next):
    started = time.perf_counter()
    try:
        response = await call_next(request)
    except Exception as exc:
        elapsed_ms = round((time.perf_counter() - started) * 1000.0, 1)
        if request.url.path != "/logs/live":
            _log_event(
                "ERROR",
                f"{request.method} {request.url.path} -> 500 ({elapsed_ms}ms) error={exc}",
                kind="access",
                extra={"method": request.method, "path": request.url.path, "elapsed_ms": elapsed_ms},
            )
        raise
    elapsed_ms = round((time.perf_counter() - started) * 1000.0, 1)
    if request.url.path != "/logs/live":
        _log_event(
            "INFO",
            f"{request.method} {request.url.path} -> {response.status_code} ({elapsed_ms}ms)",
            kind="access",
            extra={
                "method": request.method,
                "path": request.url.path,
                "status": response.status_code,
                "elapsed_ms": elapsed_ms,
            },
        )
    return response


@app.on_event("startup")
async def _announce_online_startup() -> None:
    host, port = _guess_bind_info()
    cli_mode = "custom_patched" if CUSTOM_CLI_PATCHED else "default_subprocess"
    _log_event(
        "INFO",
        f"Worker server running at {host}:{port} (max_parallel={MAX_PARALLEL}, cli_mode={cli_mode})",
        kind="startup",
        extra={
            "host": host,
            "port": port,
            "max_parallel": MAX_PARALLEL,
            "cli_mode": cli_mode,
            "custom_cli_patched": CUSTOM_CLI_PATCHED,
            "cli_patched_host_path": CLI_PATCHED_HOST_PATH,
            "cli_patched_cli_dir": CLI_PATCHED_CLI_DIR,
            "callback_batch_size": CALLBACK_BATCH_SIZE,
            "callback_batch_flush_seconds": CALLBACK_BATCH_FLUSH_SECONDS,
            "callback_post_timeout_seconds": CALLBACK_POST_TIMEOUT_SECONDS,
        },
    )

    token = (os.environ.get("TELEGRAM_BOT_TOKEN") or "").strip()
    if not token:
        return

    notify = (os.environ.get("OPTIMO_WORKER_TELEGRAM_NOTIFY") or "1").strip().lower()
    if notify in ("0", "false", "no", "off"):
        return

    chat_id = (os.environ.get("CHAT_ID") or os.environ.get("CHAT_DANIEL") or "").strip()
    if not chat_id:
        return

    await asyncio.sleep(0.5)

    public_url = (os.environ.get("OPTIMO_WORKER_PUBLIC_URL") or "").strip()
    if not public_url:
        ip = (os.environ.get("OPTIMO_WORKER_PUBLIC_IP") or "").strip() or _detect_public_ip()
        port = (
            (os.environ.get("OPTIMO_WORKER_PUBLIC_PORT") or "").strip()
            or (os.environ.get("OPTIMO_WORKER_PORT") or "").strip()
            or "1112"
        )
        if ip:
            public_url = f"http://{ip}:{port}"

    msg = "\n".join(
        [
            "Sono online!",
            public_url or "(public url unknown)",
            f"max_parallel={MAX_PARALLEL} cpu_cores={_auto_parallel_default()}",
        ]
    )
    for attempt in range(1, 6):
        try:
            await asyncio.to_thread(_telegram_send_message, token, chat_id, msg, 10)
            print("[telegram] online notify sent")
            return
        except Exception as exc:
            print(f"[telegram] notify failed (attempt {attempt}/5): {exc}")
            await asyncio.sleep(min(5.0, attempt))


def _get_run_or_404(run_id: str) -> _RunState:
    with STATE_LOCK:
        run = CURRENT_RUN
    if not run or run.run_id != run_id:
        raise HTTPException(status_code=404, detail="Run not found")
    return run


def _is_busy(run: _RunState | None) -> tuple[bool, int, int]:
    if not run:
        return False, 0, 0
    queued = run.queue.qsize()
    running = run.in_flight
    return (queued > 0 or running > 0), queued, running


def _track_run_proc_start(run: _RunState, proc: subprocess.Popen) -> None:
    pid = int(proc.pid or 0)
    if pid <= 0:
        return
    with STATE_LOCK:
        run.active_procs[pid] = proc


def _track_run_proc_end(run: _RunState, pid: int) -> None:
    with STATE_LOCK:
        run.active_procs.pop(int(pid), None)


def _drain_run_queue(run: _RunState) -> int:
    dropped = 0
    while True:
        try:
            _ = run.queue.get_nowait()
        except asyncio.QueueEmpty:
            break
        try:
            run.queue.task_done()
        except Exception:
            pass
        dropped += 1
    return dropped


def _terminate_active_processes(run: _RunState) -> int:
    with STATE_LOCK:
        procs = list(run.active_procs.values())
    killed = 0
    for proc in procs:
        try:
            if proc.poll() is not None:
                continue
            try:
                proc.terminate()
                proc.wait(timeout=3)
            except Exception:
                try:
                    proc.kill()
                    proc.wait(timeout=1)
                except Exception:
                    pass
            if proc.poll() is not None:
                killed += 1
        except Exception:
            continue
    return killed


def _release_run_if_idle(run: _RunState) -> bool:
    global CURRENT_RUN
    with STATE_LOCK:
        queued = run.queue.qsize()
        running = run.in_flight
        if CURRENT_RUN is run and run.stop.is_set() and queued <= 0 and running <= 0:
            CURRENT_RUN = None
            return True
    return False


def _stop_and_unlock_run(run: _RunState, reason: str) -> dict[str, Any]:
    run.stop.set()
    dropped = _drain_run_queue(run)
    killed = _terminate_active_processes(run)
    released = _release_run_if_idle(run)
    _log_event(
        "WARNING",
        (
            f"run {run.run_id} stop/unlock reason={reason} "
            f"dropped={dropped} killed={killed} released={released}"
        ),
        kind="run",
        extra={
            "run_id": run.run_id,
            "phase": "unlock",
            "reason": reason,
            "dropped": dropped,
            "killed": killed,
            "released": released,
        },
    )
    return {"dropped_queued": dropped, "killed_processes": killed, "released": released}


async def _process_loop(run: _RunState, worker_index: int) -> None:
    cli_client: _PatchedCliClient | None = None
    if CUSTOM_CLI_PATCHED:
        try:
            cli_client = _create_patched_cli_client(run, worker_index)
        except Exception as exc:
            run.stop.set()
            dropped = _drain_run_queue(run)
            _log_event(
                "ERROR",
                f"patched cli init failed for slot {worker_index}: {exc} (dropped={dropped})",
                kind="run",
                extra={
                    "run_id": run.run_id,
                    "worker_slot": worker_index,
                    "phase": "patched_cli_init_error",
                    "dropped": dropped,
                },
            )
            _release_run_if_idle(run)
            return

    try:
        while not run.stop.is_set():
            try:
                job = await asyncio.wait_for(run.queue.get(), timeout=0.5)
            except asyncio.TimeoutError:
                continue
            if run.stop.is_set():
                run.queue.task_done()
                break

            with STATE_LOCK:
                run.in_flight += 1

            started_at = now_utc_iso()
            started_perf = time.perf_counter()
            _log_event(
                "INFO",
                f"pass {job.pass_id} started (run_id={run.run_id}, worker_slot={worker_index})",
                kind="run",
                extra={"run_id": run.run_id, "pass_id": job.pass_id, "worker_slot": worker_index, "phase": "started"},
            )
            try:
                result = await asyncio.to_thread(_execute_pass_job, run, job, worker_index, cli_client)
            except Exception as exc:
                result = PassResult(
                    run_id=run.run_id,
                    pass_id=job.pass_id,
                    status="Failed",
                    started_at_utc=started_at,
                    finished_at_utc=now_utc_iso(),
                    elapsed_seconds_total=None,
                    metrics={},
                    artifacts_zip_b64=None,
                    error=str(exc),
                )
            elapsed_total = round(max(0.0, time.perf_counter() - started_perf), 3)
            result.started_at_utc = started_at
            result.finished_at_utc = now_utc_iso()
            result.elapsed_seconds_total = elapsed_total

            with STATE_LOCK:
                run.results.append(result)
                run.in_flight -= 1

            _log_event(
                "INFO" if result.status == "Completed" else "ERROR",
                (
                    f"pass {job.pass_id} finished status={result.status} "
                    f"elapsed_total_seconds={elapsed_total} (run_id={run.run_id})"
                ),
                kind="run",
                extra={
                    "run_id": run.run_id,
                    "pass_id": job.pass_id,
                    "status": result.status,
                    "phase": "finished",
                    "elapsed_seconds_total": elapsed_total,
                },
            )

            run.queue.task_done()
            if run.stop.is_set():
                _release_run_if_idle(run)

            if run.callback_queue is not None:
                await run.callback_queue.put(result)
            elif run.config.callback_url:
                asyncio.create_task(_notify_callback(run, result))
    finally:
        if cli_client:
            cli_client.close()
        _release_run_if_idle(run)


async def _notify_callback(run: _RunState, result: PassResult) -> None:
    payload = result.model_dump()
    ok, err = await asyncio.to_thread(post_json, run.config.callback_url or "", payload, CALLBACK_POST_TIMEOUT_SECONDS)
    if not ok:
        # Keep run throughput high: callback is best-effort and never blocks worker slots.
        _log_event(
            "ERROR",
            f"callback failed for pass {result.pass_id}: {err}",
            kind="run",
            extra={"run_id": run.run_id, "pass_id": result.pass_id, "phase": "callback", "error": err},
        )


def _build_callback_batch_payload(run: _RunState, items: list[PassResult]) -> dict[str, Any]:
    payload_items = [item.model_dump(exclude={"artifacts_zip_b64"}) for item in items]
    payload: dict[str, Any] = {"run_id": run.run_id, "items": payload_items}
    if not run.config.include_artifacts:
        return payload

    pass_ids = [int(item.pass_id) for item in items if int(item.pass_id or 0) > 0]
    artifacts_batch_zip_b64 = zip_pass_dirs_to_b64(run.workdir, pass_ids)
    if artifacts_batch_zip_b64:
        payload["artifacts_batch_zip_b64"] = artifacts_batch_zip_b64
    return payload


async def _notify_callback_batch(run: _RunState, items: list[PassResult]) -> None:
    if not items:
        return
    payload = await asyncio.to_thread(_build_callback_batch_payload, run, items)
    ok, err = await asyncio.to_thread(post_json, run.config.callback_url or "", payload, CALLBACK_POST_TIMEOUT_SECONDS)
    if ok:
        return
    ids = [str(item.pass_id) for item in items]
    shown = ",".join(ids[:5])
    if len(ids) > 5:
        shown += ",..."
    _log_event(
        "ERROR",
        f"callback batch failed for {len(items)} pass(es) [{shown}]: {err}",
        kind="run",
        extra={"run_id": run.run_id, "phase": "callback_batch", "error": err, "batch_size": len(items)},
    )


async def _callback_loop(run: _RunState) -> None:
    q = run.callback_queue
    if q is None or not run.config.callback_url:
        return

    pending: list[PassResult] = []
    while True:
        if run.stop.is_set() and q.empty() and not pending:
            break
        timeout = CALLBACK_BATCH_FLUSH_SECONDS if pending else 0.5
        try:
            item = await asyncio.wait_for(q.get(), timeout=timeout)
        except asyncio.TimeoutError:
            if pending:
                await _notify_callback_batch(run, pending)
                pending = []
            continue
        pending.append(item)
        try:
            q.task_done()
        except Exception:
            pass
        if len(pending) >= CALLBACK_BATCH_SIZE:
            await _notify_callback_batch(run, pending)
            pending = []

    if pending:
        await _notify_callback_batch(run, pending)


def _execute_pass_job(
    run: _RunState,
    job: PassJob,
    worker_index: int,
    cli_client: _PatchedCliClient | None = None,
) -> PassResult:
    pass_dir = run.workdir / str(job.pass_id)
    ensure_dir(pass_dir)

    report_html = pass_dir / "report.html"
    report_json = pass_dir / "report.json"
    log_path = pass_dir / "log.txt"
    events_path = pass_dir / "events.json"
    cbotset_path = pass_dir / "parameters.cbotset"

    write_events(events_path)
    write_cbotset(cbotset_path, job.parameters, run.config.symbol, run.config.period)
    if cli_client is not None:
        ok = run_backtest_with_patched_cli(
            cli_client=cli_client,
            algo_path=run.algo_path,
            cbotset_path=cbotset_path,
            start=run.config.start,
            end=run.config.end,
            data_mode=run.config.data_mode,
            ctid=run.config.ctid,
            pwd_file=run.pwd_path,
            account=run.config.account,
            symbol=run.config.symbol,
            period=run.config.period,
            report_html=report_html,
            report_json=report_json,
            log_path=log_path,
            timeout_seconds=int(run.config.timeout_seconds),
            balance=run.config.balance,
            stop_requested=run.stop.is_set,
        )
    else:
        ok = run_backtest(
            algo_path=run.algo_path,
            cbotset_path=cbotset_path,
            start=run.config.start,
            end=run.config.end,
            data_mode=run.config.data_mode,
            ctid=run.config.ctid,
            pwd_file=run.pwd_path,
            account=run.config.account,
            symbol=run.config.symbol,
            period=run.config.period,
            report_html=report_html,
            report_json=report_json,
            log_path=log_path,
            timeout_seconds=int(run.config.timeout_seconds),
            balance=run.config.balance,
            stop_requested=run.stop.is_set,
            on_proc_start=lambda proc: _track_run_proc_start(run, proc),
            on_proc_end=lambda pid: _track_run_proc_end(run, pid),
        )
    rep = parse_report(report_json) if ok else None
    metrics = rep or {}

    artifacts_zip_b64 = None
    callback_batch_enabled = bool(run.config.callback_url) and CALLBACK_BATCH_SIZE > 1
    if run.config.include_artifacts and not callback_batch_enabled:
        artifacts_zip_b64 = zip_dir_to_b64(pass_dir)

    return PassResult(
        run_id=run.run_id,
        pass_id=job.pass_id,
        status="Completed" if rep else "Failed",
        started_at_utc=now_utc_iso(),
        finished_at_utc=now_utc_iso(),
        metrics=metrics,
        artifacts_zip_b64=artifacts_zip_b64,
        error=None if rep else "report_missing_or_invalid",
    )


@app.get("/status", response_model=WorkerStatus)
def status():
    with STATE_LOCK:
        run = CURRENT_RUN
    busy, queued, running = _is_busy(run)
    return WorkerStatus(
        busy=busy,
        queued=queued,
        running=running,
        max_parallel=MAX_PARALLEL,
        cpu_cores=_auto_parallel_default(),
        current_run_id=run.run_id if run else None,
        started_at_utc=APP_STARTED_AT,
    )


@app.get("/logs/live")
def logs_live(since_id: int = 0, limit: int = 200):
    since = max(0, int(since_id or 0))
    lim = max(1, min(int(limit or 200), 2000))
    with LOG_LOCK:
        snapshot = list(LOG_BUFFER)
        latest_id = LOG_SEQ
    if not snapshot:
        return {"items": [], "next_since_id": since, "dropped": False, "latest_id": latest_id}

    oldest_id = int(snapshot[0].get("id") or 0)
    dropped = since > 0 and oldest_id > (since + 1)
    items = [entry for entry in snapshot if int(entry.get("id") or 0) > since]
    if len(items) > lim:
        items = items[-lim:]
        dropped = True
    next_since_id = int(items[-1].get("id") or since) if items else since
    return {
        "items": items,
        "next_since_id": next_since_id,
        "dropped": dropped,
        "latest_id": latest_id,
    }


@app.post("/run/start", response_model=RunStartResponse)
async def run_start(payload: RunStartRequest):
    global CURRENT_RUN
    with STATE_LOCK:
        run = CURRENT_RUN
    busy, _, _ = _is_busy(run)
    if busy:
        raise HTTPException(status_code=409, detail="Worker is busy")

    if not payload.pwd_b64 and not payload.pwd_text:
        raise HTTPException(status_code=400, detail="pwd_b64 or pwd_text is required")
    if not payload.algo_b64:
        raise HTTPException(status_code=400, detail="algo_b64 is required")

    run_id = f"run_{datetime.utcnow().strftime('%Y%m%d_%H%M%S')}_{uuid.uuid4().hex[:8]}"
    workdir = (WORKER_ROOT / run_id).resolve()
    if workdir.exists():
        shutil.rmtree(workdir, ignore_errors=True)
    ensure_dir(workdir)

    # store secrets and algo locally for this run
    pwd_path = workdir / "pwd.txt"
    if payload.pwd_b64:
        pwd_bytes = base64.b64decode(payload.pwd_b64.encode("ascii"))
        pwd_path.write_bytes(pwd_bytes)
    else:
        pwd_path.write_text(payload.pwd_text or "", encoding="utf-8")
    try:
        os.chmod(pwd_path, 0o600)
    except Exception:
        pass

    algo_path = workdir / "algo.algo"
    algo_bytes = base64.b64decode(payload.algo_b64.encode("ascii"))
    algo_path.write_bytes(algo_bytes)

    queue: asyncio.Queue[PassJob] = asyncio.Queue()
    stop = asyncio.Event()
    run_state = _RunState(
        run_id=run_id,
        workdir=workdir,
        started_at_utc=now_utc_iso(),
        config=payload,
        algo_path=algo_path,
        pwd_path=pwd_path,
        queue=queue,
        stop=stop,
    )

    # persist run metadata
    (workdir / "run.json").write_text(
        json.dumps(payload.model_dump(), indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    with STATE_LOCK:
        CURRENT_RUN = run_state

    if run_state.callback_queue is not None:
        run_state.callback_task = asyncio.create_task(_callback_loop(run_state))

    # spin up processors
    for i in range(MAX_PARALLEL):
        asyncio.create_task(_process_loop(run_state, i))

    _log_event(
        "INFO",
        (
            f"run started id={run_id} bot={payload.bot_name or '-'} ver={payload.bot_version or '-'} "
            f"symbol={payload.symbol} period={payload.period}"
        ),
        kind="run",
        extra={
            "run_id": run_id,
            "phase": "run_start",
            "symbol": payload.symbol,
            "period": payload.period,
            "callback_enabled": bool(payload.callback_url),
            "callback_batch_enabled": bool(run_state.callback_queue is not None),
            "callback_batch_size": CALLBACK_BATCH_SIZE,
        },
    )

    return RunStartResponse(run_id=run_id, max_parallel=MAX_PARALLEL, workdir=str(workdir))


@app.post("/run/{run_id}/assign", response_model=AssignPassesResponse)
async def run_assign(run_id: str, payload: AssignPassesRequest):
    run = _get_run_or_404(run_id)
    if run.stop.is_set():
        raise HTTPException(status_code=409, detail="Run is stopping/stopped")

    accepted = 0
    for p in payload.passes:
        await run.queue.put(p)
        accepted += 1

    with STATE_LOCK:
        run.enqueued_total += accepted
        queued = run.queue.qsize()

    _log_event(
        "INFO",
        f"run {run_id} assigned {accepted} pass(es), queued={queued}",
        kind="run",
        extra={"run_id": run_id, "phase": "assign", "accepted": accepted, "queued": queued},
    )

    return AssignPassesResponse(run_id=run_id, accepted=accepted, queued=queued)


@app.get("/run/{run_id}/results", response_model=RunResultsResponse)
def run_results(run_id: str, limit: int = 2000, include_artifacts: int = 1):
    run = _get_run_or_404(run_id)
    with_artifacts = bool(int(include_artifacts or 0))
    with STATE_LOCK:
        snapshot = list(run.results)[-limit:]
        completed = len(run.results)
        total = run.enqueued_total
    if with_artifacts:
        results: list[PassResult] = []
        for r in snapshot:
            artifacts = r.artifacts_zip_b64
            if artifacts is None and run.config.include_artifacts:
                pass_dir = run.workdir / str(r.pass_id)
                if pass_dir.exists():
                    try:
                        artifacts = zip_dir_to_b64(pass_dir)
                    except Exception:
                        artifacts = None
            results.append(r.model_copy(update={"artifacts_zip_b64": artifacts}))
    else:
        results = [r.model_copy(update={"artifacts_zip_b64": None}) for r in snapshot]
    return RunResultsResponse(run_id=run_id, completed=completed, total_enqueued=total, results=results)


@app.post("/run/{run_id}/stop")
async def run_stop(run_id: str):
    run = _get_run_or_404(run_id)
    summary = _stop_and_unlock_run(run, reason="run_stop")
    return {"ok": True, "run_id": run_id, **summary}


@app.post("/run/{run_id}/unlock")
async def run_unlock(run_id: str):
    run = _get_run_or_404(run_id)
    summary = _stop_and_unlock_run(run, reason="run_unlock")
    return {"ok": True, "run_id": run_id, **summary}


@app.post("/unlock")
async def unlock_current_run():
    with STATE_LOCK:
        run = CURRENT_RUN
    if not run:
        return {"ok": True, "released": True, "message": "no_active_run"}
    summary = _stop_and_unlock_run(run, reason="unlock_current")
    return {"ok": True, "run_id": run.run_id, **summary}
