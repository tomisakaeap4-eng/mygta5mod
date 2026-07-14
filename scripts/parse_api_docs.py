#!/usr/bin/env python3
"""Create deterministic, validated lookup trees for the local GTA V API corpus.

The PowerShell and Bash entry points call this module so both platforms produce
the same schema, paths, manifests, and validation behaviour.  It uses only the
Python standard library.
"""

import argparse
import copy
import hashlib
import json
import os
import re
import shutil
import sys
import unicodedata
import uuid
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple


FORMAT_VERSION = 2
WINDOWS_RESERVED_NAMES = {
    "CON", "PRN", "AUX", "NUL",
    *(f"COM{i}" for i in range(1, 10)),
    *(f"LPT{i}" for i in range(1, 10)),
}
INVALID_FILE_CHARS = re.compile(r'[<>:"/\\|?*\x00-\x1f]')
WHITESPACE = re.compile(r"\s+")
SCRIPT_DIR = Path(__file__).resolve().parent
PROJECT_ROOT = SCRIPT_DIR.parent


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def safe_segment(value: str, fallback: str, max_length: int = 120) -> str:
    """Return a Windows-safe, readable path segment without losing the source name.

    The original name is always stored in the index and entry JSON.  This value
    is solely a filesystem key, so truncation never discards lookup data.
    """
    normalized = unicodedata.normalize("NFKC", value or "")
    normalized = INVALID_FILE_CHARS.sub("_", normalized)
    normalized = WHITESPACE.sub("_", normalized).strip(" ._")
    if not normalized:
        normalized = fallback
    if normalized.upper() in WINDOWS_RESERVED_NAMES:
        normalized = f"_{normalized}"
    return normalized[:max_length].rstrip(" .") or fallback


def require_file(path_text: str, label: str) -> Path:
    path = Path(path_text).expanduser().resolve()
    if not path.is_file():
        raise ValueError(f"{label} does not exist or is not a file: {path}")
    return path


def require_safe_output_directory(path_text: str, source: Path) -> Path:
    output = Path(path_text).expanduser().resolve()
    if output == Path(output.anchor):
        raise ValueError("Refusing to use a filesystem root as an output directory.")
    if output in {PROJECT_ROOT, source, source.parent}:
        raise ValueError(f"Refusing unsafe output directory: {output}")
    if len(output.parts) < 3:
        raise ValueError(f"Refusing an unusually shallow output directory: {output}")
    if output.exists() and not output.is_dir():
        raise ValueError(f"Output path exists but is not a directory: {output}")
    return output


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2, sort_keys=True)
        stream.write("\n")


def write_xml(path: Path, element: ET.Element) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tree = ET.ElementTree(copy.deepcopy(element))
    tree.write(path, encoding="utf-8", xml_declaration=True, short_empty_elements=False)


def replace_output(staging: Path, output: Path, keep_out: bool) -> Optional[Path]:
    """Install a fully written staging tree and restore the old tree on failure."""
    output.parent.mkdir(parents=True, exist_ok=True)
    backup: Optional[Path] = None
    if output.exists():
        timestamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        backup = output.with_name(f"{output.name}.backup-{timestamp}-{uuid.uuid4().hex[:8]}")
        output.rename(backup)

    try:
        staging.rename(output)
    except Exception:
        if backup is not None and backup.exists() and not output.exists():
            backup.rename(output)
        raise

    if backup is not None and not keep_out:
        shutil.rmtree(backup)
        backup = None
    return backup


def build_atomically(
    output: Path,
    keep_out: bool,
    writer: Callable[[Path], Dict[str, Any]],
) -> Tuple[Dict[str, Any], Optional[Path]]:
    staging = output.with_name(f".{output.name}.staging-{uuid.uuid4().hex}")
    try:
        staging.mkdir(parents=True, exist_ok=False)
        summary = writer(staging)
        backup = replace_output(staging, output, keep_out)
        return summary, backup
    except Exception:
        if staging.exists():
            shutil.rmtree(staging, ignore_errors=True)
        raise


def parse_native_name(name: Any, native_hash: str) -> str:
    if isinstance(name, str) and name.strip() and name.strip().lower() != native_hash.lower():
        return name.strip()
    return ""


def native_parser(source: Path, output: Path, keep_out: bool) -> Tuple[Dict[str, Any], Optional[Path]]:
    with source.open("r", encoding="utf-8-sig") as stream:
        raw = json.load(stream)
    if not isinstance(raw, dict):
        raise ValueError("NativeDB root must be a JSON object keyed by namespace.")

    source_hash = sha256_file(source)

    def write_tree(staging: Path) -> dict[str, Any]:
        records: Dict[str, Dict[str, Any]] = {}
        by_hash: Dict[str, List[str]] = {}
        by_qualified_name: Dict[str, List[str]] = {}
        namespace_counts: Dict[str, int] = {}
        sequence = 0

        for namespace, native_group in raw.items():
            if not isinstance(namespace, str):
                raise ValueError("A NativeDB namespace key is not a string.")
            if not isinstance(native_group, dict):
                raise ValueError(f"NativeDB namespace '{namespace}' is not an object.")

            namespace_segment = safe_segment(namespace, "UNNAMED_NAMESPACE")
            namespace_count = 0
            for native_hash, definition in native_group.items():
                if not isinstance(native_hash, str):
                    raise ValueError(f"A native hash in '{namespace}' is not a string.")
                if not isinstance(definition, dict):
                    raise ValueError(f"Native '{namespace}:{native_hash}' is not an object.")

                sequence += 1
                display_name = parse_native_name(definition.get("name"), native_hash)
                hash_suffix = hashlib.sha256(f"{namespace}\0{native_hash}".encode("utf-8")).hexdigest()[:12]
                name_segment = safe_segment(display_name or "UNNAMED", "UNNAMED")
                relative_path = Path("by_namespace") / namespace_segment / f"{name_segment}__{hash_suffix}.json"
                record_id = f"N{sequence:06d}"
                entry_document = {
                    "formatVersion": FORMAT_VERSION,
                    "hash": native_hash,
                    "namespace": namespace,
                    "name": display_name or None,
                    "native": definition,
                }
                write_json(staging / relative_path, entry_document)

                record = {
                    "hash": native_hash,
                    "namespace": namespace,
                    "name": display_name or None,
                    "path": relative_path.as_posix(),
                }
                records[record_id] = record
                by_hash.setdefault(native_hash, []).append(record_id)
                if display_name:
                    by_qualified_name.setdefault(f"{namespace}:{display_name}", []).append(record_id)
                namespace_count += 1
            namespace_counts[namespace] = namespace_count

        index = {
            "formatVersion": FORMAT_VERSION,
            "kind": "gta5-nativedb-legacy",
            "source": {"path": str(source), "sha256": source_hash},
            "counts": {"namespaces": len(namespace_counts), "natives": len(records)},
            "records": records,
            "byHash": by_hash,
            "byQualifiedName": by_qualified_name,
        }
        report = {
            "formatVersion": FORMAT_VERSION,
            "kind": "gta5-nativedb-legacy",
            "source": index["source"],
            "counts": index["counts"],
            "validation": {"status": "passed", "invalidRecords": 0},
        }
        write_json(staging / "index.json", index)
        write_json(staging / "parse-report.json", report)

        written_entries = list(staging.glob("by_namespace/**/*.json"))
        if len(written_entries) != len(records):
            raise RuntimeError("Native parse validation failed: entry file count does not match the index.")
        return report

    return build_atomically(output, keep_out, write_tree)


def member_details(canonical_name: str) -> Tuple[str, str, str, Optional[str]]:
    if len(canonical_name) >= 2 and canonical_name[1] == ":":
        kind = canonical_name[0]
        body = canonical_name[2:]
    else:
        kind = "X"
        body = canonical_name

    signature = None
    unqualified = body
    if kind == "M" and "(" in body:
        unqualified, signature = body.split("(", 1)
        signature = f"({signature}"
    member_name = unqualified.rsplit(".", 1)[-1] if unqualified else "member"
    return kind, body, member_name, signature


def local_xml_parser(source: Path, output: Path, keep_out: bool) -> Tuple[Dict[str, Any], Optional[Path]]:
    try:
        root = ET.parse(source).getroot()
    except ET.ParseError as error:
        raise ValueError(f"Invalid XML in {source}: {error}") from error
    if root.tag != "doc":
        raise ValueError(f"Expected <doc> as XML root, found <{root.tag}>.")
    assembly = root.find("assembly")
    members_parent = root.find("members")
    if assembly is None or members_parent is None:
        raise ValueError("The XML document must contain both <assembly> and <members>.")

    members = list(members_parent.findall("member"))
    source_hash = sha256_file(source)

    def write_tree(staging: Path) -> dict[str, Any]:
        write_xml(staging / "assembly.xml", assembly)
        records: Dict[str, Dict[str, Any]] = {}
        by_canonical_name: Dict[str, List[str]] = {}
        by_kind: Dict[str, List[str]] = {}

        for sequence, member in enumerate(members, start=1):
            canonical_name = member.get("name") or ""
            kind, body, member_name, signature = member_details(canonical_name)
            kind_segment = safe_segment(kind, "X", 8)
            readable_name = safe_segment(body.split("(", 1)[0], "member")
            stable_suffix = hashlib.sha256(canonical_name.encode("utf-8")).hexdigest()[:12]
            relative_path = Path("members") / kind_segment / f"{kind_segment}__{readable_name}__{stable_suffix}.xml"
            record_id = f"M{sequence:06d}"
            write_xml(staging / relative_path, member)

            record = {
                "canonicalName": canonical_name or None,
                "kind": kind,
                "qualifiedName": body or None,
                "memberName": member_name,
                "signature": signature,
                "path": relative_path.as_posix(),
            }
            records[record_id] = record
            by_canonical_name.setdefault(canonical_name, []).append(record_id)
            by_kind.setdefault(kind, []).append(record_id)

        index = {
            "formatVersion": FORMAT_VERSION,
            "kind": "scripthookvdotnet-xml",
            "source": {"path": str(source), "sha256": source_hash},
            "assemblyPath": "assembly.xml",
            "counts": {"members": len(records)},
            "records": records,
            "byCanonicalName": by_canonical_name,
            "byKind": by_kind,
        }
        report = {
            "formatVersion": FORMAT_VERSION,
            "kind": "scripthookvdotnet-xml",
            "source": index["source"],
            "counts": index["counts"],
            "validation": {"status": "passed", "unnamedMembers": sum(not item["canonicalName"] for item in records.values())},
        }
        write_json(staging / "index.json", index)
        write_json(staging / "parse-report.json", report)

        written_members = list(staging.glob("members/**/*.xml"))
        if len(written_members) != len(records):
            raise RuntimeError("XML parse validation failed: member file count does not match the index.")
        for record in records.values():
            ET.parse(staging / record["path"])
        return report

    return build_atomically(output, keep_out, write_tree)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    for command, help_text in (
        ("natives", "Parse NativeDB Legacy natives.json."),
        ("local-xml", "Parse a local ScriptHookVDotNet XML API document."),
    ):
        child = subparsers.add_parser(command, help=help_text)
        child.add_argument("--source", required=True, help="Source documentation file.")
        child.add_argument("--out-dir", required=True, help="Directory to replace with parsed output.")
        child.add_argument(
            "--keep-out",
            action="store_true",
            help="Keep the former output as a timestamped backup instead of deleting it after a successful refresh.",
        )
    return parser


def main() -> int:
    args = build_parser().parse_args()
    try:
        source = require_file(args.source, "Source file")
        output = require_safe_output_directory(args.out_dir, source)
        if args.command == "natives":
            report, backup = native_parser(source, output, args.keep_out)
        else:
            report, backup = local_xml_parser(source, output, args.keep_out)
    except (OSError, ValueError, RuntimeError, json.JSONDecodeError) as error:
        print(f"ERROR: {error}", file=sys.stderr)
        return 1

    counts = ", ".join(f"{key}={value}" for key, value in report["counts"].items())
    print(f"Parsed successfully: {counts}; validation={report['validation']['status']}; output={output}")
    if backup is not None:
        print(f"Previous output kept at: {backup}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
