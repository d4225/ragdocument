# 📚 HỆ THỐNG TRỢ LÝ TRA CỨU TÀI LIỆU THÔNG MINH (RAG DOCUMENT Q&A)

> **Công nghệ cốt lõi:** .NET 10 Web API + React 19 (TypeScript, TailwindCSS) + PostgreSQL 16 (`pgvector`) + Hangfire + PdfPig

Hệ thống tra cứu và hỏi đáp tài liệu nội bộ (PDF) sử dụng kỹ thuật tiên tiến **Retrieval-Augmented Generation (RAG)** kết hợp **Parent-Child Chunking**, **Vector Search Cosine Distance**, **Parent Retrieval** và giao diện **Split-View** trực quan hỗ trợ nhảy trực tiếp tới đúng số trang PDF được trích dẫn.

---

## 🎯 1. TỔNG HỢP NHỮNG GÌ ĐÃ HOÀN THÀNH (100%)

Dự án đã được thiết kế, tối ưu và lập trình hoàn chỉnh với các hạng mục sau:

### ✅ 1. Công cụ & Môi trường phát triển trên máy
- [x] Kiểm tra và tích hợp **.NET 10 SDK** (`10.0.401`).
- [x] Tự động cài đặt **Entity Framework Core CLI** (`dotnet-ef 10.0.12`).
- [x] Tự động cài đặt **Node.js LTS** (`v24.19.0`) & **npm** (`11.17.0`), cấp quyền thực thi script PowerShell (`RemoteSigned`).
- [x] Tự động tải sẵn bộ cài đặt **Docker Desktop** (`D:\DALN\Docker Desktop_4.93.0_Machine_X64_exe_en-US.exe`).
- [x] Tạo file cấu hình [docker-compose.yml](file:///D:/DALN/ragdocument/docker-compose.yml) gồm **PostgreSQL 16 pgvector** (`port 5432`) và **pgAdmin 4** (`port 5050`).

### ✅ 2. Cơ sở dữ liệu & EF Core (Database Schema)
- [x] Chuẩn hóa các Entity đúng đặc tả kỹ thuật, **không có Mock Data**:
  - `Documents`: `Id` (Guid), `FileName` (Max 255), `FilePath`, `FileSize`, `Status` (Enum: `Pending`, `Processing`, `Completed`, `Failed`), `ErrorMessage`, `UploadedAt` (DateTimeOffset UTC).
  - `ParentChunks`: `Id` (Guid), `DocumentId` (FK), `Content` (~800-1000 từ), `PageNumber`, `ChunkIndex`. Quan hệ **Cascade Delete** với Documents.
  - `ChildChunks`: `Id` (Guid), `ParentChunkId` (FK), `Content` (~150-200 từ), `Embedding` (`vector(768)`), `ChunkIndex`. Quan hệ **Cascade Delete** với ParentChunks.
- [x] Thiết lập **HNSW Index** (`vector_cosine_ops`) trên cột `Embedding` giúp tăng tốc độ tìm kiếm vector hàng chục lần so với quét tuần tự.
- [x] Tạo Migration hoàn chỉnh: `InitialRAGSchema` trong thư mục [Migrations](file:///D:/DALN/ragdocument/Migrations).

### ✅ 3. Xử lý tác vụ nền (Hangfire Worker)
- [x] Cấu hình Hangfire lưu trữ trạng thái trên PostgreSQL (`Hangfire.PostgreSql`).
- [x] Giao diện Dashboard thời gian thực tại endpoint `/hangfire`.
- [x] Lập trình `DocumentProcessingJob` triển khai interface `IDocumentProcessorJob`:
  - Trích xuất văn bản từng trang PDF bằng `PdfPig` (thuần C#, không phụ thuộc C++ native).
  - **Parent-Child Chunking**: Cắt khối ngữ cảnh lớn `ParentChunk` (~800 từ) và chia nhỏ thành các `ChildChunk` (~180 từ) kèm độ trùng lặp 20 từ (`overlap = 20`).
  - Gọi AI Service sinh vector embedding 768 chiều.
  - **Batch Insert**: Lưu toàn bộ chunks vào DB theo lô để tối ưu hiệu năng I/O.
  - Tự động cập nhật trạng thái `Completed` hoặc `Failed` kèm `ErrorMessage`.

### ✅ 4. Tích hợp AI & Dịch vụ RAG (Hybrid Search + Parent Retrieval)
- [x] Module `AIService` linh hoạt, hỗ trợ 3 nhà cung cấp:
  - **Google Gemini**: Embedding `text-embedding-004` (768 chiều) + Chat `gemini-1.5-flash`.
  - **Ollama Local**: Embedding `nomic-embed-text` (768 chiều) + Chat `llama3`.
  - **OpenAI**: Embedding `text-embedding-3-small` (dimensions = 768) + Chat `gpt-4o-mini`.
- [x] `RetrievalService`:
  - Tính khoảng cách cosine (`<=>`) trên `ChildChunks` lấy Top K phù hợp nhất.
  - **Parent Retrieval**: Từ danh sách `ChildChunk`, truy xuất `ParentChunk` tương ứng để lấy ngữ cảnh rộng (~800 từ) kèm `PageNumber` và `DocumentId`.
- [x] `ChatRAGService`:
  - Tổng hợp ngữ cảnh vào Prompt chuyên dụng.
  - Ép LLM trả về cấu trúc JSON nghiêm ngặt chứa câu trả lời (`answer`) và danh sách trích dẫn (`citations` gồm `documentId`, `documentName`, `pageNumber`, `snippet`).

### ✅ 5. Backend RESTful APIs
- [x] `POST /api/documents/upload`: Tiếp nhận file PDF, lưu trữ vật lý, tạo trạng thái `Pending`, đẩy vào hàng đợi Hangfire và trả về HTTP `202 Accepted` ngay lập tức (không treo UI).
- [x] `GET /api/documents`: Lấy danh sách toàn bộ tài liệu kèm trạng thái, số trang, kích thước.
- [x] `GET /api/documents/{id}/file`: Stream file PDF phục vụ trực tiếp cho PDF Viewer trên Web (hỗ trợ HTTP Range Processing).
- [x] `DELETE /api/documents/{id}`: Xóa tài liệu khỏi DB (tự động Cascade Delete toàn bộ chunks và vector) và xóa file vật lý trên ổ đĩa.
- [x] `POST /api/chat/query`: Nhận câu hỏi người dùng, thực hiện RAG và trả kết quả kèm Metadata trích dẫn.
- [x] Cấu hình CORS mở, Swagger UI tài liệu hóa toàn bộ API tại `/swagger`.

### ✅ 6. Giao diện Frontend React Split-View Hiện đại
- [x] Khởi tạo ứng dụng **React 19 + TypeScript + Vite + TailwindCSS v4 + Lucide Icons** tại `frontend/`.
- [x] **Layout 2 cột Split-View (Chia đôi màn hình)**:
  - **Cột trái (Chat Console):** Hiển thị lịch sử chat, ô nhập câu hỏi, các thẻ **Trích dẫn Chip** `[Tên-file.pdf - Trang X]`.
  - **Cột phải (PDF Viewer):** Nhúng PDF trực tiếp, khi nhấp vào thẻ Trích dẫn bên trái thì viewer **tự động nhảy ngay lập tức đến số trang PDF tương ứng** (`#page=X`).
- [x] **Trang Quản trị (Admin Portal Modal):**
  - Vùng kéo thả (Drag & Drop) nhiều file PDF cùng lúc.
  - Bảng quản lý hiển thị trạng thái real-time (`Pending`, `Processing`, `Completed`, `Failed`). Tự động polling cập nhật khi Hangfire xử lý xong.
  - Xem thông tin chi tiết lỗi nếu có file bị lỗi, nút xóa file, nút liên kết sang Hangfire Dashboard.

---

## 📂 2. CẤU TRÚC THƯ MỤC DỰ ÁN

```text
D:\DALN\ragdocument\
├── Controllers\
│   ├── ChatController.cs            # API xử lý hỏi đáp RAG
│   └── DocumentsController.cs       # API quản lý file PDF, upload, stream, delete
├── Data\
│   └── ApplicationDbContext.cs      # EF Core DbContext cấu hình pgvector & cascade
├── DTOs\
│   ├── ChatDto.cs                   # Request / Response / Citation DTOs
│   ├── DocumentDto.cs               # Document metadata DTO
│   └── SearchResultDto.cs           # DTO kết quả tìm kiếm hybrid
├── Migrations\
│   └── ..._InitialRAGSchema.cs      # Migration tạo bảng pgvector & HNSW index
├── Models\
│   ├── ChildChunk.cs                # Entity đoạn con chứa Vector(768)
│   ├── Document.cs                  # Entity tài liệu gốc
│   ├── DocumentStatus.cs            # Enum: Pending, Processing, Completed, Failed
│   └── ParentChunk.cs               # Entity đoạn cha chứa ngữ cảnh lớn & số trang
├── Services\
│   ├── AIService.cs                 # Xử lý Embedding & LLM (Gemini, Ollama, OpenAI)
│   ├── IAIService.cs                # Interface cho AI Service
│   ├── ChatRAGService.cs            # Xử lý tổng hợp câu trả lời & ép trích dẫn JSON
│   ├── IChatRAGService.cs           # Interface cho Chat RAG
│   ├── DocumentProcessingJob.cs     # Hangfire Worker xử lý chunking & batch insert
│   ├── IDocumentProcessorJob.cs     # Interface Hangfire Worker
│   ├── RetrievalService.cs          # Hybrid Search & Parent Retrieval
│   └── IRetrievalService.cs         # Interface tìm kiếm vector
├── frontend\                        # Giao diện React Split-View
│   ├── src\
│   │   ├── components\
│   │   │   ├── AdminModal.tsx       # Quản trị PDF, Drag & Drop upload, bảng trạng thái
│   │   │   ├── ChatConsole.tsx      # Khung chat & các thẻ Citation Chips nhảy trang
│   │   │   └── PdfViewer.tsx        # Trình xem PDF nhúng nhảy trực tiếp đến trang
│   │   ├── App.tsx                  # Split-View container & điều phối state
│   │   ├── types.ts                 # TypeScript interfaces
│   │   └── index.css                # TailwindCSS v4 styles
│   ├── package.json
│   └── vite.config.ts               # Proxy /api và /hangfire sang Backend
├── appsettings.json                 # Cấu hình chuỗi kết nối DB & AI API Keys
├── docker-compose.yml               # Container PostgreSQL 16 pgvector + pgAdmin 4
├── Program.cs                       # Khởi tạo DI, Middleware, Hangfire, Swagger
└── SmartDocumentRAG.API.csproj      # .NET 10 project file
```

---

## 🚀 3. HƯỚNG DẪN CÀI ĐẶT & KHỞI CHẠY CHI TIẾT

### Bước 1: Khởi động PostgreSQL 16 + pgvector qua Docker

1. Nếu máy bạn chưa cài Docker Desktop: Hãy nhấp đúp vào bộ cài đã tải sẵn tại:  
   📁 `D:\DALN\Docker Desktop_4.93.0_Machine_X64_exe_en-US.exe` và hoàn tất cài đặt (khởi động lại máy nếu Windows yêu cầu).
2. Mở ứng dụng **Docker Desktop**.
3. Mở cửa sổ **PowerShell** tại thư mục `D:\DALN\ragdocument` và chạy:

```powershell
cd D:\DALN\ragdocument
docker compose up -d
```

> **Kiểm tra:**
> - PostgreSQL pgvector sẽ lắng nghe tại cổng `localhost:5432`.
> - pgAdmin 4 lắng nghe tại `http://localhost:5050` (Email: `admin@rag.com`, Mật khẩu: `adminpassword`).

---

### Bước 2: Cập nhật Migration vào Database

Chạy lệnh EF Core sau trong PowerShell để tạo các bảng `Documents`, `ParentChunks`, `ChildChunks` cùng tiện ích mở rộng `pgvector`:

```powershell
dotnet ef database update
```

*(Lệnh này sẽ tạo cấu trúc bảng sạch hoàn toàn và sẵn sàng nhận dữ liệu thật).*

---

### Bước 3: Cấu hình API Key trong `appsettings.json`

Mở file [appsettings.json](file:///D:/DALN/ragdocument/appsettings.json) và cấu hình nhà cung cấp AI bạn muốn dùng:

#### Cách 1: Sử dụng Google Gemini (Khuyên dùng, miễn phí & nhanh)
Lấy API Key miễn phí tại [Google AI Studio](https://aistudio.google.com/):
```json
"AISettings": {
  "Provider": "Gemini"
},
"GeminiSettings": {
  "ApiKey": "AIzaSy...",
  "EmbeddingModel": "text-embedding-004",
  "ChatModel": "gemini-1.5-flash"
}
```

#### Cách 2: Sử dụng Ollama (Chạy offline trên máy local)
Nếu bạn có cài Ollama trên máy:
```powershell
ollama pull nomic-embed-text
ollama pull llama3
```
Và đổi trong `appsettings.json`:
```json
"AISettings": {
  "Provider": "Ollama"
}
```

#### Cách 3: Sử dụng OpenAI API
```json
"AISettings": {
  "Provider": "OpenAI"
},
"OpenAISettings": {
  "ApiKey": "sk-...",
  "EmbeddingModel": "text-embedding-3-small",
  "ChatModel": "gpt-4o-mini"
}
```

---

### Bước 4: Khởi chạy Backend .NET 10

Tại thư mục `D:\DALN\ragdocument`, gõ lệnh:

```powershell
dotnet run
```

Backend sẽ khởi động và phục vụ các đường dẫn sau:
- **Swagger API Documentation:** `http://localhost:5000/swagger`
- **Hangfire Worker Dashboard:** `http://localhost:5000/hangfire`

---

### Bước 5: Khởi chạy Frontend React (Split-View)

Mở một cửa sổ **PowerShell thứ 2**, chuyển vào thư mục `frontend` và chạy:

```powershell
cd D:\DALN\ragdocument\frontend
npm run dev
```

Mở trình duyệt Web truy cập vào:  
👉 **`http://localhost:3000`**

---

## 🧪 4. HƯỚNG DẪN KIỂM THỬ CÁC TÍNH NĂNG CHÍNH

### Kịch bản 1: Upload tài liệu & Theo dõi Hangfire xử lý ngầm
1. Trên giao diện `http://localhost:3000`, nhấp nút **"Quản lý Tài liệu"** ở góc trên bên phải.
2. Kéo thả một hoặc nhiều file PDF tài liệu nội bộ vào ô upload.
3. API sẽ phản hồi ngay lập tức `202 Accepted` và đưa file vào hàng đợi.
4. Trạng thái tài liệu sẽ chuyển sang màu xanh dương: `Đang nhúng vector...` và tự động cập nhật sang màu xanh lá: `Hoàn thành` khi xử lý xong.
5. Bạn có thể mở tab `http://localhost:5000/hangfire` để xem đồ thị tác vụ nền được xử lý theo thời gian thực.

### Kịch bản 2: Hỏi đáp RAG và Nhảy trực tiếp đến trang PDF (Visual Citation)
1. Tại khung **Chat Console** bên trái, nhập câu hỏi liên quan đến nội dung tài liệu vừa upload (ví dụ: *"Tóm tắt các quy định chính trong tài liệu"*).
2. Hệ thống sẽ:
   - Tính vector câu hỏi.
   - Quét qua bảng `ChildChunks` bằng Cosine Distance.
   - Lấy ngữ cảnh lớn từ `ParentChunks`.
   - Gửi ngữ cảnh đến AI để trả lời.
3. Kết quả trả về gồm câu trả lời tiếng Việt chính xác và các thẻ chip trích dẫn:  
   👉 `[Tên-file.pdf - Trang X]`
4. **Nhấp chuột vào bất kỳ thẻ Trích dẫn nào:** Trình xem PDF ở cột bên phải sẽ lập tức cuộn và hiển thị chính xác đến trang `X` của tài liệu đó để người dùng đối chiếu văn bản gốc.

### Kịch bản 3: Xóa tài liệu
1. Mở modal **"Quản lý Tài liệu"**.
2. Nhấp vào biểu tượng thùng rác bên cạnh file muốn xóa.
3. Hệ thống sẽ tự động Cascade Delete toàn bộ vector trong PostgreSQL và xóa file vật lý trên máy chủ.

---

## 📡 5. DANH SÁCH ENDPOINTS API CHÍNH

| Method | Endpoint | Mô tả |
| :--- | :--- | :--- |
| `POST` | `/api/documents/upload` | Upload PDF (multipart/form-data), đẩy vào Hangfire Queue |
| `GET` | `/api/documents` | Lấy danh sách toàn bộ tài liệu kèm trạng thái xử lý |
| `GET` | `/api/documents/{id}` | Lấy chi tiết thông tin một tài liệu |
| `GET` | `/api/documents/{id}/file` | Stream tệp PDF phục vụ trình xem PDF Web |
| `DELETE` | `/api/documents/{id}` | Xóa tài liệu, xóa cascade chunks/vector và file đĩa |
| `POST` | `/api/chat/query` | Hỏi đáp RAG: Nhận câu hỏi, trả về câu trả lời và trích dẫn |
| `GET` | `/api/chat/search` | Kiểm thử trực tiếp Hybrid Search không qua LLM |
| `GET` | `/hangfire` | Dashboard giám sát Background Jobs thời gian thực |
| `GET` | `/swagger` | Tài liệu API tương tác trực tiếp qua Swagger UI |
