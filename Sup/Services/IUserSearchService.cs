using System.Threading.Tasks;
using Sup.Models;

namespace Sup.Services
{
    public interface IUserSearchService
    {
        Task<SearchUsersResponse?> SearchAsync(string query, int page = 0, int size = 8);
        Task<uint?> GetUserIdByNameAsync(string username);
        Task<UserDto?> GetUserByIdAsync(uint userId);
    }
}