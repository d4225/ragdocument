using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ParentChunk> ParentChunks => Set<ParentChunk>();
    public DbSet<ChildChunk> ChildChunks => Set<ChildChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Kích hoạt extension pgvector trong PostgreSQL
        modelBuilder.HasPostgresExtension("vector");

        // Cấu hình bảng Document
        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.FileName).HasMaxLength(255).IsRequired();
            entity.Property(d => d.FilePath).IsRequired();
            entity.Property(d => d.Status).HasConversion<string>().HasMaxLength(50);
            entity.Property(d => d.UploadedAt).IsRequired();

            entity.HasMany(d => d.ParentChunks)
                .WithOne(p => p.Document)
                .HasForeignKey(p => p.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cấu hình bảng ParentChunk
        modelBuilder.Entity<ParentChunk>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Content).IsRequired();
            entity.Property(p => p.PageNumber).IsRequired();
            entity.Property(p => p.ChunkIndex).IsRequired();

            entity.HasMany(p => p.ChildChunks)
                .WithOne(c => c.ParentChunk)
                .HasForeignKey(c => c.ParentChunkId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Cấu hình bảng ChildChunk với Vector(768)
        modelBuilder.Entity<ChildChunk>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.Content).IsRequired();
            entity.Property(c => c.ChunkIndex).IsRequired();
            entity.Property(c => c.Embedding).HasColumnType("vector(768)");

            // Tạo HNSW Index cho vector cosine distance tìm kiếm cực nhanh
            entity.HasIndex(c => c.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });
    }
}