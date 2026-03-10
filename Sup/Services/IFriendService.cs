using System.Collections.Generic;
using System.Threading.Tasks;
using Sup.Models;

namespace Sup.Services
{
    public interface IFriendService
    {
        Task<List<FriendshipDto>?> GetFriendsAsync(uint userId);
        Task<FriendshipStatusDto?> CheckFriendshipStatusAsync(uint userId, uint targetId);
        Task<bool> SendFriendRequestAsync(uint userId, uint targetId);
        Task<bool> AcceptFriendRequestAsync(uint userId, uint friendId);
        Task<bool> RejectFriendRequestAsync(uint userId, uint friendId);
        Task<bool> CancelFriendRequestAsync(uint userId, uint friendId);
        Task<bool> RemoveFriendAsync(uint userId, uint friendId);
        Task<List<FriendshipDto>?> GetIncomingFriendRequestsAsync(uint userId);
        Task<List<FriendshipDto>?> GetOutgoingFriendRequestsAsync(uint userId);
    }
}
