namespace SmartDocumentRAG.API.DTOs
{
    public class QuizRequestDto
    {
        public Guid DocumentId { get; set; }
        public int QuestionCount { get; set; } = 5; // Mặc định 5 câu (hoặc 10, 20)
    }

    public class QuizQuestionDto
    {
        public int QuestionNumber { get; set; }
        public string QuestionText { get; set; } = string.Empty;
        public List<string> Options { get; set; } = new(); // 4 đáp án A, B, C, D
        public string CorrectAnswer { get; set; } = string.Empty; // Đáp án đúng (Ví dụ: "A")
        public string Explanation { get; set; } = string.Empty; // Giải thích chi tiết
    }
}