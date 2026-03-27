import json
from collections import defaultdict
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT.parents[1] / "reference" / "Slay the Spire 2" / "localization" / "eng" / "events.json"
TARGET = ROOT / "ai-event" / "data" / "vanilla_event_samples.json"


def main() -> None:
    raw = json.loads(SOURCE.read_text(encoding="utf-8"))
    grouped: dict[str, dict[str, object]] = defaultdict(lambda: {"title": "", "pages": defaultdict(dict)})

    for key, value in raw.items():
        event_id = key.split(".", 1)[0]
        event = grouped[event_id]

        if key.endswith(".title"):
            event["title"] = value
            continue

        if ".pages." not in key:
            continue

        _, rest = key.split(".pages.", 1)
        page_name, _, remainder = rest.partition(".")
        page = event["pages"][page_name]

        if remainder == "description":
            page["description"] = value
            continue

        if remainder.startswith("options."):
            _, option_name, field = remainder.split(".", 2)
            options = page.setdefault("options", {})
            options.setdefault(option_name, {})[field] = value
            continue

        page[remainder] = value

    normalized = {}
    for event_id, data in grouped.items():
        normalized[event_id] = {
            "title": data["title"],
            "pages": {page_name: page_data for page_name, page_data in data["pages"].items()},
        }

    TARGET.write_text(json.dumps(normalized, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Wrote {len(normalized)} events to {TARGET}")


if __name__ == "__main__":
    main()
