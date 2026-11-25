"""Extract aggregate evidence only; never copy complete ancestry or the full Editor log.

Example: python Tools/ExtractLocomotionRunEvidence.py --runs 39 41
Run numbers count geodesic initializations in the supplied log, starting at one.
"""
import argparse
import json
import re
from pathlib import Path


def number(text):
    return float(text.replace(",", "."))


def extract(path):
    runs, current, pending = [], None, None
    with path.open(encoding="utf-8-sig", errors="replace") as stream:
        for line_number, raw in enumerate(stream, 1):
            line = raw.strip()
            if line == "[Startup Config Applied]":
                pending = {"line": line_number, "settings": [], "biology": {}}
            elif pending is not None and not line.startswith("[GeodesicBiology]"):
                if line.startswith("Biology startup parameters"):
                    pending["biology"] = json.loads(line[line.index("{"):])
                elif line.startswith("Start Paused:"):
                    pending["settings"].append(line)
                elif not pending["settings"] or not pending["settings"][-1].startswith("Start Paused:"):
                    pending["settings"].append(line)
            if line.startswith("[GeodesicBiology] mode=Geodesic biologySeed="):
                current = {"run": len(runs) + 1, "initializationLine": line_number, "initialization": line,
                           "startup": pending, "reports": [], "groups": {}, "firstPositiveSnapshot": {}}
                pending = None
                runs.append(current)
            if current is None or not line.startswith("[GeodesicChemotaxisAB]"):
                continue
            time = number(re.search(r"simTime=([^ ]+)", line)[1])
            if "kind=population" in line:
                current["reports"].append({"line": line_number, "time": time, "population": line, "phenotypes": {}})
            elif "kind=locomotion" in line:
                phenotype = re.search(r"type=([^ ]+)", line)[1]
                group = current["groups"].setdefault(phenotype, {"parentBirths": 0, "deaths": 0, "organismSeconds": 0})
                births, deaths = map(int, re.search(r"parentBirths/deaths=(\d+)/(\d+)", line).groups())
                group["parentBirths"] += births
                group["deaths"] += deaths
                group["organismSeconds"] += number(re.search(r"organismSeconds=([^ ]+)", line)[1])
                group["finalN"] = int(re.search(r"snapshotN=(\d+)", line)[1])
                if group["finalN"]:
                    current["firstPositiveSnapshot"].setdefault(phenotype, time)
                if current["reports"]:
                    current["reports"][-1]["phenotypes"][phenotype] = line
    return runs


def summarize(run):
    reports = run.pop("reports")
    run["reportWindows"] = len(reports)
    run["modes"] = sorted({re.search(r" mode=([^ ]+)", r["population"])[1] for r in reports})
    run["modeSwitches"] = max((int(re.search(r"runModeSwitches=(\d+)", r["population"])[1]) for r in reports), default=0)
    run["mixedWindows"] = sum("mixedWindow=True" in r["population"] for r in reports)
    run["finalReport"] = reports[-1] if reports else None
    run["nearest990Report"] = min(reports, key=lambda r: abs(r["time"] - 990)) if reports else None
    for group in run["groups"].values():
        seconds = group["organismSeconds"]
        group["parentBirthsPerOrganismSecond"] = group["parentBirths"] / seconds if seconds else None
    return run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--log", type=Path, default=Path.home() / "AppData/Local/Unity/Editor/Editor.log")
    parser.add_argument("--runs", type=int, nargs="+", default=[39, 41])
    parser.add_argument("--output", type=Path, default=Path("Temp/LocomotionAudit/selected-run-evidence.json"))
    args = parser.parse_args()
    runs = extract(args.log)
    selected = [summarize(runs[index - 1]) for index in args.runs]
    result = {"source": "local Unity Editor.log", "runs": selected}
    if len(selected) == 2:
        a, b = [r["startup"] for r in selected]
        result["loggedEnvironmentIdentical"] = a["settings"] == b["settings"]
        result["biologyDifferences"] = {k: [a["biology"].get(k), b["biology"].get(k)]
                                        for k in a["biology"].keys() | b["biology"].keys() if a["biology"].get(k) != b["biology"].get(k)}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "runNumbers": args.runs,
                      "loggedEnvironmentIdentical": result.get("loggedEnvironmentIdentical"),
                      "biologyDifferences": result.get("biologyDifferences")}, indent=2))


if __name__ == "__main__":
    main()
