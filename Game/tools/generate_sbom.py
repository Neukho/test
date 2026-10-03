#!/usr/bin/env python3
"""CycloneDX 1.5 SBOM 생성기 (표준 라이브러리만). 출력: docs/legal/SBOM.cdx.json

사용:
  python3 tools/generate_sbom.py            # 생성 + 구조 자체 검사
  python3 tools/generate_sbom.py --check    # 파일을 쓰지 않고, 생성 가능 여부 + 저장된 SBOM 과 components 일치 여부 검사

원칙: 모르는 버전은 'unspecified', 모르는 라이선스 ID 는 만들지 않고 license.name 에 서술한다.
버전을 알게 되면 아래 COMPONENTS 표만 고치면 된다(빌드 후 실제 사용 버전으로 갱신 권장).
"""
import argparse
import datetime
import json
import os
import re
import sys
import uuid

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "docs", "legal", "SBOM.cdx.json")
UCL = "Unity Companion License"  # Unity 공식 패키지 라이선스(SPDX ID 없음 -> name 으로 기재)

# (bom-ref, type, name, version, license dict, scope, purl or None, note)
COMPONENTS = [
    ("unity-engine", "platform", "Unity Engine", "6000.x (exact editor version: unspecified)",
     {"name": "Unity Software proprietary terms (depends on your Unity plan; verify)"}, "required", None,
     "Runtime is built into the player. Record the exact editor version from ProjectSettings/ProjectVersion.txt at release."),
    ("pkg-ugui", "library", "com.unity.ugui (uGUI + TextMesh Pro)", "2.0.0",
     {"name": UCL}, "required", None,
     "TextMesh Pro is merged into com.unity.ugui in Unity 6. tools/unity_compile_check compiles the GitHub 6000.0 branch of uGUI; the exact version the editor resolves is unspecified until the project is opened."),
    ("pkg-inputsystem", "library", "com.unity.inputsystem", "unspecified",
     {"name": UCL}, "required", None,
     "Constraint: 1.x. tools/unity_compile_check clones the upstream default branch (not pinned); 1.20.1 was observed in one local run. Pin it in Packages/manifest.json."),
    ("font-noto-sans-kr", "data", "Noto Sans KR (PLANNED - not yet bundled)", "unspecified",
     {"id": "OFL-1.1"}, "optional", None,
     "Placeholder entry. Resources/Fonts/MainFont does not exist yet; FontProvider falls back to OS fonts. Remove or confirm after the font is chosen (see docs/legal/THIRD_PARTY_NOTICES.md)."),
    # ---- 개발/검증 도구: 배포물에 포함되지 않음(scope=excluded) ----
    ("tool-unityengine-modules", "library", "UnityEngine.Modules (NuGet reference assemblies, compile check only)", "2021.3.33",
     {"name": "unspecified - verify on nuget.org"}, "excluded", "pkg:nuget/UnityEngine.Modules@2021.3.33",
     "Used only by tools/unity_compile_check. Not shipped."),
    ("tool-nunit", "library", "NUnit", "3.14.0",
     {"id": "MIT"}, "excluded", "pkg:nuget/NUnit@3.14.0", "Test framework referenced by the compile check of Tests/EditMode. Not shipped."),
    ("tool-system-memory", "library", "System.Memory (NuGet)", "4.5.5",
     {"id": "MIT"}, "excluded", "pkg:nuget/System.Memory@4.5.5", "Needed to compile Input System sources in the compile check. Not shipped."),
    ("tool-dotnet-sdk", "application", ".NET SDK", "8.0",
     {"id": "MIT"}, "excluded", None, "Used by tools/unity_compile_check/run.sh (dotnet build)."),
    ("tool-mono-mcs", "application", "Mono C# compiler (mcs) and runtime", "unspecified",
     {"name": "unspecified - see upstream Mono licensing"}, "excluded", None, "Used by tools/csharp_check/*/run.sh pure-logic harnesses."),
    ("tool-python", "application", "Python 3 (standard library only)", "3.x",
     {"name": "Python Software Foundation License"}, "excluded", None, "tools/*.py"),
]


def project_version():
    """ProjectSettings/ProjectSettings.asset 의 bundleVersion(있을 때). 저장소에는 아직 없을 수 있다."""
    p = os.path.join(ROOT, "UnityProject", "ProjectSettings", "ProjectSettings.asset")
    try:
        m = re.search(r"^\s*bundleVersion:\s*(\S+)", open(p, encoding="utf-8").read(), re.M)
        if m:
            return m.group(1)
    except OSError:
        pass
    return "unspecified"


def build_components():
    out = []
    for ref, typ, name, ver, lic, scope, purl, note in COMPONENTS:
        c = {"bom-ref": ref, "type": typ, "name": name, "version": ver,
             "scope": scope, "licenses": [{"license": lic}],
             "properties": [{"name": "morphehouse:note", "value": note}]}
        if purl:
            c["purl"] = purl
        out.append(c)
    return out


def build_bom():
    comps = build_components()
    # 결정적 serialNumber: 구성 요소 내용에서 유도(내용이 같으면 같은 번호)
    seed = json.dumps(comps, sort_keys=True, ensure_ascii=False)
    serial = "urn:uuid:" + str(uuid.uuid5(uuid.NAMESPACE_URL, "morphehouse-sbom/" + seed))
    app = {"bom-ref": "app-moon-corner-house", "type": "application", "name": "MoonCornerHouse (Moon Corner House)",
           "version": project_version(), "description": "Korean furry cafe-tycoon + romance visual novel (Unity 6)."}
    shipped = [c["bom-ref"] for c in comps if c["scope"] != "excluded"]
    return {
        "bomFormat": "CycloneDX",
        "specVersion": "1.5",
        "serialNumber": serial,
        "version": 1,
        "metadata": {
            "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
            "tools": {"components": [{"type": "application", "name": "tools/generate_sbom.py", "version": "1"}]},
            "component": app,
            "properties": [{"name": "morphehouse:status", "value": "DRAFT - versions marked 'unspecified' must be filled in before release; not a legal document"}],
        },
        "components": comps,
        "dependencies": [{"ref": app["bom-ref"], "dependsOn": shipped}] + [{"ref": r} for r in shipped],
    }


VALID_TYPES = {"application", "framework", "library", "container", "platform", "operating-system", "device",
               "device-driver", "firmware", "file", "machine-learning-model", "data", "cryptographic-asset"}


def self_check(bom):
    errs = []
    if bom.get("bomFormat") != "CycloneDX": errs.append("bomFormat")
    if bom.get("specVersion") != "1.5": errs.append("specVersion")
    if not re.match(r"^urn:uuid:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", bom.get("serialNumber", "")): errs.append("serialNumber")
    if not isinstance(bom.get("version"), int) or bom["version"] < 1: errs.append("version")
    md = bom.get("metadata", {})
    if not re.match(r"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ$", md.get("timestamp", "")): errs.append("metadata.timestamp")
    mc = md.get("component", {})
    if mc.get("type") not in VALID_TYPES or not mc.get("name"): errs.append("metadata.component")
    refs = {mc.get("bom-ref")}
    for c in bom.get("components", []):
        if c.get("type") not in VALID_TYPES: errs.append("type:" + str(c.get("name")))
        if not c.get("name"): errs.append("component without name")
        r = c.get("bom-ref")
        if not r or r in refs: errs.append("bom-ref missing/duplicate:" + str(r))
        refs.add(r)
        if c.get("scope") not in ("required", "optional", "excluded"): errs.append("scope:" + str(r))
        for l in c.get("licenses", []):
            lic = l.get("license", {})
            if bool(lic.get("id")) == bool(lic.get("name")): errs.append("license needs exactly one of id/name:" + str(r))
    for d in bom.get("dependencies", []):
        if d.get("ref") not in refs: errs.append("dependency ref unknown:" + str(d.get("ref")))
        for x in d.get("dependsOn", []):
            if x not in refs: errs.append("dependsOn unknown:" + x)
    return errs


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    a = ap.parse_args()
    bom = build_bom()
    text = json.dumps(bom, indent=2, ensure_ascii=False) + "\n"
    json.loads(text)  # 왕복 파싱
    errs = self_check(bom)
    if errs:
        print("SBOM self-check FAILED:", errs)
        return 1
    n_unspec = sum(1 for c in bom["components"] if "unspecified" in c["version"])
    if a.check:
        try:
            old = json.load(open(OUT, encoding="utf-8"))
        except (OSError, ValueError) as e:
            print("SBOM file missing/invalid (%s): run python3 tools/generate_sbom.py" % e)
            return 1
        oerrs = self_check(old)
        if oerrs:
            print("committed SBOM invalid:", oerrs)
            return 1
        if old.get("components") != bom["components"]:
            print("committed SBOM is stale: run python3 tools/generate_sbom.py")
            return 1
        print("SBOM OK: %d components (%d with unspecified version), up to date" % (len(bom["components"]), n_unspec))
        return 0
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8") as f:
        f.write(text)
    print("wrote %s: %d components (%d with unspecified version), self-check OK" % (os.path.relpath(OUT, ROOT), len(bom["components"]), n_unspec))
    return 0


if __name__ == "__main__":
    sys.exit(main())
