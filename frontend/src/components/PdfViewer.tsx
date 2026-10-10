import React from 'react';
import { ExternalLink, BookOpen } from 'lucide-react';

interface PdfViewerProps {
  documentId: string | null;
  documentName: string | null;
  pageNumber: number;
  onPageChange: (newPage: number) => void;
}

export const PdfViewer: React.FC<PdfViewerProps> = ({
  documentId,
  documentName,
  pageNumber,
  onPageChange,
}) => {
  if (!documentId) {
    return (
      <div className="h-full flex flex-col items-center justify-center p-8 text-center bg-slate-950 border-l border-slate-800 text-slate-500">
        <BookOpen className="w-16 h-16 text-slate-700 mb-4 stroke-1" />
        <h3 className="text-lg font-medium text-slate-400 mb-1">Trình xem Tài liệu PDF (Visual Citations)</h3>
        <p className="text-sm max-w-sm text-slate-500">
          Chọn một <span className="text-indigo-400 font-medium">Thẻ Trích dẫn</span> bên khung Chat hoặc chọn tài liệu trong Quản trị để hiển thị trang PDF gốc tại đây.
        </p>
      </div>
    );
  }

  const pdfUrl = `/api/documents/${documentId}/file#page=${pageNumber}`;

  return (
    <div className="h-full flex flex-col bg-slate-950 border-l border-slate-800">
      {/* PDF Header bar */}
      <div className="px-4 py-3 bg-slate-900 border-b border-slate-800 flex items-center justify-between gap-4">
        <div className="flex items-center gap-2 min-w-0">
          <BookOpen className="w-5 h-5 text-indigo-400 shrink-0" />
          <span className="text-sm font-semibold text-slate-200 truncate" title={documentName || 'Tài liệu'}>
            {documentName || 'Tài liệu'}
          </span>
        </div>

        <div className="flex items-center gap-3 shrink-0">
          {/* Nhảy trang */}
          <div className="flex items-center gap-1.5 text-xs text-slate-400">
            <span>Trang:</span>
            <input
              type="number"
              min={1}
              value={pageNumber}
              onChange={(e) => {
                const val = parseInt(e.target.value);
                if (!isNaN(val) && val > 0) {
                  onPageChange(val);
                }
              }}
              className="w-14 px-2 py-1 rounded bg-slate-800 border border-slate-700 text-center text-white font-medium focus:outline-hidden focus:border-indigo-500"
            />
          </div>

          {/* Mở tab mới */}
          <a
            href={pdfUrl}
            target="_blank"
            rel="noreferrer"
            className="p-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-400 hover:text-white transition"
            title="Mở PDF trong tab mới"
          >
            <ExternalLink className="w-4 h-4" />
          </a>
        </div>
      </div>

      {/* Info Banner when jumping */}
      <div className="px-4 py-2 bg-indigo-950/40 border-b border-indigo-900/50 flex items-center justify-between text-xs text-indigo-300">
        <span className="flex items-center gap-1.5 font-medium">
          <span className="w-2 h-2 rounded-full bg-indigo-400 animate-ping"></span>
          Đang hiển thị trang {pageNumber} của tài liệu
        </span>
        <span className="text-slate-400">Được trích dẫn trực tiếp bởi AI</span>
      </div>

      {/* Embedded PDF iframe */}
      <div className="flex-1 w-full h-full relative bg-slate-900">
        <iframe
          key={`${documentId}-${pageNumber}`}
          src={pdfUrl}
          className="w-full h-full border-0"
          title="PDF Viewer"
        />
      </div>
    </div>
  );
};
