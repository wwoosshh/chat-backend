using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WebApplication1.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FriendController : ControllerBase
    {
        private readonly string _friendRequestPath;
        private readonly string _friendListPath;
        private readonly string _userFilePath;

        public FriendController()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            _friendRequestPath = Path.Combine(desktop, "FriendAdd.txt");
            _friendListPath = Path.Combine(desktop, "Friend.txt");
            _userFilePath = Path.Combine(desktop, "users.txt");

            // 파일 없으면 초기화
            if (!System.IO.File.Exists(_friendRequestPath)) System.IO.File.WriteAllText(_friendRequestPath, "[]");
            if (!System.IO.File.Exists(_friendListPath)) System.IO.File.WriteAllText(_friendListPath, "[]");
        }

        public class FriendRequest
        {
            public int HostIndex { get; set; }
            public int GetIndex { get; set; }
            public string Action { get; set; } = "Waiting"; // Waiting, Accepted, Rejected
        }

        public class FriendRelation
        {
            public int UserIndex1 { get; set; }
            public int UserIndex2 { get; set; }
        }

        #region 친구 요청 보내기
        [HttpPost("add")]
        public IActionResult SendFriendRequest([FromBody] FriendRequest request)
        {
            var requests = LoadFriendRequests();

            if (requests.Any(r => r.HostIndex == request.HostIndex && r.GetIndex == request.GetIndex && r.Action == "Waiting"))
                return BadRequest("이미 요청한 친구입니다.");

            request.Action = "Waiting";
            requests.Add(request);
            SaveFriendRequests(requests);

            return Ok("친구 요청을 보냈습니다.");
        }
        #endregion

        #region 친구 요청 목록 조회
        [HttpGet("requests")]
        public IActionResult GetFriendRequests(int userIndex)
        {
            var requests = LoadFriendRequests()
                .Where(r => r.GetIndex == userIndex && r.Action == "Waiting")
                .ToList();

            return Ok(requests);
        }
        #endregion

        #region 친구 요청 수락/거절
        [HttpPost("respond")]
        public IActionResult RespondToFriendRequest([FromBody] FriendRequest response)
        {
            var requests = LoadFriendRequests();
            var targetRequest = requests.FirstOrDefault(r => r.HostIndex == response.HostIndex && r.GetIndex == response.GetIndex && r.Action == "Waiting");

            if (targetRequest == null)
                return NotFound("해당 친구 요청을 찾을 수 없습니다.");

            if (response.Action == "Accepted")
            {
                var friends = LoadFriendRelations();

                // 이미 친구인지 확인
                if (!friends.Any(f =>
                    (f.UserIndex1 == response.HostIndex && f.UserIndex2 == response.GetIndex) ||
                    (f.UserIndex1 == response.GetIndex && f.UserIndex2 == response.HostIndex)))
                {
                    friends.Add(new FriendRelation { UserIndex1 = response.HostIndex, UserIndex2 = response.GetIndex });
                    SaveFriendRelations(friends);
                }

                // 요청 삭제
                requests.Remove(targetRequest);
                SaveFriendRequests(requests);

                return Ok("친구 요청을 수락했습니다.");
            }
            else if (response.Action == "Rejected")
            {
                requests.Remove(targetRequest);
                SaveFriendRequests(requests);
                return Ok("친구 요청을 거절했습니다.");
            }

            return BadRequest("올바르지 않은 Action 값입니다.");
        }
        #endregion
        [HttpPost("delete")]
        public IActionResult DeleteFriend([FromBody] FriendRequest request)
        {
            try
            {
                string friendFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Friend.txt");
                if (!System.IO.File.Exists(friendFilePath))
                    return NotFound("Friend.txt 파일이 존재하지 않습니다.");

                var friends = JsonConvert.DeserializeObject<List<FriendRequest>>(System.IO.File.ReadAllText(friendFilePath)) ?? new();

                // ✅ 친구 관계 삭제 (양방향 관계 모두 제거)
                friends = friends.Where(f =>
                    !(f.HostIndex == request.HostIndex && f.GetIndex == request.GetIndex) &&
                    !(f.HostIndex == request.GetIndex && f.GetIndex == request.HostIndex)
                ).ToList();

                System.IO.File.WriteAllText(friendFilePath, JsonConvert.SerializeObject(friends, Formatting.Indented));

                return Ok("친구 삭제 완료");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"서버 오류: {ex.Message}");
            }
        }

        #region 친구 목록 조회
        [HttpGet("list")]
        public IActionResult GetFriendList(int userIndex)
        {
            var friends = LoadFriendRelations()
                .Where(f => f.UserIndex1 == userIndex || f.UserIndex2 == userIndex)
                .Select(f => f.UserIndex1 == userIndex ? f.UserIndex2 : f.UserIndex1)
                .ToList();

            return Ok(friends);
        }
        #endregion

        #region 내부 파일 처리
        private List<FriendRequest> LoadFriendRequests()
        {
            var json = System.IO.File.ReadAllText(_friendRequestPath);
            return JsonConvert.DeserializeObject<List<FriendRequest>>(json) ?? new();
        }

        private void SaveFriendRequests(List<FriendRequest> requests)
        {
            System.IO.File.WriteAllText(_friendRequestPath, JsonConvert.SerializeObject(requests, Formatting.Indented));
        }

        private List<FriendRelation> LoadFriendRelations()
        {
            var json = System.IO.File.ReadAllText(_friendListPath);
            return JsonConvert.DeserializeObject<List<FriendRelation>>(json) ?? new();
        }

        private void SaveFriendRelations(List<FriendRelation> relations)
        {
            System.IO.File.WriteAllText(_friendListPath, JsonConvert.SerializeObject(relations, Formatting.Indented));
        }
        #endregion
    }
}
