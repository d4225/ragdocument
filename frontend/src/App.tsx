import { useState, useEffect, useCallback } from 'react';
import type { DocumentItem, ChatMessage } from './types';
import { ChatConsole } from './components/ChatConsole';
import { PdfViewer } from './components/PdfViewer';
import { AdminModal } from './components/AdminModal';
import { Layers, FolderKanban, Activity, ExternalLink } from 'lucide-react';

export function App() {
  const [documents, setDocuments] = useState<DocumentItem[]>([]);
  const [isAdminOpen, setIsAdminOpen] = useState(false);
  const [activeDocId, setActiveDocId] = useState<string | null>(null);
  const [activeDocName, setActiveDocName] = useState<string | null>(null);
  const [activePage, setActivePage] = useState<number>(1);
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [loading, setLoading] = useState(false);

  // Lấy danh sách documents từ Backend
  const fetchDocuments = useCallback(async () => {
    try {
      const res = await fetch('/api/documents');
      if (res.ok) {
        const data: DocumentItem[] = await res.json();
        setDocuments(data);
      }
    } catch (err) {
      console.error('Không thể tải danh sách tài liệu:', err);
    }
  }, []);

  useEffect(() => {
    fetchDocuments();
  }, [fetchDocuments]);

  // Tự động polling làm mới trạng thái nếu có job đang chạy
  useEffect(() => {
    const hasPendingOrProcessing = documents.some(
      (d) => d.status === 'Pending' || d.status === 'Processing'
    );

    if (!hasPendingOrProcessing) return;

    const interval = setInterval(() => {
      fetchDocuments();
    }, 3000);

    return () => clearInterval(interval);
  }, [documents, fetchDocuments]);

  // Xử lý gửi câu hỏi RAG
  const handleSendMessage = async (question: string) => {
    const userMsg: ChatMessage = {
      id: Date.now().toString(),
      sender: 'user',
      content: question,
      timestamp: new Date().toISOString(),
    };

    setMessages((prev) => [...prev, userMsg]);
    setLoading(true);

    try {
      const res = await fetch('/api/chat/query', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, topK: 5 }),
      });

      if (!res.ok) {
        throw new Error(`Lỗi từ máy chủ (${res.status})`);
      }

      const data = await res.json();

      const aiMsg: ChatMessage = {
        id: (Date.now() + 1).toString(),
        sender: 'assistant',
        content: data.answer,
        citations: data.citations || [],
        timestamp: new Date().toISOString(),
      };

      setMessages((prev) => [...prev, aiMsg]);

      // Tự động nhảy đến trích dẫn đầu tiên nếu có
      if (data.citations && data.citations.length > 0) {
        const first = data.citations[0];
        setActiveDocId(first.documentId);
        setActiveDocName(first.documentName);
        setActivePage(first.pageNumber);
      }
    } catch (err: unknown) {
      console.error(err);
      const errorMsg: ChatMessage = {
        id: (Date.now() + 1).toString(),
        sender: 'assistant',
        content: `Đã xảy ra lỗi khi tìm kiếm: ${err instanceof Error ? err.message : 'Không thể kết nối tới Backend'}. Vui lòng kiểm tra kết nối API và Database.`,
        timestamp: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, errorMsg]);
    } finally {
      setLoading(false);
    }
  };

  // Khi click vào Citation Chip
  const handleSelectCitation = (docId: string, docName: string, pageNumber: number) => {
    setActiveDocId(docId);
    setActiveDocName(docName);
    setActivePage(pageNumber);
  };

  const completedDocsCount = documents.filter((d) => d.status === 'Completed').length;

  return (
    <div className="h-screen w-screen flex flex-col bg-slate-950 text-slate-100 overflow-hidden">
      {/* Top Navigation Bar */}
      <header className="h-14 px-6 border-b border-slate-800 bg-slate-900/90 flex items-center justify-between shrink-0 z-10">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 rounded-lg bg-indigo-600 flex items-center justify-center text-white shadow-md shadow-indigo-600/30">
            <Layers className="w-5 h-5" />
          </div>
          <div>
            <h1 className="text-sm font-bold tracking-tight text-white flex items-center gap-2 m-0">
              RAG DOCUMENT Q&A
              <span className="text-[10px] font-medium px-2 py-0.5 rounded-full bg-indigo-950 text-indigo-300 border border-indigo-800">
                .NET 10 + pgvector
              </span>
            </h1>
          </div>
        </div>

        {/* Right Header Actions */}
        <div className="flex items-center gap-3">
          <div className="hidden md:flex items-center gap-2 text-xs text-slate-400 bg-slate-950/60 px-3 py-1.5 rounded-lg border border-slate-800">
            <Activity className="w-3.5 h-3.5 text-emerald-400" />
            <span>Kho tài liệu:</span>
            <span className="font-semibold text-slate-200">
              {completedDocsCount}/{documents.length} sẵn sàng
            </span>
          </div>

          <a
            href="/hangfire"
            target="_blank"
            rel="noreferrer"
            className="hidden sm:inline-flex items-center gap-1.5 text-xs px-3 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white transition font-medium border border-slate-700"
          >
            Hangfire <ExternalLink className="w-3 h-3 text-slate-400" />
          </a>

          <button
            onClick={() => setIsAdminOpen(true)}
            className="inline-flex items-center gap-2 text-xs font-semibold px-3.5 py-1.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 text-white shadow-md shadow-indigo-600/20 transition cursor-pointer"
          >
            <FolderKanban className="w-4 h-4" />
            Quản lý Tài liệu
          </button>
        </div>
      </header>

      {/* Main Split-View Workspace */}
      <main className="flex-1 flex overflow-hidden">
        {/* Cột trái: Chat Console (50%) */}
        <section className="w-1/2 h-full flex flex-col border-r border-slate-800">
          <ChatConsole
            messages={messages}
            loading={loading}
            onSendMessage={handleSendMessage}
            onSelectCitation={handleSelectCitation}
            onClearChat={() => setMessages([])}
            activeDocId={activeDocId}
            activePage={activePage}
          />
        </section>

        {/* Cột phải: PDF Viewer (50%) */}
        <section className="w-1/2 h-full flex flex-col">
          <PdfViewer
            documentId={activeDocId}
            documentName={activeDocName}
            pageNumber={activePage}
            onPageChange={(p) => setActivePage(p)}
          />
        </section>
      </main>

      {/* Admin Management Modal */}
      <AdminModal
        isOpen={isAdminOpen}
        onClose={() => setIsAdminOpen(false)}
        documents={documents}
        onRefresh={fetchDocuments}
        onSelectDocument={(docId, pageNum) => {
          const doc = documents.find((d) => d.id === docId);
          setActiveDocId(docId);
          setActiveDocName(doc ? doc.fileName : 'Tài liệu');
          setActivePage(pageNum);
        }}
      />
    </div>
  );
}

export default App;
