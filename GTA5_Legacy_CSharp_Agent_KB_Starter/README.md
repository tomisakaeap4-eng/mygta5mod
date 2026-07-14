# GTA V Legacy C# Agent Knowledge Base Starter

Mục tiêu: tạo một knowledge base cục bộ để LLM Agent có thể tra cứu và viết mod
GTA V **Legacy / Story Mode** bằng C# + ScriptHookVDotNet v3.

Bộ starter này không chứa lại toàn bộ nội dung của các dự án bên thứ ba. Thay vào đó,
nó cung cấp script để clone nguồn chính thức/canonical về máy, lọc đúng phần Legacy,
và chuyển chúng thành `corpus.jsonl` để nạp vào RAG, vector store hoặc công cụ tìm kiếm
của agent.

## Nguồn được dùng

1. ScriptHookVDotNet repository
   - API implementation: `source/scripting_v3/GTA`
   - Runtime behavior: `source/core`
   - Examples: `examples`
   - Configuration and README

2. ScriptHookVDotNet Wiki
   - Getting Started
   - User Guides
   - How Tos
   - Migration Guide
   - Code snippets

3. alloc8or GTA V NativeDB data
   - **Legacy only:** `natives.json`
   - Schema: `schema.json`
   - Không ingest `natives_gen9.json` vì đó là Gen9/Enhanced.

4. Tài liệu XML của đúng DLL compile reference
   - Đặt `ScriptHookVDotNet3.xml` cạnh `ScriptHookVDotNet3.dll`
   - Sau đó copy XML vào `inputs/local_api_docs/`

5. Project thực tế của bạn
   - `.cs`, `.csproj`, `.sln`, `.config`, `.ini`
   - Log: `ScriptHookVDotNet.log`, `ScriptHookV.log`
   - Copy vào `inputs/project/` và `inputs/logs/`

## Chạy nhanh

Mở PowerShell tại thư mục này:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\bootstrap_kb.ps1
python .\build_corpus.py
```

Kết quả:

```text
output/
├── corpus.jsonl
├── corpus_manifest.json
└── source_versions.json
```

## Cập nhật tài liệu

```powershell
.\update_kb.ps1
python .\build_corpus.py
```

## Nạp vào agent

Agent cần có ít nhất hai tool:

- `search_kb(query, filters, top_k)`
- `read_chunk(chunk_id)` hoặc đọc trực tiếp record JSONL

Thứ tự ưu tiên nguồn:

1. Project hiện tại và log hiện tại
2. XML API của DLL compile reference
3. SHVDN v3 source
4. SHVDN examples/wiki
5. NativeDB Legacy
6. Issues/discussions chỉ khi điều tra lỗi cụ thể

Đừng đưa toàn bộ corpus vào một prompt. Hãy dùng retrieval theo yêu cầu.
