using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Data;

public class ApplicationDbContext : DbContext
{
    public DbSet<User> Users => Set<User>();
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<ParentChunk> ParentChunks => Set<ParentChunk>();
    public DbSet<ChildChunk> ChildChunks => Set<ChildChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Kích hoạt extension vector trong Postgres
        modelBuilder.HasPostgresExtension("vector");

        // Cấu hình cột Embedding lưu kiểu vector(768)
        modelBuilder.Entity<ChildChunk>()
            .Property(c => c.Embedding)
            .HasColumnType("vector(3072)");
        // PostgreSQL Full-Text Search
        modelBuilder.Entity<ChildChunk>()
            .Property(c => c.SearchVector)
            .HasColumnType("tsvector")
            .ValueGeneratedOnAddOrUpdate();
    }
}