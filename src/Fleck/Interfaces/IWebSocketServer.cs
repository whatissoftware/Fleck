using System;

namespace Fleck
{
    public interface IWebSocketServer : IDisposable
    {
        void Start(Action<IWebSocketConnection> config);

        void Update();

        void Close(IWebSocketConnection connection);
    }
}
