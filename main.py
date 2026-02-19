from __future__ import annotations

import asyncio
import base64
import json
import os
import shutil
import subprocess
import threading
import time
import uuid
import zipfile
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Any, Literal, Optional
from urllib import request as urlrequest
from urllib import error as urlerror

from fastapi import FastAPI, HTTPException
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


MAX_PARALLEL = max(1, _int_env("OPTIMO_WORKER_PARALLEL", _auto_parallel_default()))


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
) -> bool:
    cmd = [
        CTRADE_BIN,
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

        start_ts = time.time()
        while True:
            if reports_ready():
                return True
            if proc.poll() is not None:
                break
            if timeout_seconds and (time.time() - start_ts) >= timeout_seconds:
                # leave process running by policy; consider it failed for now
                break
            time.sleep(1)

    return reports_ready()


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

    def __post_init__(self):
        if self.results is None:
            self.results = []


APP_STARTED_AT = now_utc_iso()
STATE_LOCK = threading.Lock()
CURRENT_RUN: _RunState | None = None

app = FastAPI(title="Bravo OPTIMO Worker", version="0.1.0")


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


async def _process_loop(run: _RunState, worker_index: int) -> None:
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
        try:
            result = await asyncio.to_thread(_execute_pass_job, run, job, worker_index)
            result.started_at_utc = started_at
        except Exception as exc:
            result = PassResult(
                run_id=run.run_id,
                pass_id=job.pass_id,
                status="Failed",
                started_at_utc=started_at,
                finished_at_utc=now_utc_iso(),
                metrics={},
                artifacts_zip_b64=None,
                error=str(exc),
            )

        with STATE_LOCK:
            run.results.append(result)
            run.in_flight -= 1

        run.queue.task_done()

        if run.config.callback_url:
            payload = result.model_dump()
            ok, err = await asyncio.to_thread(post_json, run.config.callback_url, payload, 10)
            if not ok:
                # keep the run going; record the callback error as best-effort
                with STATE_LOCK:
                    run.results.append(
                        PassResult(
                            run_id=run.run_id,
                            pass_id=job.pass_id,
                            status="Skipped",
                            started_at_utc=now_utc_iso(),
                            finished_at_utc=now_utc_iso(),
                            metrics={},
                            artifacts_zip_b64=None,
                            error=f"callback_failed: {err}",
                        )
                    )


def _execute_pass_job(run: _RunState, job: PassJob, worker_index: int) -> PassResult:
    pass_dir = run.workdir / str(job.pass_id)
    ensure_dir(pass_dir)

    report_html = pass_dir / "report.html"
    report_json = pass_dir / "report.json"
    log_path = pass_dir / "log.txt"
    events_path = pass_dir / "events.json"
    cbotset_path = pass_dir / "parameters.cbotset"

    write_events(events_path)
    write_cbotset(cbotset_path, job.parameters, run.config.symbol, run.config.period)

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
    )
    rep = parse_report(report_json) if ok else None
    metrics = rep or {}

    artifacts_zip_b64 = None
    if run.config.include_artifacts:
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

    # spin up processors
    for i in range(MAX_PARALLEL):
        asyncio.create_task(_process_loop(run_state, i))

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

    return AssignPassesResponse(run_id=run_id, accepted=accepted, queued=queued)


@app.get("/run/{run_id}/results", response_model=RunResultsResponse)
def run_results(run_id: str, limit: int = 2000):
    run = _get_run_or_404(run_id)
    with STATE_LOCK:
        results = list(run.results)[-limit:]
        completed = len(run.results)
        total = run.enqueued_total
    return RunResultsResponse(run_id=run_id, completed=completed, total_enqueued=total, results=results)


@app.post("/run/{run_id}/stop")
async def run_stop(run_id: str):
    run = _get_run_or_404(run_id)
    run.stop.set()
    return {"ok": True}
