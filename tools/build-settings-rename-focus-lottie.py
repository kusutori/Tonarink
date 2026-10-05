"""Copy the existing rename motion, adding focus-state segment markers only."""
import json
from pathlib import Path

assets = Path(__file__).resolve().parents[1] / "src/Tonarink.App/Assets/Lottie"
source = json.loads((assets / "RenameEraseIcon.json").read_text(encoding="utf-8"))
source["nm"] = "Tonarink settings device-name focus icon"
source["markers"] = [
    {"tm": 0, "cm": "Off", "dr": 0},
    {"tm": 14, "cm": "On", "dr": 0},
    {"tm": 0, "cm": "OffToOn_Start", "dr": 0},
    {"tm": 14, "cm": "OffToOn_End", "dr": 0},
    {"tm": 18, "cm": "OnToOff_Start", "dr": 0},
    {"tm": 34, "cm": "OnToOff_End", "dr": 0},
]
destination = assets / "SettingsRenameFocusIcon.json"
destination.write_text(json.dumps(source, ensure_ascii=False, separators=(",", ":")) + "\n",
                       encoding="utf-8")
print(destination)
