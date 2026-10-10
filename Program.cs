using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=rag_document_db;Username=postgres;Password=postgrespassword";

// 1. Đăng ký PostgreSQL + pgvector
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        o => o.UseVector()
    ));

// 2. Cấu hình CORS cho Frontend React / Angular
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 3. Đăng ký Services & Dependency Injection
builder.Services.AddHttpClient<IAIService, AIService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(3);
});

builder.Services.AddScoped<IDocumentProcessorJob, DocumentProcessingJob>();
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddScoped<IChatRAGService, ChatRAGService>();

// 4. Đăng ký Hangfire chạy trên PostgreSQL Storage
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(connectionString);
    }));

// Background Server của Hangfire để xử lý Job ngầm
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = Environment.ProcessorCount * 2;
});

// 5. Đăng ký Controllers, OpenAPI & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "Smart Document RAG API",
        Version = "v1",
        Description = "API cho He thong Tro ly Tra cuu Tai lieu Thong minh (RAG) voi .NET 10, pgvector va Hangfire"
    });
});

var app = builder.Build();

// Configure Middleware Pipeline
app.UseCors("AllowAll");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Smart Document RAG API v1");
    });
}

// Bật Hangfire Dashboard tại /hangfire
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    DashboardTitle = "RAG Background Jobs Dashboard"
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

app.Run();