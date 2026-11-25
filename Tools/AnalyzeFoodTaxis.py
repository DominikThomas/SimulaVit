"""Summarize recorded experiments; missing or zero-test runs are never successes."""
import csv
import argparse
import json
import math
import re
import shutil
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "Temp/FoodTaxisAudit"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def differences(a, b, prefix=''):
    if isinstance(a, dict) and isinstance(b, dict):
        return [row for key in sorted(a.keys() | b.keys())
                for row in differences(a.get(key), b.get(key), f'{prefix}.{key}'.lstrip('.'))]
    return [] if a == b else [{'field': prefix, 'A': a, 'B': b}]


def world_accounting():
    path = DATA / 'batch-world.log'
    if not path.exists():
        return {}
    lines = path.read_text(encoding='utf-8-sig').splitlines()
    resource = [line for line in lines if '[GeodesicResourceAccounting]' in line]
    reaction = [line for line in lines if '[GeodesicReactionAccounting]' in line]
    def maximum(rows, pattern):
        values = [abs(float(match.group(1).replace(',', '.')))
                  for line in rows if (match := re.search(pattern, line))]
        return max(values) if values else None
    checks = {
        'maxResourceTransferViolations': maximum(resource, r'transferViolations=(\d+)'),
        'maxReactionInputTransferViolations': maximum(reaction, r'inputTransferViolations=(\d+)'),
        'maxZeroMeasuredInputWithEnergy': maximum(reaction, r'zeroMeasuredInputWithEnergy=(\d+)'),
        'maxAbsIntervalUnaccountedCarbon': maximum(resource, r'measuredUnaccountedCarbon\(\+missing/-excess\)=([^ ]+)'),
        'maxAbsIndividualReactionMeasuredCarbonResidual': maximum(reaction, r'maxAbsMeasuredCResidual=([^ ]+)'),
        'maxModelViolations': maximum(reaction, r'violations\(model/measured/invalid\)=(\d+)'),
        'maxMeasuredViolations': maximum(reaction, r'violations\(model/measured/invalid\)=\d+/(\d+)'),
        'maxInvalidViolations': maximum(reaction, r'violations\(model/measured/invalid\)=\d+/\d+/(\d+)'),
    }
    return {'source': path.name, 'resourceIntervalsBothTrials': len(resource),
            'reactionIntervalsBothTrials': len(reaction),
            'scope': 'Existing reaction accounting, not a new whole-planet inventory ledger.', **checks}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--publish', action='store_true')
    args = parser.parse_args()
    native_project = DATA / 'native-project-path.txt'
    if native_project.exists():
        persistent = Path(native_project.read_text(encoding='utf-8-sig').strip()) / 'Artifacts/FoodTaxisAudit'
        if persistent.exists():
            for path in persistent.glob('*.json'):
                # Per-world files are written after completion; neighbourhood captures
                # are independent complete snapshots. A running trial is not a pass.
                try:
                    read(path)
                    shutil.copyfile(path, DATA / path.name)
                except json.JSONDecodeError:
                    pass
    results = {}
    for stage in ("baseline", "food", "alternatives", "world"):
        path = DATA / f"{stage}-summary.json"
        if path.exists():
            result = read(path)
            result["executedSuccessfully"] = result["state"] == "completed" and result["passed"] > 0 and result["failed"] == 0
            results[stage] = result
        else:
            results[stage] = {"executedSuccessfully": False, "state": "no-result"}
    for stage in ('food', 'world'):
        path = DATA / f'batch-{stage}-results.xml'
        if path.exists():
            root = ET.parse(path).getroot()
            passed, failed, skipped = [int(root.get(key, '0')) for key in ('passed', 'failed', 'skipped')]
            result = {'source': path.name, 'state': root.get('result'), 'passed': passed, 'failed': failed, 'skipped': skipped,
                      'executedSuccessfully': passed > 0 and failed == 0}
            # Keep a newer/nonempty Editor run (e.g. added layer-binding assertions)
            # and also retain the independently executed batch regression count.
            results[stage + 'Batch'] = result
            if not results[stage]['executedSuccessfully']:
                results[stage] = result
    reports = []
    for name in ("A-current", "B-independent", "C-smooth", "D-response", "E-cadence", "E-speed"):
        path = DATA / f"{name}-ecology.json"
        if not path.exists():
            continue
        for row in read(path)["reports"]:
            reports.append({"alternative": name, "habitat": row["habitat"], "phenotype": row["phenotype"],
                            "viableFraction": row["metabolicallyViableSeconds"] / row["organismSeconds"],
                            "ventFraction": row["ventResidenceFraction"], "coldFraction": row["unsuitableFraction"],
                            "meanH2": row["meanH2Concentration"], "improvingTransitions": row["improvingMovementTransitionFraction"],
                            "unsensedViableExitsLowerBound": row["unsensedViableExitsLowerBound"],
                            "returned": row["returnedFounders"], "departed": row["departedFounders"],
                            "metabolicEnergy": row["metabolicEnergy"], "movementEnergy": row["movementEnergy"],
                            "netMetabolicAfterMovement": row["netMetabolicEnergyAfterMovement"],
                            "netMetabolicAfterMovementPerOrganismSecond": row["netMetabolicEnergyAfterMovement"] / row["organismSeconds"],
                            "carbonAssimilated": row["carbonAssimilated"], "finalStoredCarbon": row["finalStoredCarbon"],
                            "births": row["births"], "deaths": row["deaths"], "descendants": row["survivingDescendants"],
                            "amoeboidParentBirths": row["amoeboidParentBirths"], "flagellarMutations": row["flagellarMutations"],
                            "carbonResidual": row["carbonBalanceResidual"]})
    if reports:
        with (DATA / "ecological-comparison.csv").open("w", encoding="utf-8", newline="") as stream:
            writer = csv.DictWriter(stream, fieldnames=list(reports[0])); writer.writeheader(); writer.writerows(reports)
    scales = {"referenceCarbonGainPerSecond": .008 / .5, "carbonGrowthSeconds": (.3 - .15) / (.008 / .5),
              "cellMeanKm": 151.087341221379, "centerToEdgeASeconds": 151.087341221379 / 2 / 38.4,
              "centerToEdgeFSeconds": 151.087341221379 / 2 / 64,
              "turnControllerPiSeconds": math.pi / .5, "turnControllerPiDistanceKm": math.pi / .5 * 38.4,
              "note": "Controller-turn estimate excludes amoeboid oscillatory run noise and coast reflections."}
    scales["hazards"] = [{"phenotype": name, "trend": trend, "perSenseProbability": 1 - (1 - p) ** (dt / .5 * multiplier),
                          "meanDiscreteWaitSeconds": dt / (1 - (1 - p) ** (dt / .5 * multiplier))}
                         for name, dt, multiplier in (("A", .5, 1), ("F", .2, .3))
                         for trend, p in (("better", .05), ("neutral", .2), ("worse", .65))]
    scales["zeroFieldMemorySamplesUntilNeutral"] = [{"initialTicks": q, "samples": max(0, math.ceil(math.log(.05 * .0001 / q) / math.log(.65)))}
                                                   for q in (.0001, .01, 1, 100)]
    output = {"nativeStages": results, "responseScales": scales, "ecologicalRows": reports}
    output['gradientPairs'] = [read(path) for path in sorted(DATA.glob('pair-*.json'))]
    output['walks'] = [read(path) for path in sorted(DATA.glob('walk-*.json'))]
    output['otherMetabolismSupport'] = [read(path) for path in sorted(DATA.glob('support-*.json'))]
    output['worlds'] = [read(path) for path in sorted(DATA.glob('world-?.json'))]
    configs = {mode: read(path) for mode in ('A', 'B') if (path := DATA / f'world-{mode}-startup.json').exists()}
    output['worldStartupConfigs'] = configs
    output['worldStartupDifferences'] = differences(configs['A'], configs['B']) if len(configs) == 2 else None
    output['worldAccounting'] = world_accounting()
    output['baselineEcology'] = [read(path) for path in sorted((ROOT / 'Temp/LocomotionAudit').glob('ecology-?.json'))]
    output['neighbourhoods'] = [read(path) for path in sorted(DATA.glob('neighbourhood-*.json'))]
    (DATA / "analysis.json").write_text(json.dumps(output, indent=2) + "\n", encoding="utf-8")
    if args.publish:
        directory = ROOT / 'Docs/Diagnostics'
        directory.mkdir(parents=True, exist_ok=True)
        (directory / 'FoodTaxisMeasurements.json').write_text(json.dumps(output, indent=2) + '\n', encoding='utf-8')
        if reports:
            shutil.copyfile(DATA / 'ecological-comparison.csv', directory / 'FoodTaxisEcologicalComparison.csv')
    print(json.dumps({"native": {key: value["executedSuccessfully"] for key, value in results.items()},
                      "ecologicalRows": len(reports)}, indent=2))


if __name__ == "__main__":
    main()
