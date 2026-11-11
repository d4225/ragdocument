using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartDocumentRAG.API.Data;
using SmartDocumentRAG.API.Models;

using SmartDocumentRAG.API.ViewModels;

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

        // GET: /Account/Profile
        [HttpGet("Profile")]
        public async Task<IActionResult> Profile()
        {
            if (!Guid.TryParse(HttpContext.Session.GetString("UserId"), out var userId))
                return RedirectToAction("Login");

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }

            return View(new AccountProfileViewModel
            {
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                University = user.University,
                Address = user.Address
            });
        }

        // POST: /Account/Profile
        [HttpPost("Profile")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(AccountProfileViewModel model)
        {
            if (!Guid.TryParse(HttpContext.Session.GetString("UserId"), out var userId))
                return RedirectToAction("Login");

            var universities = GetUniversities();
            if (!string.IsNullOrWhiteSpace(model.University) && !universities.Contains(model.University))
                ModelState.AddModelError(nameof(model.University), "Vui lòng chọn trường đại học trong danh sách.");

            var normalizedEmail = model.Email?.Trim() ?? string.Empty;
            var emailExists = await _db.Users.AnyAsync(user => user.Id != userId && user.Email == normalizedEmail);
            if (emailExists)
                ModelState.AddModelError(nameof(model.Email), "Địa chỉ email này đã được tài khoản khác sử dụng.");

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            user.FullName = NormalizeOptional(model.FullName);
            user.Email = normalizedEmail;
            user.PhoneNumber = NormalizeOptional(model.PhoneNumber);
            user.University = NormalizeOptional(model.University);
            user.Address = NormalizeOptional(model.Address);

            await _db.SaveChangesAsync();
            TempData["ProfileSuccess"] = "Thông tin cá nhân đã được cập nhật.";
            return RedirectToAction(nameof(Profile));
        }

        private static string[] GetUniversities() => ["Bưu chính", "Phenikaa", "FPT", "Bách Khoa"];

        private static string? NormalizeOptional(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // GET: /Account/Logout
        [HttpGet("Logout")]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }
    }
}
