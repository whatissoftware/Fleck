using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;

namespace Fleck
{
    public class SocketWrapper : ISocket
    {
    
        public const UInt32 KeepAliveInterval = 60000;
        public const UInt32 RetryInterval = 10000;
    
        private readonly Socket _socket;
        private Stream _stream;

        public string RemoteIpAddress
        {
            get
            {
                var endpoint = _socket.RemoteEndPoint as IPEndPoint;
                return endpoint != null ? endpoint.Address.ToString() : null;
            }
        }

        public int RemotePort
        {
            get
            {
                var endpoint = _socket.RemoteEndPoint as IPEndPoint;
                return endpoint != null ? endpoint.Port : -1;
            }
        }

        public void SetKeepAlive(Socket socket, UInt32 keepAliveInterval, UInt32 retryInterval)
        {
            int size = sizeof(UInt32);
            UInt32 on = 1;

            byte[] inArray = new byte[size * 3];
            Array.Copy(BitConverter.GetBytes(on), 0, inArray, 0, size);
            Array.Copy(BitConverter.GetBytes(keepAliveInterval), 0, inArray, size, size);
            Array.Copy(BitConverter.GetBytes(retryInterval), 0, inArray, size * 2, size);
            socket.IOControl(IOControlCode.KeepAliveValues, inArray, null);
        }

        public SocketWrapper(Socket socket)
        {
            _socket = socket;
            if (_socket.Connected)
                _stream = new NetworkStream(_socket);

            // The tcp keepalive default values on most systems
            // are huge (~7200s). Set them to something more reasonable.
#if NET45
            SetKeepAlive(socket, KeepAliveInterval, RetryInterval);
#else
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                SetKeepAlive(socket, KeepAliveInterval, RetryInterval);
            }
#endif

        }

        public Task Authenticate(X509Certificate2 certificate, SslProtocols enabledSslProtocols, Action callback, Action<Exception> error)
        {
            var ssl = new SslStream(_stream, false);
            _stream = new QueuedStream(ssl);
            Func<AsyncCallback, object, IAsyncResult> begin =
                (cb, s) => ssl.BeginAuthenticateAsServer(certificate, false, enabledSslProtocols, false, cb, s);

            Task task = Task.Factory.FromAsync(begin, ssl.EndAuthenticateAsServer, null);
            task.ContinueWith(t => callback(), TaskContinuationOptions.NotOnFaulted)
                .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

            return task;
        }

        public void Listen(int backlog)
        {
            _socket.Listen(backlog);
        }

        public void Bind(EndPoint endPoint)
        {
            _socket.Bind(endPoint);
        }

        public bool Connected
        {
            get { return _socket.Connected; }
        }

        public Stream Stream
        {
            get { return _stream; }
        }

        public bool NoDelay
        {
            get { return _socket.NoDelay; }
            set { _socket.NoDelay = value; }
        }

        public EndPoint LocalEndPoint
        {
            get { return _socket.LocalEndPoint; }
        }

        public void Receive(byte[] buffer, Action<int> callback, Action<Exception> error, int offset)
        {
            int readLength = 0;
            try
            {
                bool bReadable = _socket.Poll(0, SelectMode.SelectRead);
                if (bReadable)
                {
                    //此处offset默认为0，可以考虑优化_socket.Receive(m_recvBuffer, m_alreadyRecvLength, m_recvBuffer.Length - m_alreadyRecvLength,SocketFlags.None);
                    readLength = _stream.Read(buffer,offset, buffer.Length);
                }  
            }
            catch (Exception e)
            {
                error(e);
                return ;
            }

            callback(readLength);
        }

        public void Accept(Action<ISocket> callback, Action<Exception> error)
        {
            bool canAccept = false;
            try
            {
                canAccept = _socket.Poll(0, SelectMode.SelectRead);
            }
            catch (Exception e)
            {
                error(e);
                return;
            }
            if (canAccept)
            {
                Socket acceptSocket = _socket.Accept();
                //TODO 非阻塞socket不能放到Stream里
                //acceptSocket.Blocking = false;

                //禁用TCP小包缓存Linger
                acceptSocket.LingerState.Enabled = false;
                acceptSocket.LingerState.LingerTime = 0;
                SocketWrapper clientSocket = new SocketWrapper(acceptSocket);

                callback(clientSocket);
            }
        }

        public void Dispose()
        {
            if (_stream != null) _stream.Dispose();
            if (_socket != null) _socket.Dispose();
        }

        public void Close()
        {
            if (_stream != null) _stream.Close();
            if (_socket != null) _socket.Close();
        }

        public int EndSend(IAsyncResult asyncResult)
        {
            _stream.EndWrite(asyncResult);
            return 0;
        }

        public void Send(byte[] buffer, Action callback, Action<Exception> error)
        {

            try
            {
                bool bWriteAble = _socket.Poll(0, SelectMode.SelectWrite);

                if (bWriteAble)
                {

                    _stream.Write(buffer, 0, buffer.Length);
                    callback();
                }
                else {
                    //TODO 如果不能写，放入缓冲区
                    error(new IOException("Socket 不能进行写操作:" + RemoteIpAddress));
                }


            }
            catch (Exception e)
            {
                error(e);
                return;
            }
        }
    }
}
