using System;
using System.Collections.Generic;

namespace Sup.Models
{
    public class ChatDto
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public DateTime LastMessageTime { get; set; }
        public uint OtherUserId { get; set; }
        public bool IsPending { get; set; } = false;
    }
}