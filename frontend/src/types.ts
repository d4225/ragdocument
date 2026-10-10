export interface DocumentItem {
  id: string;
  fileName: string;
  fileSize: number;
  status: 'Pending' | 'Processing' | 'Completed' | 'Failed' | string;
  errorMessage?: string | null;
  uploadedAt: string;
  totalPages: number;
}

export interface Citation {
  documentId: string;
  documentName: string;
  pageNumber: number;
  snippet: string;
}

export interface ChatMessage {
  id: string;
  sender: 'user' | 'assistant';
  content: string;
  citations?: Citation[];
  timestamp: string;
}
