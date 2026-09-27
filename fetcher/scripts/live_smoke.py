from __future__ import annotations

import asyncio
import json
import sys
from pathlib import Path

FETCHER_ROOT = Path(__file__).resolve().parents[1]
if str(FETCHER_ROOT) not in sys.path:
    sys.path.insert(0, str(FETCHER_ROOT))

from mmfetch.client import MercariClient
from mmfetch.session import MercariSession
from mmfetch.transport import CurlCffiTransport

REQUIRED_FIELDS = (
    "id",
    "name",
    "price",
    "status",
    "originalPrice",
    "categoryId",
    "itemCondition",
    "brand",
    "itemCategoryHierarchy",
    "seller.sellerId",
)
KNOWN_GAP_FIELDS = ("color", "customFacetsList", "shippingPayer")


def _field_coverage(items: list[dict]) -> dict[str, bool]:
    coverage = {field: False for field in (*REQUIRED_FIELDS, *KNOWN_GAP_FIELDS)}
    for item in items:
        if item.get("id"):
            coverage["id"] = True
        if item.get("name"):
            coverage["name"] = True
        if item.get("price") is not None:
            coverage["price"] = True
        if item.get("status"):
            coverage["status"] = True
        if item.get("originalPrice") is not None:
            coverage["originalPrice"] = True
        if item.get("categoryId") is not None:
            coverage["categoryId"] = True
        if item.get("itemCondition"):
            coverage["itemCondition"] = True
        if (item.get("brand") or {}).get("name"):
            coverage["brand"] = True
        if item.get("itemCategoryHierarchy"):
            coverage["itemCategoryHierarchy"] = True
        if (item.get("seller") or {}).get("sellerId") is not None:
            coverage["seller.sellerId"] = True
        if item.get("color"):
            coverage["color"] = True
        if item.get("itemSize"):
            coverage["itemSize"] = True
        if item.get("customFacetsList"):
            coverage["customFacetsList"] = True
        if (item.get("shippingPayer") or {}).get("code"):
            coverage["shippingPayer"] = True
    return coverage


async def main() -> int:
    transport = CurlCffiTransport(proxy_url=None, timeout_seconds=40)
    session = MercariSession(transport=transport, max_attempts=5)
    client = MercariClient(transport=transport, session=session, max_attempts=5, max_concurrency=2)

    checks: list[tuple[str, bool, str]] = []

    token = await session.ensure_ready()
    checks.append(("bootstrap", True, f"csrf_len={len(token.csrf)} exp={token.exp}"))

    active_text = await client.fetch_search(
        {"query": "iphone", "itemStatuses": [1], "offset": 0, "length": 100}
    )
    active_items = json.loads(active_text)["data"]["search"]["itemsList"]
    checks.append(("search_active_returns_items", len(active_items) > 0, f"n={len(active_items)}"))

    item_ids = [item["id"] for item in active_items[:3]]
    item_oks = []
    for item_id in item_ids:
        item_text = await client.fetch_item(item_id)
        item = json.loads(item_text)["data"]["item"]
        item_oks.append(item is not None and item.get("id") == item_id)
    checks.append(
        ("item_query_for_3_search_ids", all(item_oks) and len(item_oks) == 3, f"ids={item_ids} ok={item_oks}")
    )

    sold_text = await client.fetch_search(
        {"query": "iphone", "itemStatuses": [2, 3], "offset": 0, "length": 100}
    )
    sold_items = json.loads(sold_text)["data"]["search"]["itemsList"]
    checks.append(("search_sold_returns_items", len(sold_items) > 0, f"n={len(sold_items)}"))

    band_text = await client.fetch_search(
        {"query": "iphone", "itemStatuses": [1], "minPrice": 10000, "maxPrice": 20000, "offset": 0, "length": 100}
    )
    band_items = json.loads(band_text)["data"]["search"]["itemsList"]
    band_prices = [item["price"] for item in band_items]
    band_ok = bool(band_prices) and min(band_prices) >= 10000 and max(band_prices) <= 20000
    checks.append(
        (
            "price_band_10000_20000_cents",
            band_ok,
            f"n={len(band_prices)} min={min(band_prices) if band_prices else None} "
            f"max={max(band_prices) if band_prices else None}",
        )
    )

    offset_text = await client.fetch_search(
        {"query": "iphone", "itemStatuses": [1], "offset": 100, "length": 100}
    )
    offset_ids = [item["id"] for item in json.loads(offset_text)["data"]["search"]["itemsList"]]
    offset_differs = bool(active_items) and bool(offset_ids) and item_ids[0] not in offset_ids[:10]
    checks.append(
        (
            "offset_100_returns_different_page",
            offset_differs,
            f"offset0_first={item_ids[0] if item_ids else None} offset100_first={offset_ids[0] if offset_ids else None}",
        )
    )

    coverage = _field_coverage(active_items + sold_items + band_items)
    for field_name in REQUIRED_FIELDS:
        checks.append((f"field_populated:{field_name}", coverage[field_name], "required"))
    for field_name in KNOWN_GAP_FIELDS:
        checks.append((f"field_populated:{field_name}", coverage[field_name], "known-gap, non-blocking"))

    print("mmfetch live smoke summary")
    print("=" * 60)
    hard_failures: list[str] = []
    for name, ok, detail in checks:
        status = "PASS" if ok else "WARN"
        is_hard = not (name.startswith("field_populated:") and name.split(":", 1)[1] in KNOWN_GAP_FIELDS)
        if not ok and is_hard:
            status = "FAIL"
            hard_failures.append(name)
        print(f"[{status}] {name}: {detail}")
    print("=" * 60)

    if hard_failures:
        print(f"FAILED checks: {hard_failures}")
        return 1

    print("All required checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(asyncio.run(main()))
