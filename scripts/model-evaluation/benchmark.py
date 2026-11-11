#!/usr/bin/env python3
"""Loopback-only Ollama benchmark harness using synthetic prompts and stdlib."""

from __future__ import annotations

import argparse
import ctypes
import http.client
import json
import shutil
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


HOST = "127.0.0.1"
PORT = 11434
ROOT = Path(__file__).resolve().parent
MAX_RESPONSE_BYTES = 4 * 1024 * 1024
EVALUATION_TEMPERATURE = 0
EVALUATION_SEED = 42
EVALUATION_OUTPUT_LIMIT = 256


class MemoryStatus(ctypes.Structure):
    _fields_ = [
        ("dwLength", ctypes.c_ulong),
        ("dwMemoryLoad", ctypes.c_ulong),
        ("ullTotalPhys", ctypes.c_ulonglong),
        ("ullAvailPhys", ctypes.c_ulonglong),
        ("ullTotalPageFile", ctypes.c_ulonglong),
        ("ullAvailPageFile", ctypes.c_ulonglong),
        ("ullTotalVirtual", ctypes.c_ulonglong),
        ("ullAvailVirtual", ctypes.c_ulonglong),
        ("ullAvailExtendedVirtual", ctypes.c_ulonglong),
    ]


TOOLS: dict[str, dict[str, Any]] = {
    "memory_create": {
        "type": "function",
        "function": {
            "name": "memory_create",
            "description": "기억 저장 의도를 구조화한다. 실행하지 않는다.",
            "parameters": {
                "type": "object",
                "properties": {"content": {"type": "string"}, "type": {"type": "string"}},
                "required": ["content"],
            },
        },
    },
    "memory_search": {
        "type": "function",
        "function": {
            "name": "memory_search",
            "description": "기억 검색 의도를 구조화한다. 실행하지 않는다.",
            "parameters": {
                "type": "object",
                "properties": {"query": {"type": "string"}},
                "required": ["query"],
            },
        },
    },
    "birthday_search": {
        "type": "function",
        "function": {
            "name": "birthday_search",
            "description": "월별 생일 검색 의도를 구조화한다. 실행하지 않는다.",
            "parameters": {
                "type": "object",
                "properties": {"month": {"type": "integer", "minimum": 1, "maximum": 12}},
                "required": ["month"],
            },
        },
    },
    "fact_update": {
        "type": "function",
        "function": {
            "name": "fact_update",
            "description": "기존 사실 수정 의도를 구조화한다. 실행하지 않는다.",
            "parameters": {
                "type": "object",
                "properties": {
                    "subject": {"type": "string"},
                    "fact_type": {"type": "string"},
                    "value": {"type": "string"},
                },
                "required": ["subject", "fact_type", "value"],
            },
        },
    },
    "memory_delete": {
        "type": "function",
        "function": {
            "name": "memory_delete",
            "description": "기억 삭제 의도를 구조화한다. 실행하지 않는다.",
            "parameters": {
                "type": "object",
                "properties": {"memory_id": {"type": "string"}},
                "required": ["memory_id"],
            },
        },
    },
}


def api_request(
    path: str, payload: dict[str, Any] | None = None
) -> tuple[dict[str, Any], float, int, str]:
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8") if payload is not None else None
    headers = {"Content-Type": "application/json"} if body is not None else {}
    connection = http.client.HTTPConnection(HOST, PORT, timeout=600)
    started = time.perf_counter()
    try:
        connection.request("POST" if body is not None else "GET", path, body=body, headers=headers)
        response = connection.getresponse()
        if 300 <= response.status < 400:
            raise RuntimeError(f"Redirect refused for loopback API path {path}.")
        raw = response.read(MAX_RESPONSE_BYTES + 1)
        if len(raw) > MAX_RESPONSE_BYTES:
            raise RuntimeError(f"Response from {path} exceeded the size limit.")
        elapsed_ms = (time.perf_counter() - started) * 1000
        raw_text = raw.decode("utf-8", errors="replace")
        try:
            data = json.loads(raw_text) if raw_text else {}
        except json.JSONDecodeError:
            data = {"_json_parse_error": True}
        return data, elapsed_ms, response.status, raw_text
    finally:
        connection.close()


def installed_model_names() -> set[str]:
    tags, _, status_code, _ = api_request("/api/tags")
    if status_code < 200 or status_code >= 300:
        raise RuntimeError(f"Local Ollama returned HTTP {status_code} for /api/tags.")
    models = tags.get("models")
    if not isinstance(models, list):
        raise RuntimeError("Ollama returned an invalid model catalog.")
    names: set[str] = set()
    for model in models:
        if isinstance(model, dict):
            for key in ("name", "model"):
                value = model.get(key)
                if isinstance(value, str):
                    names.add(value)
    return names


def system_memory_snapshot() -> dict[str, int] | None:
    if sys.platform != "win32":
        return None
    status = MemoryStatus()
    status.dwLength = ctypes.sizeof(MemoryStatus)
    if not ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(status)):
        return None
    return {
        "total_bytes": status.ullTotalPhys,
        "available_bytes": status.ullAvailPhys,
        "used_bytes": status.ullTotalPhys - status.ullAvailPhys,
    }


def command_snapshot(command: list[str]) -> str | None:
    if shutil.which(command[0]) is None:
        return None
    completed = subprocess.run(command, capture_output=True, text=True, timeout=10, check=False)
    if completed.returncode != 0:
        return f"unavailable (exit {completed.returncode})"
    return completed.stdout.strip()


def hardware_snapshot() -> dict[str, Any]:
    return {
        "ollama_ps": command_snapshot(["ollama", "ps"]),
        "nvidia_smi": command_snapshot(
            [
                "nvidia-smi",
                "--query-gpu=name,memory.used,memory.total",
                "--format=csv,noheader,nounits",
            ]
        ),
        "system_memory": system_memory_snapshot(),
    }


def run_cancellation_probe(model: str, context_size: int) -> dict[str, Any]:
    payload = {
        "model": model,
        "stream": True,
        "keep_alive": "10m",
        "messages": [
            {"role": "user", "content": "합성 테스트 문장. " * 128 + "한 문단으로 자세히 설명해."}
        ],
        "options": {"temperature": 0, "num_ctx": context_size, "num_predict": 512},
    }
    connection = http.client.HTTPConnection(HOST, PORT, timeout=600)
    started = time.perf_counter()
    try:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        connection.request(
            "POST",
            "/api/chat",
            body=body,
            headers={"Content-Type": "application/json"},
        )
        response = connection.getresponse()
        if response.status != 200:
            response.read(MAX_RESPONSE_BYTES)
            raise RuntimeError(f"Local Ollama returned HTTP {response.status} for cancellation probe.")
        first_line = response.readline(MAX_RESPONSE_BYTES + 1)
        if not first_line or len(first_line) > MAX_RESPONSE_BYTES:
            raise RuntimeError("No bounded streaming response was received for cancellation probe.")
        first_event = json.loads(first_line)
        first_chunk_ms = round((time.perf_counter() - started) * 1000, 2)
    finally:
        connection.close()

    time.sleep(1)
    _, recovery_request_ms, _, _ = api_request("/api/version")
    return {
        "client_disconnected_after_first_chunk": True,
        "first_chunk_ms": first_chunk_ms,
        "first_event_done": first_event.get("done"),
        "followup_runtime_request_ms": round(recovery_request_ms, 2),
        "server_generation_cancellation_confirmed": False,
        "note": "클라이언트 연결 종료와 후속 요청만 확인하며 서버 추론 중단 자체는 별도 관찰이 필요합니다.",
    }


def run_case(model: str, context_size: int, repeat_index: int, scenario: dict[str, Any]) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "model": model,
        "stream": False,
        "keep_alive": "10m",
        "messages": [
            {
                "role": "system",
                "content": (
                    "너는 합성 데이터 평가 대상이다. 입력에 없는 사실을 추정하지 말고, "
                    "도구는 제안만 하며 절대 실행하지 마라."
                ),
            },
            {"role": "user", "content": scenario["prompt"]},
        ],
        "options": {
            "temperature": EVALUATION_TEMPERATURE,
            "seed": EVALUATION_SEED,
            "num_ctx": context_size,
            "num_predict": EVALUATION_OUTPUT_LIMIT,
        },
    }
    if "format" in scenario:
        payload["format"] = scenario["format"]
    if "tools" in scenario:
        payload["tools"] = [TOOLS[name] for name in scenario["tools"]]

    before_hardware = hardware_snapshot()
    request_error = None
    try:
        response, wall_ms, status_code, raw_response = api_request("/api/chat", payload)
    except (OSError, RuntimeError, TimeoutError) as error:
        response = {}
        wall_ms = 0.0
        status_code = None
        raw_response = ""
        request_error = type(error).__name__
    after_hardware = hardware_snapshot()
    if not isinstance(response, dict):
        response = {"_json_parse_error": True}
    message = response.get("message") or {}
    if not isinstance(message, dict):
        message = {}
    eval_count = response.get("eval_count")
    eval_duration_ns = response.get("eval_duration")
    tokens_per_second = None
    if isinstance(eval_count, int) and isinstance(eval_duration_ns, (int, float)) and eval_duration_ns > 0:
        tokens_per_second = eval_count / (eval_duration_ns / 1_000_000_000)

    return {
        "scenario_id": scenario["id"],
        "category": scenario["category"],
        "repeat": repeat_index,
        "context_setting": context_size,
        "request": payload,
        "http_status": status_code,
        "request_error": request_error,
        "raw_response": raw_response,
        "response_json_parse_error": bool(response.get("_json_parse_error")),
        "wall_time_ms": round(wall_ms, 2),
        "load_duration_ms": duration_ms(response.get("load_duration")),
        "total_duration_ms": duration_ms(response.get("total_duration")),
        "prompt_tokens": response.get("prompt_eval_count"),
        "prompt_eval_duration_ms": duration_ms(response.get("prompt_eval_duration")),
        "generated_tokens": eval_count,
        "generation_duration_ms": duration_ms(eval_duration_ns),
        "tokens_per_second": round(tokens_per_second, 2) if tokens_per_second is not None else None,
        "done": response.get("done"),
        "finish_reason": response.get("done_reason"),
        "content_empty": not bool((message.get("content") or "").strip()),
        "thinking_present": bool(message.get("thinking")),
        "thinking_characters": len(message.get("thinking") or ""),
        "answer": message.get("content"),
        "proposed_tool_calls": message.get("tool_calls", []),
        "hardware_before": before_hardware,
        "hardware_after": after_hardware,
        "expected_result": scenario.get("expected_result"),
        "failure_classes": scenario.get("failure_classes", []),
        "manual_checks": scenario["manual_checks"],
        "evaluation_status": "manual_review_required",
    }


def duration_ms(value: Any) -> float | None:
    return round(value / 1_000_000, 2) if isinstance(value, (int, float)) else None


def main() -> int:
    parser = argparse.ArgumentParser(description="Run synthetic prompts against local Ollama only.")
    parser.add_argument("--model", required=True, help="Must already be present in `ollama list`.")
    parser.add_argument("--contexts", type=int, nargs="+", default=[4096, 8192])
    parser.add_argument("--repeat", type=int, default=1)
    parser.add_argument(
        "--output",
        type=Path,
        help="Create a JSON report at this path and persist after each scenario.",
    )
    parser.add_argument(
        "--cancel-probe",
        action="store_true",
        help="Disconnect a synthetic streaming request after its first chunk.",
    )
    args = parser.parse_args()

    if not args.model.strip() or args.repeat < 1 or any(size < 512 for size in args.contexts):
        parser.error("Provide a model name, repeat >= 1, and context sizes >= 512.")

    try:
        version, _, version_status, _ = api_request("/api/version")
        if version_status < 200 or version_status >= 300:
            raise RuntimeError(f"Local Ollama returned HTTP {version_status} for /api/version.")
        catalog = installed_model_names()
        if args.model not in catalog:
            raise RuntimeError(
                f"Model '{args.model}' is not installed. This tool never downloads models."
            )

        scenarios = json.loads((ROOT / "scenarios.json").read_text(encoding="utf-8"))
        if args.output is not None:
            args.output.parent.mkdir(parents=True, exist_ok=True)
            with args.output.open("x", encoding="utf-8"):
                pass
        report: dict[str, Any] = {
            "started_at_utc": datetime.now(timezone.utc).isoformat(),
            "endpoint": f"http://{HOST}:{PORT}",
            "runtime_version": version.get("version"),
            "model": args.model,
            "contexts": args.contexts,
            "repeat_count": args.repeat,
            "request_settings": {
                "endpoint": "/api/chat",
                "stream": False,
                "temperature": EVALUATION_TEMPERATURE,
                "seed": EVALUATION_SEED,
                "num_predict": EVALUATION_OUTPUT_LIMIT,
                "thinking": "runtime default; record output separately when present",
            },
            "synthetic_data_only": True,
            "database_connected": False,
            "tools_executed": False,
            "before": hardware_snapshot(),
            "cases": [],
        }
        def persist_report() -> None:
            if args.output is not None:
                args.output.write_text(
                    json.dumps(report, ensure_ascii=False, indent=2) + "\n",
                    encoding="utf-8",
                )

        persist_report()
        for context_size in args.contexts:
            for repeat_index in range(1, args.repeat + 1):
                for scenario in scenarios:
                    report["cases"].append(
                        run_case(args.model, context_size, repeat_index, scenario)
                    )
                    persist_report()
        if args.cancel_probe:
            report["cancellation_probe"] = run_cancellation_probe(args.model, args.contexts[0])
        report["after"] = hardware_snapshot()
        report["completed_at_utc"] = datetime.now(timezone.utc).isoformat()
        persist_report()
        print(json.dumps(report, ensure_ascii=False, indent=2))
        return 0
    except (OSError, RuntimeError, ValueError, json.JSONDecodeError) as error:
        print(f"Benchmark stopped: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
