from __future__ import annotations

import argparse
import json
import math
import random
import time
from pathlib import Path
from typing import Any, Dict, List, Tuple

BATCH_SIZE = 16
LEARNING_RATE = 2e-5
WEIGHT_DECAY = 0.01
MAX_LEN = 384
WARMUP_FRACTION = 0.05
GRAD_CLIP_NORM = 1.0
VAL_CHUNK = 16
BASE_CHECKPOINT = "convaiinnovations/laya"


def load_jsonl(path: Path) -> List[Dict[str, Any]]:
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def encode_row(agent: Any, row: Dict[str, Any]) -> Tuple[Any, int]:
    internal = agent._to_internal(row["q"])
    item = agent._encode_state(row["state"], ["q"], {"q": internal}, max_len=MAX_LEN)[0]
    if row["q"]["type"] == "noul":
        gold = 1 if row["gold"] else 0
    else:
        gold = list(row["q"]["criteria"].keys()).index(row["gold"])
    return item, gold


def prepare_rows(agent: Any, rows: List[Dict[str, Any]]) -> List[Tuple[Tuple[Any, int], str]]:
    prepared: List[Tuple[Tuple[Any, int], str]] = []
    for row in rows:
        try:
            prepared.append((encode_row(agent, row), row["src"]))
        except ValueError:
            continue
    return prepared


def compute_step_count(train_size: int, epochs: float, batch_size: int = BATCH_SIZE) -> int:
    return int(math.ceil(train_size / batch_size) * epochs)


def linear_warmup_decay(step: int, total_steps: int, warmup_steps: int) -> float:
    warm = min(1.0, (step + 1) / warmup_steps)
    decay = max(0.0, (total_steps - step) / max(1, total_steps - warmup_steps))
    return warm * decay


def parse_seed_list(raw: str) -> List[int]:
    return [int(part) for part in raw.split(",") if part.strip()]


def train_one_seed(
    seed: int,
    init_state_dict_path: Path,
    train_rows: List[Dict[str, Any]],
    val_rows: List[Dict[str, Any]],
    epochs: float,
    device: str,
) -> Tuple[Any, Dict[str, float]]:
    import torch
    import torch.nn.functional as functional
    import laya
    from laya.common import collate_items

    random.seed(seed)
    torch.manual_seed(seed)

    agent = laya.load(BASE_CHECKPOINT, device=device)
    model, tokenizer = agent.model, agent.tok
    state_dict = torch.load(init_state_dict_path, map_location=device)
    model.load_state_dict(state_dict)
    if hasattr(model.encoder, "gradient_checkpointing_enable"):
        model.encoder.gradient_checkpointing_enable()

    train = prepare_rows(agent, train_rows)
    val = prepare_rows(agent, val_rows)

    def forward(batch: List[Tuple[Tuple[Any, int], str]]) -> Tuple[Any, Any]:
        collated = collate_items([[item] for (item, _gold), _src in batch], tokenizer.pad_token_id)
        with torch.autocast(device if device != "cuda" else "cuda", dtype=torch.bfloat16):
            logits, _ = model(
                collated["input_ids"].to(device),
                collated["attention_mask"].to(device),
                collated["marker_pos"].to(device),
                collated["marker_mask"].to(device),
                collated["qtype"].to(device),
            )
        gold = torch.tensor([g for (_item, g), _src in batch], device=device)
        return logits.float(), gold

    @torch.no_grad()
    def evaluate() -> Dict[str, float]:
        model.eval()
        hits: Dict[str, int] = {}
        totals: Dict[str, int] = {}
        for start in range(0, len(val), VAL_CHUNK):
            chunk = val[start : start + VAL_CHUNK]
            logits, gold = forward(chunk)
            correct = (logits.argmax(-1) == gold).tolist()
            for is_correct, (_, src) in zip(correct, chunk):
                hits[src] = hits.get(src, 0) + int(is_correct)
                totals[src] = totals.get(src, 0) + 1
        model.train()
        if device == "cuda":
            torch.cuda.empty_cache()
        return {src: hits[src] / totals[src] for src in totals}

    steps = compute_step_count(len(train), epochs)
    optimizer = torch.optim.AdamW(model.parameters(), lr=LEARNING_RATE, weight_decay=WEIGHT_DECAY)
    warmup_steps = max(1, int(steps * WARMUP_FRACTION))
    scheduler = torch.optim.lr_scheduler.LambdaLR(
        optimizer, lambda step: linear_warmup_decay(step, steps, warmup_steps)
    )

    model.train()
    step = 0
    started = time.time()
    while step < steps:
        random.shuffle(train)
        for start in range(0, len(train) - BATCH_SIZE + 1, BATCH_SIZE):
            logits, gold = forward(train[start : start + BATCH_SIZE])
            loss = functional.cross_entropy(logits, gold)
            loss.backward()
            torch.nn.utils.clip_grad_norm_(model.parameters(), GRAD_CLIP_NORM)
            optimizer.step()
            scheduler.step()
            optimizer.zero_grad(set_to_none=True)
            step += 1
            if step % 200 == 0:
                print(
                    f"seed {seed} step {step}/{steps} {time.time() - started:.0f}s",
                    flush=True,
                )
            if step >= steps:
                break

    metrics = evaluate()
    return model, metrics


def cuda_is_available() -> bool:
    import torch

    return bool(torch.cuda.is_available())


def parse_args(argv: List[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(prog="python -m mmclassifier.train")
    parser.add_argument("--init", required=True, type=Path)
    parser.add_argument("--data", required=True, type=Path)
    parser.add_argument("--seeds", default="7,11,13")
    parser.add_argument("--epochs", type=float, default=3)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--device", default=None)
    return parser.parse_args(argv)


def run(args: argparse.Namespace) -> Dict[str, Dict[str, float]]:
    import torch

    device = args.device or ("cuda" if cuda_is_available() else "cpu")
    seeds = parse_seed_list(args.seeds)

    train_rows = load_jsonl(args.data / "train.jsonl")
    val_rows = load_jsonl(args.data / "val.jsonl")

    args.out.mkdir(parents=True, exist_ok=True)
    metrics_by_seed: Dict[str, Dict[str, float]] = {}
    for seed in seeds:
        model, metrics = train_one_seed(seed, args.init, train_rows, val_rows, args.epochs, device)
        out_path = args.out / f"seed{seed}.pt"
        torch.save(model.state_dict(), out_path)
        metrics_by_seed[str(seed)] = metrics
        print(f"saved {out_path} metrics {metrics}", flush=True)
        del model
        if device == "cuda":
            torch.cuda.empty_cache()

    (args.out / "metrics.json").write_text(json.dumps(metrics_by_seed, indent=2), encoding="utf-8")
    return metrics_by_seed


def main(argv: List[str] | None = None) -> None:
    args = parse_args(argv)
    run(args)


if __name__ == "__main__":
    main()
