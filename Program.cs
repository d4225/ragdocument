using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Thêm dịch vụ Session phục vụ cho việc quản lý tài khoản đăng nhập
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Thời gian hết hạn session
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Đăng ký Controllers + Views (MVC)
builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Đăng ký PostgreSQL + pgvector
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        o => o.UseVector()
    ));

// Đăng ký Endpoints & Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Đăng ký các Custom Services
builder.Services.AddScoped<IRetrievalService, RetrievalService>();
builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddScoped<IChatRAGService, ChatRAGService>();

// Đăng ký Hangfire
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options =>
    {
        options.UseNpgsqlConnection(connectionString);
    }));

// Chạy Hangfire Server
builder.Services.AddHangfireServer();

// Đăng ký Job xử lý tài liệu nền
builder.Services.AddScoped<DocumentProcessingJob>();

var app = builder.Build();

// Swagger (Chỉ hiển thị ở môi trường Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Hangfire Dashboard
app.UseHangfireDashboard("/hangfire");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// QUAN TRỌNG: Phải đặt UseSession() sau UseRouting() và trước UseAuthorization()
app.UseSession();

app.UseAuthorization();

// Map các API Controllers
app.MapControllers();

// Cấu hình Route mặc định khởi động vào Trang Đăng nhập (Account/Login)
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();