using System;
using System.Threading.Tasks;
using Sup.Models;

namespace Sup.Services
{
    public interface INotificationService
    {
        Task StartAsync(uint userId);
        void Stop();
        event EventHandler<NotificationDto>? OnNotificationReceived;
    }
}