from __future__ import annotations

from pathlib import Path
import json
import re
import subprocess
import hashlib
import xml.etree.ElementTree as ET
from typing import Iterable, Dict, Any, List

BASE = Path(__file__).resolve().parent
KB = BASE / "kb"
INPUTS = BASE / "inputs"
OUTPUT = BASE / "output"
OUTPUT.mkdir(parents=True, exist_ok=True)

OUT_JSONL = OUTPUT / "corpus.jsonl"
OUT_MANIFEST = OUTPUT / "corpus_manifest.json"
OUT_VERSIONS = OUTPUT / "source_versions.json"

MAX_CHARS = 26000
LINES_PER_CHUNK = 240
LINE_OVERLAP = 35

SKIP_DIRS = {".git", "bin", "obj", ".vs", "packages", "node_modules"}
TEXT_EXTS = {".cs", ".md", ".txt", ".ini", ".config", ".csproj", ".sln", ".props", ".targets", ".log"}

def sha1_text(text: str) -> str:
    return hashlib.sha1(text.encode("utf-8", errors="replace")).hexdigest()

def safe_read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")

def should_skip(path: Path) -> bool:
    return any(part in SKIP_DIRS for part in path.parts)

def git_head(repo: Path) -> str | None:
    try:
        return subprocess.check_output(
            ["git", "-C", str(repo), "rev-parse", "HEAD"],
            text=True,
            stderr=subprocess.DEVNULL
        ).strip()
    except Exception:
        return None

def line_chunks(text: str) -> Iterable[tuple[int, int, str]]:
    lines = text.splitlines()
    if len(text) <= MAX_CHARS:
        yield 1, len(lines), text
        return

    start = 0
    while start < len(lines):
        end = min(len(lines), start + LINES_PER_CHUNK)
        yield start + 1, end, "\n".join(lines[start:end])
        if end >= len(lines):
            break
        start = max(start + 1, end - LINE_OVERLAP)

def extract_cs_metadata(text: str) -> Dict[str, Any]:
    namespaces = re.findall(r"\bnamespace\s+([A-Za-z_][\w.]*)", text)
    symbols = re.findall(
        r"\b(?:public|internal|protected|private)?\s*"
        r"(?:static\s+|sealed\s+|abstract\s+|partial\s+)*"
        r"(?:class|struct|interface|enum|record)\s+([A-Za-z_]\w*)",
        text
    )
    members = re.findall(
        r"\b(?:public|internal|protected)\s+"
        r"(?:static\s+|virtual\s+|override\s+|abstract\s+|sealed\s+|async\s+)*"
        r"[\w<>\[\],.?]+\s+([A-Za-z_]\w*)\s*\(",
        text
    )
    return {
        "namespaces": sorted(set(namespaces)),
        "declared_symbols": sorted(set(symbols)),
        "public_member_candidates": sorted(set(members))[:200],
    }

def emit(records: List[Dict[str, Any]], *, source: str, path: str,
         kind: str, content: str, metadata: Dict[str, Any]) -> None:
    if not content.strip():
        return
    cid = sha1_text(f"{source}\n{path}\n{kind}\n{content}")[:20]
    records.append({
        "id": cid,
        "source": source,
        "path": path.replace("\\", "/"),
        "kind": kind,
        "content": content,
        "metadata": metadata,
    })

def ingest_text_tree(records: List[Dict[str, Any]], root: Path, source: str,
                     allowed_exts=TEXT_EXTS, include_predicate=None,
                     base_metadata=None) -> None:
    if not root.exists():
        return
    base_metadata = dict(base_metadata or {})
    for path in root.rglob("*"):
        if not path.is_file() or should_skip(path):
            continue
        if allowed_exts and path.suffix.lower() not in allowed_exts:
            continue
        rel = path.relative_to(root).as_posix()
        if include_predicate and not include_predicate(rel):
            continue
        text = safe_read(path)
        meta = dict(base_metadata)
        if path.suffix.lower() == ".cs":
            meta.update(extract_cs_metadata(text))
        for line_start, line_end, chunk in line_chunks(text):
            chunk_meta = dict(meta)
            chunk_meta.update({"line_start": line_start, "line_end": line_end})
            emit(records, source=source, path=rel, kind=path.suffix.lower().lstrip("."),
                 content=chunk, metadata=chunk_meta)

def ingest_xml_api(records: List[Dict[str, Any]], root: Path) -> None:
    if not root.exists():
        return
    for path in root.rglob("*.xml"):
        try:
            tree = ET.parse(path)
        except ET.ParseError:
            continue
        assembly = tree.findtext("./assembly/name")
        members = tree.findall("./members/member")
        for member in members:
            name = member.attrib.get("name", "")
            xml_content = ET.tostring(member, encoding="unicode")
            text_content = " ".join("".join(member.itertext()).split())
            emit(
                records,
                source="local_api_xml",
                path=path.relative_to(root).as_posix(),
                kind="xml_api_member",
                content=f"{name}\n{text_content}\n\nRAW_XML:\n{xml_content}",
                metadata={
                    "assembly": assembly,
                    "member_id": name,
                    "priority": 10,
                    "api": "SHVDN v3 compile reference"
                },
            )

def ingest_nativedb(records: List[Dict[str, Any]], path: Path) -> None:
    if not path.exists():
        return
    data = json.loads(safe_read(path))
    # Expected shape: { namespace: { hash: native_record } }
    for namespace, entries in data.items():
        if not isinstance(entries, dict):
            continue
        for hash_value, record in entries.items():
            if not isinstance(record, dict):
                continue
            name = record.get("name") or hash_value
            params = record.get("params", [])
            returns = record.get("results") or record.get("return_type") or record.get("returnType")
            canonical = {
                "namespace": namespace,
                "name": name,
                "hash": hash_value,
                "jhash": record.get("jhash"),
                "params": params,
                "returns": returns,
                "description": record.get("description") or record.get("comment"),
                "build": record.get("build"),
                "raw": record,
            }
            emit(
                records,
                source="native_db_legacy",
                path="natives.json",
                kind="gta_native",
                content=json.dumps(canonical, ensure_ascii=False, indent=2),
                metadata={
                    "namespace": namespace,
                    "native_name": name,
                    "hash": hash_value,
                    "priority": 40,
                    "game": "legacy"
                }
            )

def main() -> None:
    records: List[Dict[str, Any]] = []
    versions: Dict[str, Any] = {}

    shvdn = KB / "scripthookvdotnet"
    wiki = KB / "scripthookvdotnet.wiki"
    nativedb = KB / "gta5-nativedb-data"

    versions["scripthookvdotnet"] = git_head(shvdn)
    versions["scripthookvdotnet_wiki"] = git_head(wiki)
    versions["gta5_nativedb_data"] = git_head(nativedb)

    def shvdn_include(rel: str) -> bool:
        allowed_roots = (
            "README.md",
            "ScriptHookVDotNet.ini",
            "examples/",
            "source/scripting_v3/GTA/",
            "source/core/",
        )
        return rel == "README.md" or rel == "ScriptHookVDotNet.ini" or rel.startswith(allowed_roots[2:])

    ingest_text_tree(
        records, shvdn, "shvdn_source",
        include_predicate=shvdn_include,
        base_metadata={
            "repository_commit": versions["scripthookvdotnet"],
            "priority": 20,
            "api": "v3",
            "game": "legacy"
        }
    )

    ingest_text_tree(
        records, wiki, "shvdn_wiki",
        allowed_exts={".md"},
        base_metadata={
            "repository_commit": versions["scripthookvdotnet_wiki"],
            "priority": 30,
            "api": "v3"
        }
    )

    ingest_nativedb(records, nativedb / "natives.json")

    ingest_xml_api(records, INPUTS / "local_api_docs")

    ingest_text_tree(
        records, INPUTS / "project", "current_project",
        allowed_exts=TEXT_EXTS,
        base_metadata={"priority": 0, "project_specific": True}
    )

    ingest_text_tree(
        records, INPUTS / "logs", "current_logs",
        allowed_exts={".log", ".txt"},
        base_metadata={"priority": 0, "runtime_evidence": True}
    )

    records.sort(key=lambda x: (
        x.get("metadata", {}).get("priority", 999),
        x["source"], x["path"], x["id"]
    ))

    with OUT_JSONL.open("w", encoding="utf-8") as f:
        for record in records:
            f.write(json.dumps(record, ensure_ascii=False) + "\n")

    manifest = {
        "record_count": len(records),
        "sources": sorted({r["source"] for r in records}),
        "output": OUT_JSONL.name,
        "notes": [
            "Legacy NativeDB only: natives.json",
            "natives_gen9.json intentionally excluded",
            "Project/log records have highest retrieval priority",
            "Rebuild embeddings after source commit changes"
        ]
    }
    OUT_MANIFEST.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    OUT_VERSIONS.write_text(json.dumps(versions, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"Wrote {len(records)} records to {OUT_JSONL}")
    print(f"Manifest: {OUT_MANIFEST}")
    print(f"Versions: {OUT_VERSIONS}")

if __name__ == "__main__":
    main()
