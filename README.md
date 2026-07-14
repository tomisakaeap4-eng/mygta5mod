# FirstGtaMod

A C# mod for **Grand Theft Auto V Legacy (Story Mode)** built on top of
[ScriptHookVDotNet v3](https://github.com/scripthookvdotnet/scripthookvdotnet) (SHVDN)
on .NET Framework 4.8.

The project ships a minimal example mod (`main.cs` — the `FirstGtaMod` script
class) that spawns a Zentorno on F6, plus a toolchain of PowerShell and Bash
scripts that clone, maintain, and pre-parse the SHVDN / NativeDB / wiki corpus
under `api_docs/` and `local_api_docs/`.

## Requirements

- .NET Framework 4.8 (build) + GTA V Legacy (run) with
  [ScriptHookV](http://www.dev-c.com/gtav/scripthookv/) and
  [ScriptHookVDotNet v3](https://github.com/scripthookvdotnet/scripthookvdotnet) installed.
- For the scripts in `scripts/`:
  - **Windows**: PowerShell 5.1 or PowerShell 7+.
  - **Linux / WSL / macOS**: Bash 4+, `git`, `python3`.

## Project structure

```
.
├── main.cs                      # SHVDN entry class (FirstGtaMod)
├── Properties/AssemblyInfo.cs   # Assembly metadata
├── FirstGtaMod.csproj           # MSBuild project (Class Library, .NET 4.8)
├── FirstGtaMod.slnx             # Solution file
├── AGENTS.md                    # System prompt for the AI agent (read first)
├── scripts/                     # PowerShell + Bash utility scripts (see below)
├── api_docs/                    # Cloned corpus, gitignored (see below)
├── local_api_docs/              # Offline SHVDN XML doc + parsed tree (gitignored)
├── inputs/, output/             # Working dirs, created by bootstrap scripts
```

The compiled `FirstGtaMod.dll` is automatically copied to
`C:\Games\Grand Theft Auto V\scripts\` on a successful build (see
`PostBuildEvent` in `FirstGtaMod.csproj`; adjust the path if your install
lives elsewhere).

## Build

### Windows (Visual Studio / MSBuild)

```cmd
msbuild FirstGtaMod.csproj /p:Configuration=Release
```

Or open `FirstGtaMod.slnx` in Visual Studio 2019/2022 and press F6.

### Cross-platform note

`FirstGtaMod.csproj` is a legacy non-SDK-style csproj. Building on Linux/macOS
requires Mono or `dotnet msbuild`. End-users typically build on Windows where
the SHVDN runtime is available.

## Scripts

All scripts live in `scripts/` and follow the project's `set -euo pipefail` /
`$ErrorActionPreference = 'Stop'` strict-mode style. Each script auto-derives
its paths from `$PSScriptRoot` / `$(dirname "$0")/..` so they work no matter
where you invoke them from.

| First-time | Each time you need | Purpose |
| --- | --- | --- |
| `scripts/bootstrap_api_docs.ps1` | — | Clone shallow 3 repos into `api_docs/` (Windows PowerShell) |
| — | `scripts/update_api_docs.ps1` | `git pull --ff-only` repos in `api_docs/` (Windows PowerShell) |
| `scripts/bootstrap_api_docs.sh` | — | Bash equivalent of bootstrap (Linux / WSL / macOS) |
| — | `scripts/update_api_docs.sh` | Bash equivalent of update |
| — | `scripts/copy_gta_logs.ps1` | Copy `ScriptHookVDotNet.log` + `ScriptHookV.log` from GTA V install into `inputs/logs/` |
| — | `scripts/parse_natives.sh` | Tear apart `natives.json` (legacy) into `by_namespace/<NS>/<name>.json` + `index.json` |
| — | `scripts/parse_natives.ps1` | PowerShell equivalent |
| — | `scripts/parse_local_api_docs.sh` | Tear apart `ScriptHookVDotNet3.xml` into `by_namespace/<NS>/<Type>/<K>__<Member>.json` + `index.json` |
| — | `scripts/parse_local_api_docs.ps1` | PowerShell equivalent |

Run any script with `-h` / `--help` for usage. Most scripts support
`--source` / `-s` and `--out-dir` / `-o` overrides to point at custom
locations.

## Corpus: `api_docs/` and `local_api_docs/`

These directories are gitignored. Clone / populate them once after cloning
this repo, then re-run `update_*` whenever you want fresh docs.

```bash
# Linux / WSL / macOS
bash scripts/bootstrap_api_docs.sh
bash scripts/parse_natives.sh
bash scripts/parse_local_api_docs.sh
```

```powershell
# Windows PowerShell
pwsh -File scripts/bootstrap_api_docs.ps1
pwsh -File scripts/parse_natives.ps1
pwsh -File scripts/parse_local_api_docs.ps1
pwsh -File scripts/copy_gta_logs.ps1 -SourceDir "C:\Games\Grand Theft Auto V"
```

After bootstrap, the layout is:

```
api_docs/
├── scripthookvdotnet/          # SHVDN v3 source + XML API doc
├── scripthookvdotnet.wiki/     # SHVDN wiki (tutorials, examples)
├── gta5-nativedb-data/         # NativeDB Legacy
│   ├── natives.json            # Aggregated legacy natives (2.6 MB)
│   ├── natives_gen9.json       # gen9 variant (2.6 MB)
│   ├── schema.json
│   └── natives_parsed/         # Produced by parse_natives
│       ├── index.json          # hash -> relative path entries
│       └── by_namespace/<NS>/<name>.json
└── ...

local_api_docs/
├── ScriptHookVDotNet3.xml      # Offline SHVDN v3 .NET XML doc
└── parsed/                     # Produced by parse_local_api_docs
    ├── index.json              # member-name -> relative path entries
    └── by_namespace/<NS>/<Type>/<K>__<Member>.json
```

## Agent workflow

The agent's system prompt lives in [`AGENTS.md`](AGENTS.md). It tells the agent:

- What `api_docs/` and `local_api_docs/` contain.
- Which file (class / member / native) to look up first, in which priority order.
- The mandatory pre-code workflow (read project + corpus, identify SHVDN
  version, search by namespace / member / hash, fix runtime errors via the
  log files).
- The expected response shape (`Assumptions` / `Files changed` / `Why this
  API is valid` / `Build and test` / `Failure checks`).

If `api_docs/` is missing or empty, the agent must stop and ask the user to
run `bootstrap_api_docs` (and the parse scripts) before continuing.

## License

TBD.
