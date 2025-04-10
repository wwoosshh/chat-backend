using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace WebApplication1.Controllers
{
    
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly string _userFilePath;
        private static Dictionary<string, string> RefreshTokens = new();

        public UserController(IConfiguration config)
        {
            _config = config;
            #if DEBUG
            _userFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "users.txt");
            #else
            _userFilePath = Path.Combine(Directory.GetCurrentDirectory(), "users.txt");
            #endif
        }

        [Authorize]
        [HttpGet("secret")]
        public IActionResult GetSecret()
        {
            return Ok("이건 인증된 사용자만 볼 수 있는 정보야!");
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserData user)
        {
            try
            {
                // ngrok-url.txt 위치: 바탕화면
                #if DEBUG
                string ngrokUrlPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "ngrok-url.txt");
                #else
                string ngrokUrlPath = "/etc/ngrok-url.txt"; // 예: 리눅스 서버에 직접 지정된 경로
                #endif

                Console.WriteLine("NGROK 경로: " + ngrokUrlPath); // 추가

                if (!System.IO.File.Exists(ngrokUrlPath))
                    return StatusCode(500, "ngrok-url.txt 파일을 찾을 수 없습니다.");

                // 파일에서 ngrok 주소 읽기
                string baseUrl = System.IO.File.ReadAllText(ngrokUrlPath).Trim();

                if (string.IsNullOrWhiteSpace(baseUrl))
                    return StatusCode(500, "ngrok-url.txt에 유효한 ngrok 주소가 없습니다.");

                // URL 조합 시 이중 슬래시 방지
                string fullUrl = $"{baseUrl.TrimEnd('/')}/api/User/register";

                // HTTP 요청
                using var client = new HttpClient();
                var content = new StringContent(JsonConvert.SerializeObject(user), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(fullUrl, content);

                // 응답 처리
                if (response.IsSuccessStatusCode)
                    return Ok("회원가입 성공");
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, $"ngrok 서버 응답 오류: {error}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"서버 내 오류: {ex.Message}");
            }
        }
        public class UserData
        {
            public string Id { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string ProfileImage { get; set; } = string.Empty;
            public string StatusMessage { get; set; } = string.Empty;
            public List<string> JoinedRoomIds { get; set; } = new();
        }

        public class LoginRequest
        {
            public string Id { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest login)
        {
            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Id == login.Id && u.Password == login.Password);

            if (user == null)
                return Unauthorized("아이디 또는 비밀번호가 틀렸습니다.");

            string jwtToken = GenerateJwtToken(user);
            string refreshToken = Guid.NewGuid().ToString();
            RefreshTokens[user.Id] = refreshToken;

            return Ok(new { Token = jwtToken, RefreshToken = refreshToken });
        }

        [HttpPost("refresh")]
        public IActionResult RefreshToken([FromBody] dynamic body)
        {
            string userId = body.userId;
            string oldRefreshToken = body.refreshToken;

            if (!RefreshTokens.TryGetValue(userId, out string? storedToken) || storedToken != oldRefreshToken)
                return Unauthorized("유효하지 않은 리프레시 토큰입니다.");

            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
                return Unauthorized();

            string newJwt = GenerateJwtToken(user);
            string newRefresh = Guid.NewGuid().ToString();
            RefreshTokens[user.Id] = newRefresh;

            return Ok(new { Token = newJwt, RefreshToken = newRefresh });
        }

        private string GenerateJwtToken(UserData user)
        {
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Name),
                new Claim(JwtRegisteredClaimNames.Email, user.Id),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JwtSettings:SecretKey"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["JwtSettings:Issuer"],
                audience: _config["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: creds);


            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}