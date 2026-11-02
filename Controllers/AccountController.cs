using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.Models;

namespace SmartDocumentRAG.API.Controllers
{
    [Route("Account")]
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _db;

        public AccountController(ApplicationDbContext db)
        {
            _db = db;
        }

        // GET: /Account/Login
        [HttpGet("Login")]
        public IActionResult Login() => View();

        // POST: /Account/Login
        [HttpPost("Login")]
        public async Task<IActionResult> Login(string username, string password)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || user.PasswordHash != password)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc mật khẩu không chính xác.");
                return View();
            }

            // Lưu Session
            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("Username", user.Username);

            // 👉 Chuyển hướng chính xác về DocumentController -> Action Index
            return RedirectToAction("Index", "Document");
        }

        // GET: /Account/Register
        [HttpGet("Register")]
        public IActionResult Register() => View();

        // POST: /Account/Register
        [HttpPost("Register")]
        public async Task<IActionResult> Register(string username, string email, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                ModelState.AddModelError(string.Empty, "Vui lòng nhập đầy đủ thông tin.");
                return View();
            }

            // Kiểm tra xem Username hoặc Email đã tồn tại chưa
            var existingUser = await _db.Users
                .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

            if (existingUser != null)
            {
                ModelState.AddModelError(string.Empty, "Tên đăng nhập hoặc Email đã tồn tại trong hệ thống.");
                return View();
            }

            // Tạo đối tượng User mới khớp hoàn toàn với model
            var newUser = new User
            {
                Username = username,
                Email = email,
                PasswordHash = password, // (Lưu ý thực tế nên dùng PasswordHasher để mã hóa bảo mật)
                Role = "User",
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(newUser);
            await _db.SaveChangesAsync();

            // Đăng ký thành công -> Chuyển hướng về trang Đăng nhập
            return RedirectToAction("Login");
        }

        // GET: /Account/Logout
        [HttpGet("Logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}