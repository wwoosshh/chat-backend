using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static WebApplication1.Controllers.UserController;

public class ChatHub : Hub
{
    private readonly string _userFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "users.txt");

    public async Task JoinRoom(string roomId, string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        RegisterUserRoom(userId, roomId);
    }

    public async Task SendMessage(string roomId, string userId, string message)
    {
        var chatMessage = new ChatMessage
        {
            RoomId = roomId,
            Sender = userId,
            Message = message,
            Timestamp = DateTime.Now
        };

        SaveMessage(chatMessage);
        await Clients.Group(roomId).SendAsync("ReceiveMessage", userId, message);
    }
    public async Task SendImage(string roomId, string senderId, string imageUrl)
    {
        await Clients.Group(roomId).SendAsync("ReceiveImage", senderId, imageUrl);
    }
    private void RegisterUserRoom(string userId, string roomId)
    {
        if (!File.Exists(_userFilePath)) return;

        var users = JsonConvert.DeserializeObject<List<UserData>>(File.ReadAllText(_userFilePath)) ?? new();
        var user = users.FirstOrDefault(u => u.Id == userId);

        if (user != null && !user.JoinedRoomIds.Contains(roomId))
        {
            user.JoinedRoomIds.Add(roomId);
            File.WriteAllText(_userFilePath, JsonConvert.SerializeObject(users, Formatting.Indented));
        }
    }

    private void SaveMessage(ChatMessage message)
    {
        string chatDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "chatdata");
        Directory.CreateDirectory(chatDataDir);

        string filePath = Path.Combine(chatDataDir, $"{message.RoomId}.txt");
        List<ChatMessage> messages = new();

        if (File.Exists(filePath))
        {
            messages = JsonConvert.DeserializeObject<List<ChatMessage>>(File.ReadAllText(filePath)) ?? new();
        }

        messages.Add(message);
        File.WriteAllText(filePath, JsonConvert.SerializeObject(messages, Formatting.Indented));
    }
}

public class ChatMessage
{
    public string RoomId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
