using Microsoft.AspNetCore.SignalR;

namespace Elysium.WorkStation.Hubs
{
    public class WorkStationHub : Hub
    {
        public async Task SendMessage(string user, string message)
            => await Clients.All.SendAsync("ReceiveMessage", user, message);

        public async Task Broadcast(string eventName, object payload)
            => await Clients.All.SendAsync(eventName, payload);

        public async Task ClipboardSync(string text, string senderName)
            => await Clients.Others.SendAsync("ReceiveClipboard", text, senderName);
    }
}
