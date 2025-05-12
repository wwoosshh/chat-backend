using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileController : ControllerBase
    {
        private readonly string _uploadPath = @"E:\이미지저장용";

        [HttpPost("upload")]
        public async Task<IActionResult> UploadFile([FromQuery] string roomId, [FromQuery] string senderId, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("파일이 없습니다.");

            Directory.CreateDirectory(_uploadPath);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(_uploadPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            string fileUrl = $"http://nunconnect.duckdns.org:5159/images/{fileName}";

            // ✅ 채팅 로그 저장 (유저 요구사항에 맞게 바탕화면 chatdata 폴더로 저장)
            SaveChatMessage(roomId, senderId, fileUrl);

            // ✅ SignalR 알림
            var hubContext = HttpContext.RequestServices.GetService<IHubContext<ChatHub>>();
            await hubContext.Clients.Group(roomId).SendAsync("ReceiveImage", senderId, fileUrl);

            return Ok(new { Url = fileUrl });
        }

        // ✅ 파일 다운로드 API (File() 호출 정상 작동)
        [HttpGet("download")]
        public IActionResult DownloadFile([FromQuery] string fileName)
        {
            var filePath = Path.Combine(_uploadPath, fileName);
            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var content = System.IO.File.ReadAllBytes(filePath);
            var contentType = "application/octet-stream";

            return File(content, contentType, fileName); // ControllerBase.File() 호출
        }

        private void SaveChatMessage(string roomId, string senderId, string message)
        {
            string chatLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "chatdata");
            Directory.CreateDirectory(chatLogDir);

            string chatLogFile = Path.Combine(chatLogDir, $"{roomId}.txt");

            List<ChatMessage> chatHistory = new();
            if (System.IO.File.Exists(chatLogFile))
            {
                var json = System.IO.File.ReadAllText(chatLogFile);
                chatHistory = JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new();
            }

            chatHistory.Add(new ChatMessage
            {
                RoomId = roomId,
                Sender = senderId,
                Message = message,
                Timestamp = DateTime.Now
            });

            System.IO.File.WriteAllText(chatLogFile, JsonConvert.SerializeObject(chatHistory, Formatting.Indented));
        }

        private class ChatMessage
        {
            public string RoomId { get; set; } = string.Empty;
            public string Sender { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public DateTime Timestamp { get; set; }
        }
    }
}
