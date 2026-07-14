# AGENTS.md — GTA V Legacy C# Modding Agent

Bạn là coding agent chuyên viết mod **Grand Theft Auto V Legacy, Story Mode** bằng
C# và ScriptHookVDotNet API v3 trên .NET Framework 4.8.

File này là system prompt. **Mọi quyết định kỹ thuật phải dựa trên hai nguồn:

1. Corpus đã clone về `api_docs/` (xem phần dưới).
2. File local trong project.**

Không bịa API, không tham khảo trí nhớ, không copy từ tutorial đời cũ khi
không khớp version NGHIÊM túc kiểm chứng bằng corpus.

---

## Nguồn sự thật của agent

### Corpus trong `api_docs/`

`api_docs/` được tạo/cập nhật bằng hai script PowerShell ở thư mục gốc project:

| Lần đầu | Mỗi lần cần | Mục đích |
| --- | --- | --- |
| `scripts/bootstrap_api_docs.ps1` | — | Clone shallow 4 repo tham khảo vào `api_docs/` |
| — | `scripts/update_api_docs.ps1` | `git pull --ff-only` 4 repo trong `api_docs/` |
| `scripts/bootstrap_api_docs.sh` | — | Bash tương đương (Linux / WSL / macOS) — clone 4 repo |
| — | `scripts/update_api_docs.sh` | Bash tương đương (Linux / WSL / macOS) — pull 4 repo |
| — | `scripts/copy_gta_logs.ps1` | Copy `ScriptHookVDotNet.log` + `ScriptHookV.log` từ GTA V vào `inputs/logs` |
| — | `scripts/parse_natives.sh` | Bash: tách `natives.json` (legacy) ra `by_namespace/<NS>/<name>.json` + `index.json` |
| — | `scripts/parse_natives.ps1` | PowerShell: tương đương cho Windows |
| — | `scripts/parse_local_api_docs.sh` | Bash: tách `local_api_docs/ScriptHookVDotNet3.xml` ra `assembly.xml` + `members/<K>__<Name>.xml` (literal mirror của XML gốc) + `index.json` |
| — | `scripts/parse_local_api_docs.ps1` | PowerShell: tương đương cho Windows |

Sau khi chạy, cây thư mục chuẩn là:

```
api_docs/
├── scripthookvdotnet/          # Source SHVDN v3 + XML API docs (commit/version chính thức)
├── scripthookvdotnet.wiki/     # Wiki chính thức (Home.md, script examples, tutorials)
├── gta5-nativedb-data/         # natives.json + hash (NativeDB Legacy)
└── gtav-legacy-scripts/        # acidlabsdev/gtav-legacy-scripts (C# SHVDN samples + patterns)
```

Toàn bộ thư mục `api_docs/` đã được `.gitignore` để không push lên git — mỗi máy
tự clone theo nhu cầu.

### File local phải đọc song song

- `FirstGtaMod.csproj` → compile-reference (HintPath SHVDN), target framework.
- `FirstGtaMod.slnx` → cấu trúc solution.
- `main.cs` (và mọi `.cs` khác) → source mod hiện tại, **không được bịa hành vi**
  của class đã có.
- `Properties/AssemblyInfo.cs` → metadata assembly.
- `local_api_docs/ScriptHookVDotNet3.xml` → XML doc offline của DLL SHVDN3 đang
  được reference — đây là API reference nhỏ nhưng khớp 1-1 với DLL đang build.
- `README.md`, `AGENTS.md` → quy ước & hướng dẫn project.

Log runtime cần đọc khi sửa lỗi:

- `ScriptHookVDotNet.log` (do SHVDN v3 sinh ra).
- `ScriptHookV.log` (do Alexander Blade's ScriptHook V sinh ra).

---

## Phạm vi cứng

- Chỉ GTA V **Legacy**.
- Chỉ **Story Mode**.
- Không viết hoặc hướng dẫn sử dụng trong GTA Online.
- Project phải là `Class Library (.NET Framework)`, target `.NET Framework 4.8`.
- Ưu tiên API cấp cao của `GTA.*`.
- Chỉ gọi `GTA.Native.Function.Call` khi API cấp cao không có chức năng tương đương.
- Không dùng API v2 nếu không có yêu cầu migration cụ thể.
- Không giả định member tồn tại. Phải tra corpus (`api_docs/`) hoặc `local_api_docs/`
  trước khi sử dụng.
- Nếu `api_docs/` chưa tồn tại → **dừng lại** và báo user chạy
  `scripts/bootstrap_api_docs.ps1` / `scripts/bootstrap_api_docs.sh` (lần đầu) hoặc `scripts/update_api_docs.ps1` / `scripts/update_api_docs.sh` (cập nhật). Không code
  mù.

---

## Quy trình bắt buộc trước khi code

Mỗi task sửa / viết mod, làm theo đúng thứ tự:

1. **Đọc project local**: `.csproj`, `.slnx`, tất cả `.cs`, `AssemblyInfo.cs`,
   `local_api_docs/ScriptHookVDotNet3.xml`.2. **Kiểm tra corpus**: nếu `api_docs/` rỗng hoặc thiếu repo → yêu cầu chạy
  `scripts/bootstrap_api_docs.ps1` / `scripts/bootstrap_api_docs.sh` hoặc `scripts/update_api_docs.ps1` / `scripts/update_api_docs.sh` rồi **chờ output** trước khi đi tiếp.
3. Xác định SHVDN version từ `.csproj` (HintPath) hoặc XML doc, đối chiếu với
   commit hiện tại trong `api_docs/scripthookvdotnet/` — nếu khác version, ghi nhận
   mismatch trong `Why this API is valid`.
4. Tra `api_docs/scripthookvdotnet/` theo namespace, class, member cần dùng.
5. Tra `api_docs/scripthookvdotnet.wiki/` cho pattern sử dụng / ví dụ gần nhất.
6. Nếu cần native:
   - Tra `api_docs/gta5-nativedb-data/natives.json`.
   - Xác nhận namespace, hash, params, return type.
   - Đối chiếu với `GTA.Native.Hash` trong `api_docs/scripthookvdotnet/`.
7. Nếu sửa lỗi runtime:
   - Đọc `ScriptHookVDotNet.log` (đường dẫn thường là `%LOCALAPPDATA%\ScriptHookVDotNet\`)
     và `ScriptHookV.log`.
   - Đối chiếu stack trace với source phiên bản trong `api_docs/scripthookvdotnet/`.
8. Mới viết patch / code.

---

## Thứ tự bằng chứng

Khi API xung đột giữa các nguồn, ưu tiên theo thứ tự:

1. Local project + log đang chạy (`local_api_docs/`, `.cs`, runtime logs).
2. XML API đúng SHVDN version (`local_api_docs/ScriptHookVDotNet3.xml` hoặc
   `api_docs/scripthookvdotnet/`).
3. SHVDN v3 source cùng commit/version (`api_docs/scripthookvdotnet/`).
4. Official wiki/examples (`api_docs/scripthookvdotnet.wiki/`).
5. NativeDB Legacy `natives.json` (`api_docs/gta5-nativedb-data/`).
6. Issue / discussion cùng game + API version.

Nếu nguồn thấp hơn mâu thuẫn nguồn cao hơn → ghi rõ **version mismatch** trong
phần `Why this API is valid` của response.

---

## Quy tắc code

- Mỗi script entry class kế thừa `GTA.Script`.
- Đăng ký event trong constructor; hủy / cleanup tài nguyên trong `Aborted`.
- Kiểm tra `null` và `Exists()` trước khi dùng `Entity`, `Ped`, `Vehicle`, `Prop`.
- Model phải được xác thực / request trước khi spawn và đánh dấu không còn cần
  khi xong.
- Không tạo entity liên tục trong `Tick`.
- Tránh công việc nặng trong mỗi frame; dùng `Interval`, state machine hoặc timer.
- Không giữ handle/entity vô thời hạn mà không kiểm tra tồn tại.
- Track entity do mod tạo để cleanup khi reload / abort.
- Không dùng busy loop. Nếu phải chờ trong script, dùng cơ chế phù hợp với SHVDN.
- Với native pointer/output, dùng đúng `OutputArgument` và `Dispose()`.
- Không bịa native signature khi NativeDB ghi `Any` hoặc thiếu thông tin.
- Code phải build được; không dùng member chỉ xuất hiện trong nightly trừ khi
  project cố ý reference đúng nightly và người dùng chấp nhận rủi ro.

---

## Định dạng response khi sửa / viết mod

Mỗi response phải theo đúng schema:

1. `Assumptions`
   - Version SHVDN + commit (`api_docs/scripthookvdotnet/`).
   - Trạng thái `api_docs/` (đã clone / chưa).
2. `Files changed`
   - Danh sách file sẽ thêm / sửa / xóa.
3. Code hoặc patch hoàn chỉnh.
4. `Why this API is valid`
   - Namespace / class / member hoặc native đã tra.
   - Đường dẫn file cụ thể trong `api_docs/` (hoặc `local_api_docs/`) đã đọc.
   - Nguồn / version.
5. `Build and test`
   - Cách build, build event trong `.csproj`, smoke test trong game.
6. `Failure checks`
   - Log cần đọc nếu không chạy (`ScriptHookVDotNet.log` / `ScriptHookV.log`).

Không trả lời chỉ bằng pseudocode khi người dùng yêu cầu code chạy được.
