📚 HỆ THỐNG TRỢ LÝ TRA CỨU TÀI LIỆU THÔNG MINH (RAG DOCUMENT Q&A)

Công nghệ: .NET 10 Web API + ReactJS/Angular + PostgreSQL (pgvector) + Hangfire

🎯 1. Mục tiêu Đề tài
Xây dựng hệ thống tra cứu tài liệu nội bộ (PDF) thông minh bằng kỹ thuật RAG:

Xử lý ngầm (Background Job): Upload PDF lớn không treo UI nhờ Hangfire.

Hybrid Search + Parent Retrieval: Tìm kiếm chính xác đoạn ngắn nhưng trả về đoạn văn lớn cho AI đọc đủ ngữ cảnh.

Trích dẫn trực quan: Trả lời kèm liên kết nhảy đến đúng trang PDF.

🏗️ 2. Kiến trúc & Công nghệ
Backend: .NET Web API, Entity Framework Core, Hangfire Worker.

Database: PostgreSQL 16 + pgvector (chạy qua Docker).

Frontend: ReactJS / Angular + PDF Viewer.

🗄️ 3. Mô hình CSDL (dự kiến)
Documents: Id, FileName, FilePath, UploadedAt, Status.

ParentChunks: Id, DocumentId (FK), Content (~1000 từ), PageNumber.

ChildChunks: Id, ParentChunkId (FK), Content (~200 từ), Embedding (vector(768)).
