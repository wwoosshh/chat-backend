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
using System.Net.Mail;
using System.Net;
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
        private readonly string _chatListFilePath;
        private readonly string _chatDataDirPath;
        private static Dictionary<string, string> RefreshTokens = new();

        public UserController(IConfiguration config)
        {
            _config = config;
#if DEBUG
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _userFilePath = Path.Combine(desktop, "users.txt");
            _chatListFilePath = Path.Combine(desktop, "chatlist.txt");
            _chatDataDirPath = Path.Combine(desktop, "chatdata");
#else
            _userFilePath = Path.Combine(Directory.GetCurrentDirectory(), "users.txt");
            _chatListFilePath = Path.Combine(Directory.GetCurrentDirectory(), "chatlist.txt");
            _chatDataDirPath = Path.Combine(Directory.GetCurrentDirectory(), "chatdata");
#endif
            Directory.CreateDirectory(_chatDataDirPath);
        }

        [Authorize]
        [HttpGet("secret")]
        public IActionResult GetSecret()
        {
            return Ok("이건 인증된 사용자만 볼 수 있는 정보야!");
        }
        [HttpGet("checkVersion")]
        public IActionResult CheckVersion()
        {
            const string latestVersion = "1.3.0"; // 여기에 최신 버전 입력
            return Ok(new { Version = latestVersion });
        }

        [HttpPost("register")]
        public IActionResult Register([FromBody] UserRegisterRequest req)
        {
            string userFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "users.txt");
            if (!System.IO.File.Exists(userFilePath))
                System.IO.File.WriteAllText(userFilePath, "[]");

            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(userFilePath)) ?? new();

            if (users.Any(u => u.Email.Equals(req.Email, StringComparison.OrdinalIgnoreCase)))
                return BadRequest("이미 사용 중인 이메일입니다.");

            if (users.Any(u => u.Id.Equals(req.Id, StringComparison.OrdinalIgnoreCase)))
                return BadRequest("이미 존재하는 아이디입니다.");

            int maxIndex = users.Any() ? users.Max(u => u.Index) : 0;

            var newUser = new UserData
            {
                Index = maxIndex + 1,
                Id = req.Id,
                Password = req.Password,
                Name = req.Name,
                Email = req.Email,
                StatusMessage = "",
                ProfileImage = "",
                JoinedRoomIds = new(),
                EmailConfirmed = false
            };

            users.Add(newUser);
            System.IO.File.WriteAllText(userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));

            return Ok("회원가입 성공. 이메일을 확인해주세요.");
        }
        [HttpGet("getUserNameByIndex")]
        public IActionResult GetUserNameByIndex(int index)
        {
            if (!System.IO.File.Exists(_userFilePath))
                return NotFound("유저 파일이 없습니다.");

            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Index == index);

            if (user == null)
                return NotFound("해당 유저를 찾을 수 없습니다.");

            return Ok(user.Name); // ✅ 닉네임만 반환
        }
        [HttpPost("checkEmailConfirmed")]
        public IActionResult CheckEmailConfirmed([FromBody] EmailRequest req)
        {
            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Email == req.Email);

            if (user != null && user.EmailConfirmed)
                return Ok();

            return BadRequest("이메일 인증이 완료되지 않았습니다.");
        }
        private void SendVerificationEmail(string toEmail, string token)
        {
            var fromAddress = new MailAddress("wwoosshh1234@naver.com", "Connect"); // 네이버 이메일 주소
            var toAddress = new MailAddress(toEmail);
            const string fromPassword = "4YLYY4MQ12T1"; // 네이버에서 생성한 앱 비밀번호로 교체
            const string subject = "Connect 이메일 인증";
            string body = $"아래 링크를 클릭해 이메일 인증을 완료하세요:\n\n" +
                          $"http://nunconnect.duckdns.org:5159/api/User/verifyEmail?token={token}";

            var smtp = new SmtpClient
            {
                Host = "smtp.naver.com",      // ✅ 네이버 SMTP 서버
                Port = 587,                   // ✅ STARTTLS 사용 (권장)
                EnableSsl = true,              // ✅ TLS 사용 (필수)
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, fromPassword)
            };

            using var message = new MailMessage(fromAddress, toAddress)
            {
                Subject = subject,
                Body = body
            };

            smtp.Send(message);
        }
        [HttpGet("verifyEmail")]
        public IActionResult VerifyEmail(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidIssuer = _config["JwtSettings:Issuer"],
                    ValidAudience = _config["JwtSettings:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JwtSettings:SecretKey"]!))
                };

                var principal = handler.ValidateToken(token, validationParameters, out _);
                var userId = principal.FindFirst("UserId")?.Value;

                var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
                var user = users.FirstOrDefault(u => u.Id == userId);

                if (user != null)
                {
                    user.EmailConfirmed = true; // ✅ 이메일 인증 성공 처리
                    System.IO.File.WriteAllText(_userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));
                    return Ok("이메일 인증이 완료되었습니다.");
                }

                return BadRequest("잘못된 사용자 정보입니다.");
            }
            catch (SecurityTokenExpiredException)
            {
                return BadRequest("인증 링크가 만료되었습니다. 새 인증 요청을 해주세요.");
            }
            catch (Exception ex)
            {
                return BadRequest($"인증 실패: {ex.Message}");
            }
        }
        [HttpPost("resendVerification")]
        public IActionResult ResendVerification([FromBody] EmailRequest req)
        {
            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Email == req.Email);

            if (user == null)
                return NotFound("해당 이메일로 등록된 사용자가 없습니다.");

            if (user.EmailConfirmed)
                return Ok("이미 이메일 인증이 완료되었습니다.");

            string token = GenerateEmailVerificationToken(user);
            SendVerificationEmail(user.Email, token); // 📧 이메일 재발송

            return Ok("인증 메일을 다시 발송했습니다.");
        }

        public class EmailRequest
        {
            public string Email { get; set; } = string.Empty;
        }

        private string GenerateEmailVerificationToken(UserData user)
        {
            var claims = new[]
            {
        new Claim(JwtRegisteredClaimNames.Sub, user.Email),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        new Claim("UserId", user.Id)
    };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JwtSettings:SecretKey"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _config["JwtSettings:Issuer"],
                audience: _config["JwtSettings:Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(5), // ⏰ 5분 유효
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public class UserRegisterRequest
        {
            public string Id { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty; // 이미 클라이언트에서 해싱됨
            public string Name { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
        }

        [HttpPost("login")]
        public IActionResult Login([FromBody] LoginRequest login)
        {
            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Id == login.Id && u.Password == login.Password);

            if (user == null)
                return Unauthorized("아이디 또는 비밀번호가 틀렸습니다.");

            if (!user.EmailConfirmed)
                return Unauthorized("이메일 인증이 완료되지 않았습니다.");

            string jwtToken = GenerateJwtToken(user);
            string refreshToken = Guid.NewGuid().ToString();
            RefreshTokens[user.Id] = refreshToken;

            return Ok(user);
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

        [HttpGet("verifyRoomPassword")]
        public IActionResult VerifyRoomPassword(string roomId, string roomName, string password)
        {
            if (!System.IO.File.Exists(_chatListFilePath))
                return NotFound("채팅방 데이터가 없습니다.");

            var rooms = JsonConvert.DeserializeObject<List<ChatRoom>>(System.IO.File.ReadAllText(_chatListFilePath)) ?? new();
            var room = rooms.FirstOrDefault(r => r.RoomId == roomId && r.Name == roomName && r.Password == password);

            return Ok(room != null);
        }

        [HttpPost("deleteRooms")]
        public IActionResult DeleteRooms([FromBody] List<string> roomIds)
        {
            try
            {
                if (!System.IO.File.Exists(_chatListFilePath))
                    return NotFound("채팅방 파일이 없습니다.");

                // 1. 채팅방 목록에서 삭제
                var rooms = JsonConvert.DeserializeObject<List<ChatRoom>>(System.IO.File.ReadAllText(_chatListFilePath)) ?? new();
                rooms = rooms.Where(r => !roomIds.Contains(r.RoomId)).ToList();
                System.IO.File.WriteAllText(_chatListFilePath, JsonConvert.SerializeObject(rooms, Formatting.Indented));

                // 2. 유저 데이터에서 삭제된 방 ID 제거
                var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
                foreach (var user in users)
                {
                    user.JoinedRoomIds = user.JoinedRoomIds.Where(id => !roomIds.Contains(id)).ToList();
                }
                System.IO.File.WriteAllText(_userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));

                // 3. 채팅방 파일 삭제
                foreach (var roomId in roomIds)
                {
                    string chatFile = Path.Combine(_chatDataDirPath, $"{roomId}.txt");
                    if (System.IO.File.Exists(chatFile))
                    {
                        System.IO.File.Delete(chatFile);
                    }
                }

                return Ok("선택한 채팅방 삭제 완료");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"서버 오류: {ex.Message}");
            }
        }

        [HttpPatch("update")]
        public IActionResult UpdateUser([FromBody] UserData updatedUser)
        {
            if (!System.IO.File.Exists(_userFilePath))
                return NotFound("유저 파일이 없습니다.");

            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var match = users.FirstOrDefault(u => u.Id == updatedUser.Id);

            if (match == null)
                return NotFound("해당 유저를 찾을 수 없습니다.");

            match.Name = updatedUser.Name;
            match.StatusMessage = updatedUser.StatusMessage;
            match.ProfileImage = updatedUser.ProfileImage;

            System.IO.File.WriteAllText(_userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));
            return Ok("업데이트 완료");
        }
        [HttpPost("createRoom")]
        public IActionResult CreateRoom([FromBody] CreateRoomRequest req)
        {
            try
            {
                // 1. 채팅방 정보 저장
                var rooms = new List<ChatRoom>();
                if (System.IO.File.Exists(_chatListFilePath))
                {
                    rooms = JsonConvert.DeserializeObject<List<ChatRoom>>(System.IO.File.ReadAllText(_chatListFilePath)) ?? new();
                }

                if (rooms.Any(r => r.RoomId == req.RoomId))
                    return BadRequest("이미 존재하는 채팅방 ID입니다.");

                rooms.Add(new ChatRoom
                {
                    RoomId = req.RoomId,
                    Name = req.RoomName,
                    Password = req.Password
                });

                System.IO.File.WriteAllText(_chatListFilePath, JsonConvert.SerializeObject(rooms, Formatting.Indented));

                // 2. 사용자 정보에 해당 채팅방 ID 추가
                if (System.IO.File.Exists(_userFilePath))
                {
                    var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
                    var user = users.FirstOrDefault(u => u.Id == req.UserId);

                    if (user != null && !user.JoinedRoomIds.Contains(req.RoomId))
                    {
                        user.JoinedRoomIds.Add(req.RoomId);
                        System.IO.File.WriteAllText(_userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));
                    }
                }

                return Ok(new { RoomId = req.RoomId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"서버 오류: {ex.Message}");
            }
        }


        [HttpPost("saveMessage")]
        public IActionResult SaveMessage([FromBody] ChatMessage message)
        {
            string filePath = Path.Combine(_chatDataDirPath, message.RoomId + ".txt");
            List<ChatMessage> messages = new();

            if (System.IO.File.Exists(filePath))
            {
                var json = System.IO.File.ReadAllText(filePath);
                messages = JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new();
            }

            messages.Add(message);
            System.IO.File.WriteAllText(filePath, JsonConvert.SerializeObject(messages, Formatting.Indented));
            return Ok("메시지 저장 완료");
        }
        [HttpGet("loadMessages")]
        public IActionResult LoadMessages(string roomId)
        {
            try
            {
                string filePath = Path.Combine(_chatDataDirPath, roomId + ".txt");
                if (!System.IO.File.Exists(filePath))
                    return Ok(new List<ChatMessage>()); // 없으면 빈 리스트 반환

                var json = System.IO.File.ReadAllText(filePath);
                var messages = JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new();
                return Ok(messages);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"서버 오류: {ex.Message}");
            }
        }
        [HttpPost("joinRoom")]
        public IActionResult JoinRoom([FromBody] JoinRoomRequest req)
        {
            try
            {
                if (!System.IO.File.Exists(_chatListFilePath) || !System.IO.File.Exists(_userFilePath))
                    return NotFound("채팅방 데이터 또는 유저 데이터가 없습니다.");

                var rooms = JsonConvert.DeserializeObject<List<ChatRoom>>(System.IO.File.ReadAllText(_chatListFilePath)) ?? new();
                var matchRoom = rooms.FirstOrDefault(r => r.Name == req.RoomName && r.Password == req.Password);

                if (matchRoom == null)
                    return BadRequest("채팅방 이름 또는 비밀번호가 일치하지 않습니다.");

                var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
                var user = users.FirstOrDefault(u => u.Id == req.UserId);

                if (user != null && !user.JoinedRoomIds.Contains(matchRoom.RoomId))
                {
                    user.JoinedRoomIds.Add(matchRoom.RoomId);
                    System.IO.File.WriteAllText(_userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));
                }

                return Ok(new { RoomId = matchRoom.RoomId });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"서버 오류: {ex.Message}");
            }
        }
        public class JoinRoomRequest
        {
            public string RoomName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
        }


        public class CreateRoomRequest
        {
            public string RoomId { get; set; } = string.Empty;
            public string RoomName { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
        }

        public class ChatRoom
        {
            public string RoomId { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        public class ChatMessage
        {
            public string RoomId { get; set; } = string.Empty;
            public string Sender { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
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
        [HttpGet("getUser")]
        public IActionResult GetUser(string userId)
        {
            if (!System.IO.File.Exists(_userFilePath))
                return NotFound("유저 파일이 없습니다.");

            var users = JsonConvert.DeserializeObject<List<UserData>>(System.IO.File.ReadAllText(_userFilePath)) ?? new();
            var user = users.FirstOrDefault(u => u.Id == userId);

            if (user == null)
                return NotFound("해당 유저를 찾을 수 없습니다.");

            return Ok(user);
        }

        [HttpGet("getChatList")]
        public IActionResult GetChatList()
        {
            if (!System.IO.File.Exists(_chatListFilePath))
                return Ok(new List<ChatRoom>());

            var rooms = JsonConvert.DeserializeObject<List<ChatRoom>>(System.IO.File.ReadAllText(_chatListFilePath)) ?? new();
            return Ok(rooms);
        }



        public class UserData
        {
            public int Index { get; set; }
            public string Id { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string ProfileImage { get; set; } = string.Empty;
            public string StatusMessage { get; set; } = string.Empty;
            public List<string> JoinedRoomIds { get; set; } = new();
            public bool EmailConfirmed { get; set; } = false;
        }

        public class LoginRequest
        {
            public string Id { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }
    }
}
