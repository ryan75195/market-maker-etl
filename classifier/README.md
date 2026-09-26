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
| `CLASSIFIER_BATCH_SIZE` | `16` | Max states collated into one forward pass in `predict_batch`, per seed. Bounds peak GPU memory for a request; lower it if a batch of long states OOMs or spills into shared memory. |

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

## Training toolkit

Model loading lives in `mmclassifier.models` (`ModelRegistry`, `load_laya_ensemble`,
`Ensemble`) and the training toolkit (`mmclassifier.build`, `mmclassifier.train`,
`mmclassifier.evaluate`) imports it directly instead of re-implementing the seed-loading
convention, so a model built with these tools loads back into the sidecar unchanged.

The toolkit ports the pilot fine-tuning recipe (base Laya checkpoint, fine-tuned from a
shared `base-hist` checkpoint, then per-family with 3 random seeds averaged at serve time)
into three reusable steps. None of the three commands need a GPU except `train`.

### `python -m mmclassifier.build`

Builds `train.jsonl` / `val.jsonl` for a family from labelled listings plus historical
replay rows.

```powershell
python -m mmclassifier.build `
  --taxonomy E:\data\market-maker-classifier\taxonomies\iphone-15.json `
  --labels "pilot:E:\data\market-maker-classifier\labels\iphone-15\labels_*.json" `
  --labels "export:E:\data\market-maker-classifier\labels\iphone-15\reviewed.jsonl" `
  --replay E:\data\market-maker-classifier\labels\train.jsonl `
  --replay-count 3000 `
  --val-listings 40 `
  --out E:\data\market-maker-classifier\models\iphone-15-rebuild\data
```

| Flag | Meaning |
|---|---|
| `--taxonomy` | Path to the family's taxonomy JSON (`questions.<name>.{instructions,criteria,askWhen?}`). |
| `--labels` | Repeatable. `<format>:<pattern>`, e.g. `pilot:labels_*.json` or `export:reviewed.jsonl`. All sources are loaded and merged, de-duplicated by listing id; when the same id appears in more than one source, the source given later on the command line wins. |
| `--replay` | JSONL of historical rows (already `{src, state, q, gold}`) mixed in for regularisation. |
| `--replay-count` | Rows sampled from `--replay` (capped at the pool size). |
| `--val-listings` | Listings held out for validation, split with a fixed seed before any row expansion. |
| `--seed` | Split/sample/shuffle seed (default `5`). |
| `--out` | Output directory; writes `train.jsonl` and `val.jsonl`. |

**Row building.** For every taxonomy question, a listing contributes one row only when: the
question's `askWhen` clauses (if any) all hold against that listing's *own* answers, and the
listing's answer for that question is one of the question's option keys (drops
`not_applicable` and any other non-option value). Each row is `{src, state, q, gold}` where
`state` is always `{title, mercari_category, brand, description}` with the description
truncated to 1,200 characters (or `null`). The family's rows are duplicated ×2 and mixed with
the replay sample, then everything is shuffled.

**Label formats:**
- `pilot` — the per-listing label arrays used in the original pilot
  (`labels\<family>\labels_*.json`), joined to listing state by `id` against any sibling
  `*.json` array of state objects in the same directory (conventionally `pool.json` for the
  training pool, `test.json` for a held-out test set).
- `export` — the review queue's JSONL export: one `{"listingId", "taxonomyVersion", "state":
  {...}, "answers": {question: option}}` object per line. No join step; `state` and
  `answers` are taken as given.

### `python -m mmclassifier.train`

```powershell
$env:PYTORCH_CUDA_ALLOC_CONF = "expandable_segments:True"
python -m mmclassifier.train `
  --init E:\data\market-maker-classifier\models\base-hist\seed7.pt `
  --data E:\data\market-maker-classifier\models\iphone-15-rebuild\data `
  --seeds 7,11,13 `
  --epochs 3 `
  --out E:\data\market-maker-classifier\models\iphone-15-rebuild
```

Loads the base Laya checkpoint, `load_state_dict`s `--init` onto it, enables gradient
checkpointing on the encoder, then fine-tunes one model per seed with AdamW (lr `2e-5`,
weight decay `0.01`), batch size 16, a 5% linear warm-up followed by linear decay, and
gradient clipping at 1.0. Validation runs in chunks of 16 with `torch.cuda.empty_cache()`
called afterwards — without it a 16 GB card spills into shared memory and slows down by an
order of magnitude. Writes `seed<N>.pt` per seed plus `metrics.json` (validation accuracy per
question, per seed) to `--out`, which should be a fresh subdirectory of `MODELS_DIR` — **never
overwrite an existing model directory**, since the sidecar and any in-flight evaluation may
still be reading from it. Always initialise `--init` from `base-hist`, not from another
family's fine-tuned model: in the pilot, a PS5-initialised iPhone model transferred far worse
(condition accuracy 26%) than starting from `base-hist` (65%).

### `python -m mmclassifier.evaluate`

```powershell
python -m mmclassifier.evaluate `
  --model-dir E:\data\market-maker-classifier\models\iphone-15-rebuild `
  --taxonomy E:\data\market-maker-classifier\taxonomies\iphone-15.json `
  --test "E:\data\market-maker-classifier\labels\iphone-15\test_labels_*.json" `
  --labels-format pilot
```

Loads the ensemble in `--model-dir` via `mmclassifier.models.load_laya_ensemble` (which
already forces calibration temperature to 1.0), predicts every taxonomy question over the
test listings' states, and scores gold-scoped: a question is only scored on a listing when
its `askWhen` clauses hold against that listing's gold answers, mirroring how `build` decides
which rows to train on. Prints and writes (`--out`, default `<model-dir>/evaluation.json`)
per-question ensemble accuracy, an accuracy split between answers with averaged confidence at
or above 0.9 and those below it, the fraction of answers below 0.9 ("flagged"), and the list
of mistakes.

### `python -m mmclassifier.retrain`

Runs `build` → `train` → `evaluate` end to end for an existing family and, optionally, promotes
the result. It never overwrites or deletes a model directory — it always trains into a fresh
`<models-dir>\<family-key>-<yyyyMMddHHmm>` directory and only reads from the family's current
model directory to score it.

```powershell
python -m mmclassifier.retrain `
  --family-key iphone-15 `
  --api http://localhost:5000 `
  --taxonomy E:\data\market-maker-classifier\taxonomies\iphone-15.json `
  --base-labels "E:\data\market-maker-classifier\labels\iphone-15\labels_*.json" `
  --replay E:\data\market-maker-classifier\labels\train.jsonl `
  --test "E:\data\market-maker-classifier\labels\iphone-15\test_labels_*.json" `
  --models-dir E:\data\market-maker-classifier\models `
  --init E:\data\market-maker-classifier\models\base-hist\seed7.pt `
  --promote
```

| Flag | Meaning |
|---|---|
| `--family-key` | The family's `Key` in the .NET API, used to look it up via `GET /api/families` and to name the new model directory. |
| `--api` | Base URL of the .NET API (e.g. `http://localhost:5000`). |
| `--taxonomy` | Taxonomy JSON, same as `build`/`evaluate`. |
| `--base-labels` | A `pilot`-format glob of labelled listings, merged with the review queue's export fetched from the API (the fetched export wins on a shared listing id — it reflects the latest human corrections). |
| `--replay` | Same as `build --replay`. |
| `--test` | A `pilot`-format glob of held-out test listings, scored against both the candidate and the family's current model. |
| `--models-dir` | Same `MODELS_DIR` the sidecar serves from; the candidate is written to a fresh subdirectory of it. |
| `--init` | Base checkpoint state dict to fine-tune from, same as `train --init`. |
| `--promote` | When set, calls `PUT /api/families/{id}` with the candidate's directory name as `modelName`, but only if the comparison passes (see below). |

Steps:
1. Fetches `GET /api/families`, finds the family by `--family-key`, and reads its current
   `modelName`.
2. Fetches `GET /api/families/{id}/labels.jsonl` and writes it into the new candidate
   directory, then builds the training data from `--base-labels` plus that export.
3. Trains the candidate with `mmclassifier.train`'s recipe.
4. Evaluates the candidate and the family's *current* model (read-only — it never writes into
   the current model's directory) on the same `--test` set.
5. Writes `comparison.json` next to the candidate's `seed*.pt` files with both models'
   per-question accuracy, their means, and any question that dropped.
6. With `--promote`: promotes only when the candidate's mean per-question accuracy is at least
   the current model's **and** no individual question's accuracy dropped by more than 2.0
   points. Otherwise it leaves the family on its current model.

## Adding a product family

1. **Write the taxonomy** (`taxonomies\<family>.json`): one `questions.<name>` entry per
   axis, each with `instructions`, `criteria` (option key → description), and `askWhen` for
   any axis that only applies given another axis's answer (see `taxonomies\iphone-15.json`
   for the `askWhen: [{"question": ..., "anyOf": [...]}]` shape — all clauses must hold).
   Put every labelling convention **in the criteria text itself** — the model is trained and
   served on `instructions`/`criteria` alone, it never sees a separate labelling guide. This
   was the single biggest fix in the pilot: criteria text with concrete disambiguation
   ("a case *for* an iPhone 15 is `case_or_protector`, not `iphone_15_family`") trains and
   scores far better than criteria that assumes shared context with a human labeller.
2. **LLM-label about 400 listings** into the `pilot` label format, sampling so the item's own
   category (or the gated axes' trigger condition, e.g. "is this an iPhone 15") is
   over-represented — otherwise the gated questions (model, storage, carrier, condition, ...)
   won't have enough in-category examples to learn from.
3. **Hand- or Opus-label a separate 150-listing test set**, disjoint from the training pool,
   in the same label format.
4. **Build, train, evaluate:**
   ```powershell
   python -m mmclassifier.build --taxonomy ... --labels pilot:... --replay ... --out <dir>\data
   python -m mmclassifier.train --init <base-hist seed>.pt --data <dir>\data --out <dir>
   python -m mmclassifier.evaluate --model-dir <dir> --taxonomy ... --test ...
   ```
5. **Copy to `MODELS_DIR`.** Once the evaluation looks right, copy (don't move, keep the
   working directory around for reproducing) `<dir>` into `MODELS_DIR/<family-name>` so the
   sidecar can serve it.
6. **Create the family and point it at the model.** `POST /api/families` with the family's
   `key`, `name` and `modelName` (the directory name copied into `MODELS_DIR` above). Later,
   `PUT /api/families/{id}` (accepting `name` and/or `modelName`, both optional) switches the
   family over to a different model directory without recreating it — `modelName` must match
   `[a-z0-9-]+`.

Always start from `base-hist`'s `seed7.pt`, never from another family's fine-tuned model —
see the transfer-accuracy warning under `mmclassifier.train` above.

**Retraining an existing family** should go through `python -m mmclassifier.retrain
--promote` (see above) instead of the manual steps above — it builds from the latest reviewed
labels, trains into a fresh directory, evaluates the candidate against the family's current
model on the same test set, and only calls `PUT /api/families/{id}` when the candidate is not
worse.
