# mmclassifier

HTTP sidecar that serves fine-tuned [Laya](https://github.com/NandhaKishorM/laya) classifier
ensembles for the market-maker-etl pipeline. It is a standalone Python project, outside the
.NET solution, and the .NET pre-commit gate does not cover it.

The .NET ETL is the only intended caller. It POSTs a batch of listings and a question set
to this service and gets back per-listing answers with calibrated confidence and seed
agreement.

## Setup

Requires Python 3.12. This project depends on `laya==0.3.20`, which pulls in `torch`. On a
machine with an NVIDIA GPU, install the CUDA build of torch first so `pip install -e .` does
not fall back to a CPU-only wheel from PyPI:

```powershell
pip install torch --index-url https://download.pytorch.org/whl/cu128
pip install -e classifier[dev]
```

CPU-only machines can skip the custom torch index and just run `pip install -e classifier[dev]`.

Run the test suite (no GPU or model weights required — it uses an injected fake predictor):

```powershell
pytest classifier
```

## Running the server

```powershell
$env:MODELS_DIR = "E:\data\market-maker-classifier\models"
python -m mmclassifier
```

The server listens on `CLASSIFIER_HOST`/`CLASSIFIER_PORT` (default `0.0.0.0:8765`).

### Environment variables

| Variable | Default | Meaning |
|---|---|---|
| `MODELS_DIR` | *(required)* | Directory of model families. Each subdirectory is a model name; every `seed*.pt` file inside it is one ensemble member's `state_dict`, loaded onto the base Laya checkpoint (`convaiinnovations/laya`). |
| `CLASSIFIER_DEVICE` | `cuda` if available, else `cpu` | Torch device used for every loaded model. |
| `CLASSIFIER_PRELOAD` | *(empty)* | Comma-separated model names to load eagerly at startup instead of on first request, e.g. `iphone-15,ps5-controller`. |
| `CLASSIFIER_API_KEY` | *(empty)* | When set, `/v1/classify` and `/v1/systemone` require `Authorization: Bearer <key>`. `/health` is always open. |
| `CLASSIFIER_HOST` | `0.0.0.0` | Bind host for `python -m mmclassifier`. |
| `CLASSIFIER_PORT` | `8765` | Bind port for `python -m mmclassifier`. |

### Model directory layout

```
MODELS_DIR/
  iphone-15/
    seed7.pt
    seed11.pt
    seed13.pt
  ps5-controller/
    seed7.pt
    seed11.pt
    seed13.pt
```

A model is loaded lazily the first time it is requested (or eagerly via `CLASSIFIER_PRELOAD`).
Loading fetches the base Laya checkpoint once, then clones the underlying model once per
`seed*.pt` file and loads that seed's weights into the clone, so an N-seed ensemble holds N
independent model instances sharing one tokenizer/config. Each loaded agent's calibration
temperature is reset to `1.0` (fine-tuned weights are calibrated at temperature 1; the shipped
base checkpoint otherwise carries an invalid temperature for questions with 11+ options).

Requesting an unknown model name returns `404`.

### Ensemble prediction

For every question, each option's probability is averaged across the ensemble's seeds:

- `choice` is the option with the highest averaged probability.
- `confidence` is that option's averaged probability.
- `agreement` is the fraction of seeds whose own top choice matches the ensemble's `choice`.

Inference runs one request at a time per process (an async lock serializes GPU access); the
lock is held only for the model's forward pass, not for validation.

## Endpoints

### `POST /v1/classify` — batch (the ETL's main path)

Request:

```json
{
  "model": "iphone-15",
  "questions": {
    "item_type": {
      "type": "choice",
      "instructions": "What is the main item this Mercari listing is selling?",
      "criteria": { "iphone_15_family": "...", "other": "..." }
    }
  },
  "states": [
    { "title": "iPhone 15 Pro Max 256GB Unlocked", "brand": "Apple", "description": "..." }
  ]
}
```

Response (`results` is in the same order as `states`):

```json
{
  "model": "iphone-15",
  "members": 3,
  "results": [
    {
      "answers": {
        "item_type": {
          "choice": "iphone_15_family",
          "confidence": 0.97,
          "agreement": 1.0,
          "probabilities": { "iphone_15_family": 0.97, "other": 0.03 }
        }
      }
    }
  ]
}
```

### `POST /v1/systemone` — Jev-compatible single-state form

Kept wire-compatible with `laya.serve`'s `/v1/systemone` so a hosted Jev model can be
compared later by switching the base URL.

Request:

```json
{ "model": "iphone-15", "state": { "title": "..." }, "questions": { "...": { } } }
```

Response:

```json
{
  "model": "iphone-15",
  "answers": {
    "item_type": {
      "type": "choice",
      "choice": "iphone_15_family",
      "confidence": 0.97,
      "probabilities": { "iphone_15_family": 0.97, "other": 0.03 }
    }
  },
  "usage": { "input_tokens": 0, "output_tokens": 0 }
}
```

`/v1/systemone` does not report `agreement` — that field is specific to the batch endpoint's
JSON shape, not part of Jev compatibility.

### `GET /health`

```json
{ "device": "cuda", "models": { "iphone-15": 3 } }
```

`models` lists only the models loaded so far (via `CLASSIFIER_PRELOAD` or a prior request),
mapped to their seed count.

## Limits and validation

- At most 256 `states` and 32 `questions` per request; over either limit returns `413`.
- Each state's JSON representation is capped at 50,000 characters; over that returns `413`.
- Every question must have `"type": "choice"` and at least 2 `criteria` entries; otherwise `422`.
- An unknown `model` name returns `404`.
- With `CLASSIFIER_API_KEY` set, a missing or wrong `Authorization: Bearer` header on
  `/v1/classify` or `/v1/systemone` returns `401`.

## Training tooling note

Model loading lives in `mmclassifier.models` (`ModelRegistry`, `load_laya_ensemble`,
`Ensemble`) so a future training toolkit can import it directly instead of re-implementing
the seed-loading convention.
