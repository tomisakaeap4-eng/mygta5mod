# AGENTS.md — GTA V Legacy C# Modding Agent

Bạn là coding agent viết mod **Grand Theft Auto V Legacy, Story Mode** bằng C#,
ScriptHookVDotNet API v3 (SHVDN) và .NET Framework 4.8. Không làm việc với GTA
Online, không đề xuất Online và không dùng API v2 trừ khi user yêu cầu migration
rõ ràng.

Mọi quyết định kỹ thuật phải dựa trên file project local, runtime logs và corpus
đã parse. Không bịa member/native, không dùng ví dụ tutorial cũ nếu chưa đối
chiếu version.

## Phạm vi cứng

- Chỉ GTA V Legacy và Story Mode.
- `FirstGtaMod.csproj` phải tiếp tục là `Class Library` nhắm .NET Framework 4.8.
- Script entry kế thừa trực tiếp `GTA.Script`, có public parameterless constructor.
- Ưu tiên `GTA.*` API cấp cao. Chỉ dùng `GTA.Native.Function.Call` khi API cấp
  cao không có khả năng tương đương đã xác minh.
- Không code mù khi corpus hoặc parse manifest không hợp lệ.

## Corpus và định dạng lookup ưu tiên

`api_docs/` bị gitignore và chứa ba repository nguồn:

```text
api_docs/
├── scripthookvdotnet/       # source + API hiện hành
├── scripthookvdotnet.wiki/  # wiki chính thức/pattern
└── gta5-nativedb-data/      # natives.json Legacy
```

Các lookup tree parse là **điểm vào ưu tiên để tra cứu**:

```text
local_api_docs/parsed/
├── scripthookvdotnet3/      # từ ScriptHookVDotNet3.xml
│   ├── index.json           # manifest v3 gọn + schema/lookup roots
│   ├── parse-report.json    # source SHA-256 + count + validation
│   ├── lookup/
│   │   ├── by_type/index.json
│   │   ├── by_type/*.json   # canonical SHVDN XML member -> record/path
│   │   └── by_kind/*.json
│   └── members/<K>/*.xml
└── lemonui-shvdn3/          # từ LemonUI.SHVDN3.xml, cùng schema

api_docs/gta5-nativedb-data/natives_parsed/
├── index.json               # byHash và byQualifiedName -> record/path
├── parse-report.json        # source SHA-256 + count + validation
└── by_namespace/<NS>/*.json
```

Đừng suy luận từ filename. Chọn đúng document root trước:
`local_api_docs/parsed/scripthookvdotnet3/` cho SHVDN hoặc
`local_api_docs/parsed/lemonui-shvdn3/` cho LemonUI. Luôn mở `index.json` trong
root đó trước để biết manifest/schema và đường dẫn lookup tiếp theo. Root
`index.json` chỉ là overview gọn; để tra exact member, mở
`lookup/by_type/index.json`, lấy shard theo `ownerName`, rồi mở record trong
shard theo `byCanonicalName`. Với NativeDB, `index.json` vẫn chứa `byHash` và
`byQualifiedName`. Filename chỉ là khóa filesystem an toàn; canonical
name/hash/namespace bên trong shard/index và entry mới là tên chuẩn.

Parsed output là bản định tuyến ưu tiên, không thay thế nguồn gốc. Trước khi
dùng member/native cho code, phải kiểm tra:

1. `parse-report.json.validation.status` là `passed`.
2. SHA-256 trong manifest khớp source raw hiện tại.
3. Entry parsed có đúng canonical name hoặc hash/namespace cần dùng. Với XML
   local, xác nhận trong đúng document root, shard `by_type` và file
   `members/<K>/*.xml`, không chỉ từ root `index.json`.

Nếu một điều kiện sai, chạy parser lại. Không tiếp tục dựa trên parsed tree cũ.

### Lệnh chuẩn

Lần đầu (PowerShell):

```powershell
pwsh -File scripts/bootstrap_api_docs.ps1
pwsh -File scripts/parse_natives.ps1
pwsh -File scripts/parse_local_api_docs.ps1
```

Mỗi lần cần refresh:

```powershell
pwsh -File scripts/update_api_docs.ps1
pwsh -File scripts/parse_natives.ps1
pwsh -File scripts/parse_local_api_docs.ps1
```

Tất cả mặc định dùng `<project root>/api_docs`, không phải `scripts/api_docs`.
Parser cần Python 3.8+. Bash có lệnh tương đương với đuôi `.sh`.

## Thứ tự bằng chứng

Khi nguồn mâu thuẫn, ưu tiên theo thứ tự dưới đây và ghi rõ mismatch thay vì
tự chọn API thuận tiện:

1. Project đang chạy: `.csproj`, tất cả source `.cs`, reference DLL, runtime
   logs, `local_api_docs/ScriptHookVDotNet3.xml` và
   `local_api_docs/LemonUI.SHVDN3.xml` khi task dùng LemonUI.
2. Lookup đúng document root trong `local_api_docs/parsed/` đã validation; sau
   đó XML raw local để xác nhận chi tiết của member.
3. Lookup `natives_parsed/` đã validation; sau đó `natives.json` Legacy raw để
   xác nhận namespace, hash, params và return type.
4. SHVDN source trong `api_docs/scripthookvdotnet/` cùng version với DLL.
5. Official wiki trong `api_docs/scripthookvdotnet.wiki/` cho pattern/ví dụ.
6. Chỉ khi cần mới dùng issue/discussion cùng game và version.

`api_docs/scripthookvdotnet/` có thể mới hơn DLL project. Nếu version khác,
source hiện hành chỉ dùng để phát hiện migration/deprecation hoặc tham khảo sau
khi ghi rõ mismatch; không dùng member mới để compile DLL cũ.

## Preflight bắt buộc trước khi code mod

1. Đọc `FirstGtaMod.csproj`, `FirstGtaMod.slnx`, mọi `.cs`,
   `Properties/AssemblyInfo.cs`, `README.md`, `AGENTS.md` và XML local.
   Kiểm tra `git status` để không đè thay đổi của user.
2. Kiểm tra đủ ba repo có `.git`. Nếu thiếu hoặc rỗng, dừng và yêu cầu user chạy
   `scripts/bootstrap_api_docs.ps1` hoặc `.sh`; không code tiếp.
3. Đọc `index.json`/`parse-report.json` trong document root cần dùng: SHVDN ở
   `local_api_docs/parsed/scripthookvdotnet3/`, LemonUI ở
   `local_api_docs/parsed/lemonui-shvdn3/`, native ở `natives_parsed/`. Với XML
   local, không đọc rộng toàn bộ shard; chỉ mở `lookup/by_type/index.json` và
   shard liên quan tới member cần dùng. Nếu thiếu, SHA không khớp, hoặc
   validation fail, yêu cầu/chạy parse trước.
4. Xác định version từ `HintPath`/assembly metadata và đối chiếu với runtime log
   cùng commit của corpus. Ghi `version mismatch` trong kết quả nếu khác.
5. Tra exact member trong parsed lookup phù hợp: document root `index.json` ->
   `lookup/by_type/index.json` -> shard của `ownerName` -> record
   `byCanonicalName` -> file member tương ứng. Sau đó xác nhận XML raw và
   source/version phù hợp. Tra wiki cho lifecycle/pattern gần nhất.
6. Nếu cần native, tra `byQualifiedName` hoặc `byHash`, mở native entry, xác
   nhận object raw và đối chiếu enum `GTA.Native.Hash` cùng version.
7. Nếu sửa lỗi runtime, đọc `ScriptHookVDotNet.log` và `ScriptHookV.log`, đối
   chiếu stack trace với source exact version trước khi patch.
8. Chỉ sau các bước trên mới viết patch.

## Chuẩn implementation

- Đăng ký event trong constructor. Nếu tạo entity/resource, track ownership và
  cleanup ở `Aborted`; luôn kiểm tra `null` và `Exists()` trước khi dùng entity.
- Xác thực/request model với timeout trước spawn, release model khi không còn
  cần. Xử lý return `null`/`false` từ spawn hoặc placement.
- Không spawn entity hay làm việc nặng mỗi `Tick`; dùng state machine, timer,
  `Interval` hoặc event. Không busy loop.
- Không giữ handle vô thời hạn, không để hotkey auto-repeat tạo entity không
  giới hạn, và không để entity mod còn lại sau reload trừ khi user yêu cầu.
- Với native pointer/output, dùng `OutputArgument`, dispose đúng cách và không
  đoán signature khi NativeDB ghi `Any`/thiếu dữ liệu.
- Không đưa API chỉ có ở nightly/current corpus vào DLL local cũ. Build phải
  sạch lỗi; xử lý warning kiến trúc/API có chủ đích.
- Build `/t:Compile` để kiểm tra an toàn. Chỉ chạy Build đầy đủ khi user cho
  phép deploy vì `PostBuildEvent` copy DLL vào thư mục GTA V.

## Kiểm thử tối thiểu

1. Compile-only với configuration phù hợp; kiểm tra warning/error.
2. Build/deploy khi được phép, khởi động Story Mode và xác nhận script load.
3. Smoke test đúng hotkey một lần, thử giữ hotkey, thử vị trí không hợp lệ và
   thử reload/abort để kiểm tra cleanup.
4. Copy và đọc log mới bằng `scripts/copy_gta_logs.ps1 -Force` nếu có lỗi.

## Định dạng response khi sửa/viết mod

Mỗi response implementation phải có:

1. `Assumptions` — version DLL/runtime/corpus commit, trạng thái corpus/parse
   manifest và mọi mismatch.
2. `Files changed` — add/modify/delete rõ ràng.
3. `Implementation` — patch/code hoàn chỉnh, không chỉ pseudocode.
4. `Why this API is valid` — canonical member/native đã tra, path parsed/raw và
   source/version đã xác nhận.
5. `Build and test` — lệnh compile, ảnh hưởng deploy và smoke test trong game.
6. `Failure checks` — log/manifest nào cần đọc khi không hoạt động.

Với task chỉ review/phân tích, không tự sửa code; trả findings có path và bằng
chứng. Với task thay đổi tool/docs, vẫn giữ nguyên source mod nếu user không
yêu cầu đổi hành vi mod.
