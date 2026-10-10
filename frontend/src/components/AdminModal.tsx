import React, { useState, useRef } from 'react';
import type { DocumentItem } from '../types';
import { UploadCloud, Trash2, FileText, CheckCircle2, AlertCircle, Clock, Loader2, ExternalLink, X } from 'lucide-react';

interface AdminModalProps {
  isOpen: boolean;
  onClose: () => void;
  documents: DocumentItem[];
  onRefresh: () => void;
  onSelectDocument: (docId: string, pageNumber: number) => void;
}

export const AdminModal: React.FC<AdminModalProps> = ({
  isOpen,
  onClose,
  documents,
  onRefresh,
  onSelectDocument,
}) => {
  const [isDragging, setIsDragging] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadMessage, setUploadMessage] = useState<string | null>(null);
  const [errorDetails, setErrorDetails] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  if (!isOpen) return null;

  const handleFiles = async (files: FileList | null) => {
    if (!files || files.length === 0) return;

    setUploading(true);
    setUploadMessage('Đang tải lên các tệp...');

    let successCount = 0;
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      if (!file.name.toLowerCase().endsWith('.pdf')) {
        continue;
      }

      const formData = new FormData();
      formData.append('file', file);

      try {
        const res = await fetch('/api/documents/upload', {
          method: 'POST',
          body: formData,
        });
        if (res.ok) {
          successCount++;
        }
      } catch (err) {
        console.error('Lỗi khi upload:', err);
      }
    }

    setUploading(false);
    setUploadMessage(`Đã đưa ${successCount} tệp vào hàng đợi Hangfire để xử lý ngầm.`);
    onRefresh();
  };

  const handleDelete = async (id: string, fileName: string) => {
    if (!window.confirm(`Bạn có chắc chắn muốn xóa tài liệu "${fileName}"? Toàn bộ vector liên quan sẽ bị xóa.`)) {
      return;
    }

    try {
      const res = await fetch(`/api/documents/${id}`, { method: 'DELETE' });
      if (res.ok) {
        onRefresh();
      } else {
        alert('Không thể xóa tài liệu.');
      }
    } catch (err) {
      console.error(err);
      alert('Lỗi khi xóa tài liệu.');
    }
  };

  const formatFileSize = (bytes: number) => {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const renderStatusBadge = (doc: DocumentItem) => {
    switch (doc.status) {
      case 'Pending':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-slate-800 text-slate-300 border border-slate-700">
            <Clock className="w-3 h-3" /> Chờ xử lý
          </span>
        );
      case 'Processing':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-blue-950 text-blue-300 border border-blue-800 animate-pulse">
            <Loader2 className="w-3 h-3 animate-spin" /> Đang nhúng vector...
          </span>
        );
      case 'Completed':
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-emerald-950 text-emerald-300 border border-emerald-800">
            <CheckCircle2 className="w-3 h-3" /> Hoàn thành
          </span>
        );
      case 'Failed':
        return (
          <button
            onClick={() => setErrorDetails(doc.errorMessage || 'Lỗi không xác định.')}
            className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full text-xs font-medium bg-rose-950 text-rose-300 border border-rose-800 hover:underline cursor-pointer"
          >
            <AlertCircle className="w-3 h-3" /> Thất bại (Xem lỗi)
          </button>
        );
      default:
        return <span>{doc.status}</span>;
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/70 backdrop-blur-xs p-4">
      <div className="bg-slate-900 border border-slate-800 rounded-xl w-full max-w-4xl max-h-[90vh] flex flex-col shadow-2xl overflow-hidden">
        {/* Header */}
        <div className="px-6 py-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <FileText className="w-6 h-6 text-indigo-400" />
            <h2 className="text-xl font-semibold text-white">Quản trị Kho Tài liệu (Admin Portal)</h2>
          </div>
          <div className="flex items-center gap-3">
            <a
              href="/hangfire"
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center gap-1 text-xs px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-indigo-300 font-medium transition"
            >
              Hangfire Dashboard <ExternalLink className="w-3 h-3" />
            </a>
            <button
              onClick={onClose}
              className="p-1 rounded-lg hover:bg-slate-800 text-slate-400 hover:text-white transition"
            >
              <X className="w-5 h-5" />
            </button>
          </div>
        </div>

        {/* Content */}
        <div className="p-6 overflow-y-auto space-y-6 flex-1">
          {/* Upload Zone */}
          <div
            onDragOver={(e) => {
              e.preventDefault();
              setIsDragging(true);
            }}
            onDragLeave={() => setIsDragging(false)}
            onDrop={(e) => {
              e.preventDefault();
              setIsDragging(false);
              handleFiles(e.dataTransfer.files);
            }}
            onClick={() => fileInputRef.current?.click()}
            className={`border-2 border-dashed rounded-xl p-8 text-center cursor-pointer transition flex flex-col items-center justify-center gap-2 ${
              isDragging
                ? 'border-indigo-500 bg-indigo-950/20'
                : 'border-slate-700 hover:border-indigo-500/50 bg-slate-950/40'
            }`}
          >
            <input
              type="file"
              ref={fileInputRef}
              multiple
              accept=".pdf"
              className="hidden"
              onChange={(e) => handleFiles(e.target.files)}
            />
            <UploadCloud className="w-10 h-10 text-indigo-400" />
            <div className="text-sm font-medium text-slate-200">
              Kéo thả file PDF vào đây hoặc <span className="text-indigo-400 underline">bấm để chọn</span>
            </div>
            <div className="text-xs text-slate-400">
              Hỗ trợ chọn nhiều file cùng lúc. Các file sẽ được Hangfire chia nhỏ và tạo vector ngầm.
            </div>
          </div>

          {uploadMessage && (
            <div className="p-3 bg-indigo-950/40 border border-indigo-900 rounded-lg text-xs text-indigo-300 flex items-center justify-between">
              <span>{uploadMessage}</span>
              {uploading && <Loader2 className="w-4 h-4 animate-spin" />}
            </div>
          )}

          {/* Table */}
          <div className="space-y-2">
            <div className="flex items-center justify-between">
              <h3 className="text-sm font-semibold text-slate-300 uppercase tracking-wider">
                Danh sách Tài liệu ({documents.length})
              </h3>
              <button
                onClick={onRefresh}
                className="text-xs text-indigo-400 hover:underline"
              >
                Làm mới
              </button>
            </div>

            <div className="border border-slate-800 rounded-lg overflow-hidden bg-slate-950/30">
              {documents.length === 0 ? (
                <div className="p-8 text-center text-sm text-slate-500">
                  Chưa có tài liệu nào trong kho. Hãy upload file PDF ở trên.
                </div>
              ) : (
                <table className="w-full text-left text-xs text-slate-300">
                  <thead className="bg-slate-800/60 uppercase font-semibold text-slate-400 border-b border-slate-800">
                    <tr>
                      <th className="px-4 py-3">Tên file</th>
                      <th className="px-4 py-3">Kích thước</th>
                      <th className="px-4 py-3">Trang</th>
                      <th className="px-4 py-3">Ngày tải lên</th>
                      <th className="px-4 py-3">Trạng thái</th>
                      <th className="px-4 py-3 text-right">Thao tác</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-800/50">
                    {documents.map((doc) => (
                      <tr key={doc.id} className="hover:bg-slate-800/30 transition">
                        <td className="px-4 py-3 font-medium text-slate-200 flex items-center gap-2">
                          <FileText className="w-4 h-4 text-slate-400 shrink-0" />
                          <span className="truncate max-w-[200px]" title={doc.fileName}>
                            {doc.fileName}
                          </span>
                        </td>
                        <td className="px-4 py-3">{formatFileSize(doc.fileSize)}</td>
                        <td className="px-4 py-3">{doc.totalPages > 0 ? doc.totalPages : '—'}</td>
                        <td className="px-4 py-3">{new Date(doc.uploadedAt).toLocaleString('vi-VN')}</td>
                        <td className="px-4 py-3">{renderStatusBadge(doc)}</td>
                        <td className="px-4 py-3 text-right space-x-2">
                          {doc.status === 'Completed' && (
                            <button
                              onClick={() => {
                                onSelectDocument(doc.id, 1);
                                onClose();
                              }}
                              className="text-indigo-400 hover:text-indigo-300 font-medium"
                            >
                              Xem PDF
                            </button>
                          )}
                          <button
                            onClick={() => handleDelete(doc.id, doc.fileName)}
                            className="text-rose-400 hover:text-rose-300"
                            title="Xóa tài liệu"
                          >
                            <Trash2 className="w-4 h-4 inline" />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>
        </div>

        {/* Footer */}
        <div className="px-6 py-3 border-t border-slate-800 bg-slate-900/60 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 text-sm font-medium transition"
          >
            Đóng
          </button>
        </div>
      </div>

      {/* Modal chi tiết lỗi nếu có */}
      {errorDetails && (
        <div className="fixed inset-0 z-60 flex items-center justify-center bg-black/60 p-4">
          <div className="bg-slate-900 border border-rose-800 rounded-xl p-5 max-w-lg w-full space-y-4 shadow-xl">
            <div className="flex items-center gap-2 text-rose-400 font-semibold text-sm">
              <AlertCircle className="w-5 h-5" /> Chi tiết lỗi xử lý Background Job
            </div>
            <pre className="text-xs bg-slate-950 p-3 rounded-lg text-rose-200 overflow-x-auto max-h-48 whitespace-pre-wrap">
              {errorDetails}
            </pre>
            <div className="flex justify-end">
              <button
                onClick={() => setErrorDetails(null)}
                className="px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs text-white"
              >
                Đóng
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};
