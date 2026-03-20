using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Sup.Models
{
    public class FriendListItemDto : INotifyPropertyChanged
    {
        public uint Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }

        private Avalonia.Media.Imaging.Bitmap? _avatarBitmap;
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
