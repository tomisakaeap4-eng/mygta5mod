# RAG ingestion and retrieval rules

## 1. Không chunk ngẫu nhiên mọi nguồn giống nhau

### C# source
- Ưu tiên chunk theo file và symbol.
- File ngắn: giữ nguyên file.
- File dài: chia theo vùng dòng, có overlap.
- Metadata:
  - repository
  - commit
  - relative_path
  - namespace
  - declared_symbols
  - api_version
  - source_kind

### XML API
- Một record cho mỗi `<member>`.
- Giữ nguyên member name, summary, params, returns, remarks, exceptions.
- Metadata:
  - assembly version nếu xác định được
  - member id
  - type/member

### Wiki/Markdown
- Chunk theo heading.
- Giữ heading chain trong nội dung và metadata.

### NativeDB Legacy
- Một record cho mỗi native.
- Metadata:
  - namespace
  - name
  - hash
  - jhash
  - params
  - return_type
  - build information nếu có
- Không trộn `natives_gen9.json`.

### Project/log
- Ưu tiên cao nhất.
- Log chunk theo exception hoặc phiên chạy.
- Giữ timestamp và stack trace.

## 2. Retrieval strategy

Một yêu cầu như “spawn xe và đưa player vào ghế lái” nên tạo nhiều truy vấn:

- `World.CreateVehicle overload Model Vector3 heading`
- `Ped.SetIntoVehicle VehicleSeat.Driver`
- `Model Request IsLoaded MarkAsNoLongerNeeded`
- examples spawn vehicle
- nếu wrapper thiếu: native `SET_PED_INTO_VEHICLE`

Dùng hybrid retrieval:
- exact symbol/BM25 cho class/member/native
- semantic retrieval cho ý định
- metadata filter `game=legacy`, `api=v3`

## 3. Context packing

Một context tốt cho agent nên chứa:

1. Chữ ký API/XML member
2. Source implementation hoặc class context
3. Ví dụ chính thức liên quan
4. Project code đang sửa
5. Native record nếu thật sự cần

Không nhét toàn bộ `Vehicle.cs`, toàn wiki và toàn NativeDB vào một prompt.

## 4. Version discipline

Lưu commit SHA của từng repository trong `source_versions.json`.
Khi agent trả code, nó phải nói API được xác nhận theo corpus version nào.
Sau khi cập nhật repo, build lại corpus và invalid cache embeddings cũ.
