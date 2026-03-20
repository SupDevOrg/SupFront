using Sup.Models;
using System.Threading.Tasks;

namespace Sup.Services
{
    public interface IUserAvatarService
    {
        /// <summary>
        /// Получить информацию текущего пользователя
        /// </summary>
        Task<UserDto> GetCurrentUserAsync();

        /// <summary>
        /// Получить URL для загрузки аватарки
        /// </summary>
        Task<AvatarUploadUrlResponse> GetAvatarUploadUrlAsync(string contentType, string fileName);

        /// <summary>
        /// Загрузить аватарку на сервер
        /// </summary>
        Task<bool> UploadAvatarAsync(string uploadUrl, byte[] imageData, string contentType);

        /// <summary>
        /// Масштабировать изображение до 512x512
        /// </summary>
        byte[] ResizeImageTo512(string filePath);
    }
}
