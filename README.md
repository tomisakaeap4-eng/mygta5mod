# FirstGtaMod

Mod C# tối giản cho **Grand Theft Auto V Legacy, Story Mode**, dùng
ScriptHookVDotNet v3 (SHVDN) và .NET Framework 4.8. `main.cs` hiện đăng ký F6
để tạo Zentorno. Dự án đi kèm một corpus tài liệu cục bộ và bộ công cụ tạo
lookup tree đã kiểm chứng, để mọi thay đổi mod đều bám vào API thực tế.

> Không dùng dự án, ScriptHookVDotNet hoặc quy trình này cho GTA Online.

## Trạng thái kỹ thuật hiện tại

- `FirstGtaMod.csproj` là **Class Library**, nhắm `.NET Framework 4.8`.
- Reference hiện tại là `..\ScriptHookVDotNet\ScriptHookVDotNet3.dll`.
  Trên máy này DLL có version `3.6.0.0`; runtime trong log đang là `3.7.0` và
  đang nạp mod tương thích. Khi nâng DLL, hãy re-parse tài liệu, build lại và
  smoke test trước khi dùng API mới.
- GTA V là tiến trình x64. Project đang là `AnyCPU`; nên đổi cấu hình build
  sang `x64` trước khi phát hành để loại bỏ cảnh báo MSBuild về SHVDN AMD64.

## Yêu cầu

- GTA V Legacy, Story Mode, ScriptHookV và ScriptHookVDotNet v3.
- .NET Framework 4.8 và MSBuild/Visual Studio để build mod.
- Git để bootstrap hoặc cập nhật corpus.
- Python 3.8+ để chạy hai parser (cả PowerShell lẫn Bash gọi chung một parser
  chuẩn thư viện).
- PowerShell 5.1+ trên Windows; hoặc Bash 4+ trên Linux/WSL/macOS.

Đặt DLL SHVDN mà project tham chiếu tại
`..\ScriptHookVDotNet\ScriptHookVDotNet3.dll`, hoặc chủ động sửa `HintPath`
trong `FirstGtaMod.csproj` để phù hợp máy của bạn. DLL runtime phải nằm đúng
vị trí ScriptHookVDotNet yêu cầu trong thư mục GTA V.

## Cấu trúc dự án

```text
.
├── main.cs                         # Script entry class kế thừa GTA.Script
├── FirstGtaMod.csproj              # Library .NET Framework 4.8 + deploy path
├── FirstGtaMod.slnx                # Solution
├── Properties/AssemblyInfo.cs       # Assembly metadata
├── AGENTS.md                       # Quy trình bắt buộc cho coding agent
├── scripts/                         # Bootstrap, update, parse, copy logs
├── local_api_docs/                  # XML API của DLL SHVDN đang reference
│   ├── ScriptHookVDotNet3.xml
│   └── parsed/                      # Lookup tree sinh bởi parser, gitignored
├── api_docs/                        # 3 repository nguồn, gitignored
│   ├── scripthookvdotnet/
│   ├── scripthookvdotnet.wiki/
│   └── gta5-nativedb-data/
└── logs/                            # Bản copy runtime logs, gitignored
```

## Khởi tạo và cập nhật corpus

Tất cả script mặc định ghi vào **`<project root>/api_docs`**; không ghi vào
`scripts/api_docs`.

### Windows PowerShell

```powershell
pwsh -File scripts/bootstrap_api_docs.ps1
pwsh -File scripts/parse_natives.ps1
pwsh -File scripts/parse_local_api_docs.ps1
```

Sau lần đầu, chỉ cần cập nhật repository và parse lại dữ liệu đã thay đổi:

```powershell
pwsh -File scripts/update_api_docs.ps1
pwsh -File scripts/parse_natives.ps1
pwsh -File scripts/parse_local_api_docs.ps1
```

Tham số PowerShell dùng một dấu gạch: `-ApiDocsRoot`, `-Source`, `-OutDir`,
`-KeepOut`. `-d`, `-s`, `-o` là alias ngắn. `-KeepOut` giữ output cũ thành
thư mục backup có timestamp sau khi output mới đã parse thành công.

### Bash / WSL / macOS

```bash
bash scripts/bootstrap_api_docs.sh
bash scripts/parse_natives.sh
bash scripts/parse_local_api_docs.sh
```

```bash
bash scripts/update_api_docs.sh
bash scripts/parse_natives.sh
bash scripts/parse_local_api_docs.sh
```

Script Bash hỗ trợ `-d/--dir`, `-s/--source`, `-o/--out-dir` và
`-k/--keep-out`. Parser dùng Python 3 ở cả hai nền tảng để schema giống hệt
nhau.

## Dữ liệu đã parse

Không suy luận API từ tên file. Luôn mở `index.json` trước, sau đó mở entry mà
index trỏ tới.

### NativeDB Legacy

`api_docs/gta5-nativedb-data/natives_parsed/` gồm:

```text
index.json                 # manifest v2 + records + byHash + byQualifiedName
parse-report.json          # source SHA-256, count và kết quả validation
by_namespace/<NS>/*.json   # một native đầy đủ, tên file ổn định/chống đụng độ
```

Mỗi entry lưu nguyên object NativeDB trong trường `native`, cùng `hash`,
`namespace` và `name`. `index.json` giữ tên gốc và đường dẫn đầy đủ, nên việc
sanitize tên file không thể làm mất thông tin hay làm đụng overload/hash.

### XML API của DLL local

`local_api_docs/parsed/` gồm:

```text
assembly.xml                 # phần <assembly> của XML gốc
index.json                   # manifest v2, lookup canonical name và kind
parse-report.json            # source SHA-256, count và kết quả validation
members/<K>/*.xml            # một <member> hợp lệ cho mỗi XML member
```

`index.json` lưu `canonicalName`, `qualifiedName`, `memberName`, `signature`,
`kind` và `path`. Tên file mang namespace/type và SHA ngắn để dễ đọc mà vẫn
duy nhất; canonical name trong index mới là định danh chuẩn.

Parser tạo output ở staging, kiểm tra số file/parse lại XML rồi mới thay output
đang dùng. Khi `parse-report.json.validation.status` không phải `passed`, hoặc
SHA-256 nguồn khác file raw hiện tại, hãy parse lại trước khi code.

## Build và deploy

Chỉ compile, không chạy post-build copy vào GTA V:

```cmd
msbuild FirstGtaMod.csproj /t:Compile /p:Configuration=Debug
```

Build đầy đủ sẽ chạy `PostBuildEvent` và copy DLL vào
`C:\Games\Grand Theft Auto V\scripts\`:

```cmd
msbuild FirstGtaMod.csproj /p:Configuration=Release
```

Sửa đường dẫn trong `FirstGtaMod.csproj` trước khi build đầy đủ nếu GTA V nằm
ở nơi khác. Build thành công không thay thế smoke test trong game.

## Quy trình phát triển mod

1. Đọc `AGENTS.md`, `FirstGtaMod.csproj`, toàn bộ `.cs`, `README.md` và
   `local_api_docs/ScriptHookVDotNet3.xml`.
2. Kiểm tra ba repository corpus và hai `parse-report.json`. Nếu SHA hoặc count
   không khớp, chạy update/bootstrap rồi parse lại.
3. Tra `local_api_docs/parsed/index.json` trước. Xác nhận entry bằng XML raw,
   source SHVDN cùng version và wiki khi cần pattern sử dụng.
4. Với native, tra `natives_parsed/index.json` trước, xác nhận object raw trong
   `natives.json`, rồi đối chiếu `GTA.Native.Hash` cùng version.
5. Sửa code theo lifecycle của SHVDN: event trong constructor, entity do mod tạo
   phải được track/cleanup ở `Aborted`, model phải có timeout và được release,
   không spawn liên tục trong `Tick`.
6. Chạy compile-only, sau đó smoke test trong Story Mode. Nhấn đúng hotkey một
   lần, thử reload script và kiểm tra entity cleanup/error handling.
7. Copy log mới để điều tra lỗi runtime:

   ```powershell
   pwsh -File scripts/copy_gta_logs.ps1 -Force
   ```

   Đọc cả `logs/ScriptHookVDotNet.log` và `logs/ScriptHookV.log`, cũng như log
   gốc trong `%LOCALAPPDATA%\ScriptHookVDotNet` khi có.

`AGENTS.md` là quy trình chi tiết và bắt buộc cho mọi thay đổi mod.

## License

TBD.
