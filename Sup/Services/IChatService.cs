using System.Collections.Generic;
using System.Threading.Tasks;
using Sup.Models;

namespace Sup.Services
{
    public interface IChatService
    {
        Task<uint> InitializeUserAsync(string username);
        Task<List<ChatDto>> GetUserChatsAsync();
        Task<List<ChatDto>> LoadChatDetailsAsync(List<ChatInfoDto> chatInfos);
        Task<uint?> CreateChatAsync(uint otherUserId);
        Task<uint?> CreateGroupChatAsync(IEnumerable<uint> memberIds);
        Task<uint> GetOtherUserIdForChat(uint chatId);
        Task<List<ChatParticipantDto>> GetChatMembersAsync(uint chatId);
        Task<List<ChatParticipantDto>> GetChatParticipantsAsync(uint chatId);
        Task<bool> AddUsersToChatAsync(uint chatId, IEnumerable<uint> userIds);
        Task<List<MessageDto>> LoadChatHistoryAsync(uint chatId, int page = 1, int pageSize = 20);
        Task<MessageDto?> SendMessageAsync(uint chatId, string content);
        Task<string?> GetUserNameByIdAsync(uint userId);
        Task<uint?> GetUserIdByNameAsync(string name);
        void PreCacheChatInfo(uint chatId, uint otherUserId, string username);

        /// <summary>
        /// Этот метод вызывается периодически UI для отслеживания изменений
        /// в списке чатов. Должен возвращать true если список чатов изменился.
        /// </summary>
        Task<bool> PollForChatListChangesAsync();
    }
}
