using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Sup.Models
{
    public class SearchResultItemDto : INotifyPropertyChanged
    {
        private Avalonia.Media.Imaging.Bitmap? _avatarBitmap;

        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }

        public Avalonia.Media.Imaging.Bitmap? AvatarBitmap
        {
            get => _avatarBitmap;
            set
            {
                if (_avatarBitmap != value)
                {
                    _avatarBitmap = value;
                    OnPropertyChanged();
                }
            }
        }

        public string FriendshipStatus { get; set; } = "none";
        public bool IsFriend { get; set; }
        public bool HasIncomingRequest { get; set; }
        public bool HasOutgoingRequest { get; set; }

        // Свойство для определения видимости кнопки "Добавить в друзья"
        public bool CanAddFriend => !IsFriend && !HasIncomingRequest && !HasOutgoingRequest;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

