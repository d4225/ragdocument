import React, { useState, useRef, useEffect } from 'react';
import type { ChatMessage } from '../types';
import { Send, Bot, User, Bookmark, Loader2, Sparkles, Trash2, ArrowUpRight } from 'lucide-react';

interface ChatConsoleProps {
  messages: ChatMessage[];
  loading: boolean;
  onSendMessage: (question: string) => void;
  onSelectCitation: (docId: string, docName: string, pageNumber: number) => void;
  onClearChat: () => void;
  activeDocId: string | null;
  activePage: number | null;
}

export const ChatConsole: React.FC<ChatConsoleProps> = ({
  messages,
  loading,
  onSendMessage,
  onSelectCitation,
  onClearChat,
  activeDocId,
  activePage,
}) => {
  const [input, setInput] = useState('');
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, loading]);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!input.trim() || loading) return;
    onSendMessage(input.trim());
    setInput('');
  };

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  };

  return (
    <div className="h-full flex flex-col bg-slate-900">
      {/* Console Top Actions */}
      <div className="px-5 py-3 border-b border-slate-800 flex items-center justify-between text-xs text-slate-400">
        <div className="flex items-center gap-2">
          <Sparkles className="w-4 h-4 text-indigo-400" />
          <span className="font-medium text-slate-300">RAG Chat Console</span>
          <span className="px-2 py-0.5 rounded-full bg-slate-800 text-[10px] text-slate-400">
            Hybrid Search + Parent Retrieval
          </span>
        </div>
        {messages.length > 0 && (
          <button
            onClick={onClearChat}
            className="flex items-center gap-1 hover:text-rose-400 transition cursor-pointer"
            title="Xóa đoạn chat"
          >
            <Trash2 className="w-3.5 h-3.5" /> Xóa hội thoại
          </button>
        )}
      </div>

      {/* Messages List */}
      <div className="flex-1 overflow-y-auto p-5 space-y-6">
        {messages.length === 0 ? (
          <div className="h-full flex flex-col items-center justify-center text-center p-8 space-y-4">
            <div className="w-12 h-12 rounded-2xl bg-indigo-950/60 border border-indigo-800/50 flex items-center justify-center text-indigo-400 shadow-lg shadow-indigo-950/50">
              <Bot className="w-6 h-6" />
            </div>
            <div className="space-y-1">
              <h3 className="text-base font-semibold text-slate-200">
                Trợ lý Tra cứu Tài liệu Thông minh
              </h3>
              <p className="text-xs text-slate-400 max-w-md">
                Đặt câu hỏi về các tài liệu PDF nội bộ đã upload. Hệ thống sẽ tự động tìm kiếm đoạn văn phù hợp nhất và trích dẫn số trang chính xác.
              </p>
            </div>

            <div className="grid grid-cols-1 gap-2 w-full max-w-sm pt-4">
              {[
                'Tóm tắt các điểm chính trong tài liệu?',
                'Quy định về thời gian và thủ tục được nêu ở đâu?',
                'Tìm kiếm các nội dung liên quan đến điều kiện kỹ thuật.',
              ].map((sample, idx) => (
                <button
                  key={idx}
                  onClick={() => onSendMessage(sample)}
                  className="text-left text-xs p-3 rounded-xl bg-slate-800/60 hover:bg-slate-800 border border-slate-700/60 hover:border-indigo-500/50 text-slate-300 hover:text-white transition cursor-pointer"
                >
                  "{sample}"
                </button>
              ))}
            </div>
          </div>
        ) : (
          messages.map((msg) => (
            <div
              key={msg.id}
              className={`flex gap-3 ${msg.sender === 'user' ? 'justify-end' : 'justify-start'}`}
            >
              {msg.sender === 'assistant' && (
                <div className="w-8 h-8 rounded-lg bg-indigo-950 border border-indigo-800/60 flex items-center justify-center text-indigo-400 shrink-0">
                  <Bot className="w-4 h-4" />
                </div>
              )}

              <div
                className={`max-w-[85%] rounded-2xl p-4 space-y-3 ${
                  msg.sender === 'user'
                    ? 'bg-indigo-600 text-white rounded-br-xs shadow-md'
                    : 'bg-slate-800/80 border border-slate-700/60 text-slate-200 rounded-bl-xs shadow-md'
                }`}
              >
                <div className="text-sm leading-relaxed whitespace-pre-wrap">
                  {msg.content}
                </div>

                {/* Thẻ Trích dẫn (Citations Chips) */}
                {msg.citations && msg.citations.length > 0 && (
                  <div className="pt-2 border-t border-slate-700/50 space-y-2">
                    <div className="flex items-center gap-1.5 text-[11px] font-semibold text-indigo-300">
                      <Bookmark className="w-3.5 h-3.5" />
                      Trích dẫn nguồn tài liệu ({msg.citations.length}):
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {msg.citations.map((c, cIdx) => {
                        const isSelected = activeDocId === c.documentId && activePage === c.pageNumber;
                        return (
                          <button
                            key={cIdx}
                            onClick={() => onSelectCitation(c.documentId, c.documentName, c.pageNumber)}
                            className={`group inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium transition cursor-pointer border ${
                              isSelected
                                ? 'bg-indigo-600 text-white border-indigo-400 shadow-sm'
                                : 'bg-slate-900/90 text-indigo-300 border-indigo-900/60 hover:bg-indigo-950 hover:border-indigo-700'
                            }`}
                            title={c.snippet}
                          >
                            <span className="truncate max-w-[140px] font-semibold">
                              {c.documentName}
                            </span>
                            <span className="px-1.5 py-0.2 rounded bg-black/30 text-[10px] text-indigo-200">
                              Trang {c.pageNumber}
                            </span>
                            <ArrowUpRight className="w-3 h-3 opacity-60 group-hover:opacity-100 transition" />
                          </button>
                        );
                      })}
                    </div>
                  </div>
                )}
              </div>

              {msg.sender === 'user' && (
                <div className="w-8 h-8 rounded-lg bg-slate-700 flex items-center justify-center text-slate-300 shrink-0">
                  <User className="w-4 h-4" />
                </div>
              )}
            </div>
          ))
        )}

        {loading && (
          <div className="flex gap-3 justify-start">
            <div className="w-8 h-8 rounded-lg bg-indigo-950 border border-indigo-800/60 flex items-center justify-center text-indigo-400 shrink-0">
              <Bot className="w-4 h-4" />
            </div>
            <div className="bg-slate-800/80 border border-slate-700/60 rounded-2xl rounded-bl-xs p-4 flex items-center gap-3 text-slate-300 text-sm">
              <Loader2 className="w-4 h-4 animate-spin text-indigo-400" />
              <span>Đang đối chiếu vector và tổng hợp câu trả lời từ tài liệu...</span>
            </div>
          </div>
        )}

        <div ref={messagesEndRef} />
      </div>

      {/* Input Form */}
      <form onSubmit={handleSubmit} className="p-4 border-t border-slate-800 bg-slate-900/80">
        <div className="relative flex items-end bg-slate-950 border border-slate-700 rounded-xl focus-within:border-indigo-500 transition shadow-inner">
          <textarea
            value={input}
            onChange={(e) => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Hỏi bất cứ điều gì về tài liệu... (Nhấn Enter để gửi, Shift+Enter xuống dòng)"
            rows={2}
            className="w-full px-4 py-3 bg-transparent text-sm text-slate-100 placeholder-slate-500 resize-none focus:outline-hidden"
          />
          <button
            type="submit"
            disabled={!input.trim() || loading}
            className="m-2 p-2.5 rounded-lg bg-indigo-600 hover:bg-indigo-500 disabled:bg-slate-800 text-white disabled:text-slate-500 transition cursor-pointer disabled:cursor-not-allowed shrink-0"
            title="Gửi câu hỏi"
          >
            <Send className="w-4 h-4" />
          </button>
        </div>
      </form>
    </div>
  );
};
