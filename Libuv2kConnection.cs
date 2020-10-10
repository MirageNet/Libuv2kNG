#region Statements

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using libuv2k;
using libuv2k.Native;
using UnityEngine;

#endregion

namespace Mirror.Libuv2kNG
{
    public class Libuv2kConnection : IConnection
    {
        #region Fields

        private readonly ConcurrentQueue<byte[]> _queuedData = new ConcurrentQueue<byte[]>();
        private TcpStream _client;
        private readonly Loop _clientLoop;
        private TaskCompletionSource<Task> _connectedComplete;
        private readonly CancellationTokenSource _cancellationToken;

        #endregion

        #region Class Specific

        /// <summary>
        ///     Initialize new <see cref="Libuv2kConnection"/>.
        /// </summary>
        /// <param name="noDelay"></param>
        /// <param name="tickRate">The rate at which we will delay before processing more data.</param>
        /// <param name="client"></param>
        public Libuv2kConnection(bool noDelay, int tickRate, TcpStream client = null)
        {
            _cancellationToken = new CancellationTokenSource();

            if (client == null)
            {
                _clientLoop = new Loop();
                _client = new TcpStream(_clientLoop);

                _ = Task.Run(() => Tick(tickRate), _cancellationToken.Token);
            }
            else
            {
                _client = client;

                // setup callbacks
                _client.onMessage = OnLibuvClientMessage;
                _client.onError = OnLibuvClientError;
                _client.onClosed = OnLibuvClientClosed;
            }

            _client.NoDelay(noDelay);
        }

        /// <summary>
        ///     Connect to server.
        /// </summary>
        /// <param name="uri">The server <see cref="Uri"/> to connect to.</param>
        /// <returns>Returns back a new <see cref="Libuv2kConnection"/> when connected or null when failed.</returns>
        public async Task<IConnection> ConnectAsync(Uri uri)
        {
            try
            {
                // libuv doesn't resolve host name, and it needs ipv4.
                if (LibuvUtils.ResolveToIPV4(uri.Host, out IPAddress address))
                {
                    // connect client
                    var localEndPoint = new IPEndPoint(IPAddress.Any, IPEndPoint.MinPort);
                    var remoteEndPoint = new IPEndPoint(address, uri.Port);

                    Libuv2kNGLogger.Log("Libuv client connecting to: " + address + ":" + uri.Port);

                    _client.ConnectTo(localEndPoint, remoteEndPoint, ConnectedAction);
                }
                else
                {
                    Libuv2kNGLogger.Log("Libuv client connect: no IPv4 found for hostname: " + uri.Host, LogType.Warning);
                }

                _connectedComplete = new TaskCompletionSource<Task>();
                Task connectedCompleteTask = _connectedComplete.Task;

                while (await Task.WhenAny(connectedCompleteTask,
                           Task.Delay(TimeSpan.FromSeconds(Math.Max(1, 30)))) !=
                       connectedCompleteTask)
                {
                    Disconnect();

                    return null;
                }

                Libuv2kNGLogger.Log("Libuv client connected to: " + address + ":" + uri.Port);

                return this;
            }
            catch (Exception ex)
            {
                Libuv2kNGLogger.Log($"Error trying to attempting to connect. Error: {ex}", LogType.Error);

                Disconnect();
            }

            return null;
        }

        /// <summary>
        ///     Receive call back when we finally connect to a server.
        /// </summary>
        /// <param name="handle">The stream handle we used to connect with server with.</param>
        /// <param name="error">If there were any errors during connection.</param>
        private void ConnectedAction(TcpStream handle, Exception error)
        {
            handle.onMessage = OnLibuvClientMessage;
            handle.onError = OnLibuvClientError;
            handle.onClosed = OnLibuvClientClosed;

            // close if errors (AFTER setting up onClosed callback!)
            if (error != null)
            {
                Libuv2kNGLogger.Log($"libuv client callback: client error {error}", LogType.Error);

                handle.CloseHandle();

                return;
            }

            _connectedComplete.SetResult(_connectedComplete.Task);
        }

        /// <summary>
        ///     We must tick through to receive connection status.
        /// </summary>
        private async void Tick(int tickRate)
        {
            // tick client
            while (!(_clientLoop is null) && !(_client is null) && !_client.IsClosing)
            {
                // Run with UV_RUN_NOWAIT returns 0 when nothing to do, but we
                // should avoid deadlocks via LibuvMaxTicksPerFrame

                if (_clientLoop.Run(uv_run_mode.UV_RUN_NOWAIT) == 0)
                {
                }

                await Task.Delay(tickRate);
            }
        }

        /// <summary>
        ///     Callback when connection receives a new message.
        /// </summary>
        /// <param name="handle">The stream handle we used to connect with server with.</param>
        /// <param name="segment">The data that has come in with it.</param>
        private void OnLibuvClientMessage(TcpStream handle, ArraySegment<byte> segment)
        {
            byte[] newData = new byte[segment.Count];

            Array.Copy(segment.Array, segment.Offset, newData, 0, segment.Count);

            Libuv2kNGLogger.Log($"libuv client callback received: data= {BitConverter.ToString(newData)}");

            _queuedData.Enqueue(newData);
        }

        /// <summary>
        ///     Callback for any errors that occur on the connection.
        /// </summary>
        /// <param name="handle">The stream handle we used to connect with server with.</param>
        /// <param name="error">The error that occurred on the connection.</param>
        private void OnLibuvClientError(TcpStream handle, Exception error)
        {
            Libuv2kNGLogger.Log($"libuv client callback: read error {error}", LogType.Error);

            handle.CloseHandle();
        }

        /// <summary>
        ///     Callback when connection has closed.
        /// </summary>
        /// <param name="handle">The stream handle in which closed connection to.</param>
        private void OnLibuvClientClosed(TcpStream handle)
        {
            Libuv2kNGLogger.Log("libuv client callback: closed connection");

            handle.Dispose();

            // set client to null so we can't send to an old reference anymore
            _client = null;
        }

        #endregion

        #region Implementation of IConnection

        /// <summary>
        ///     Send data through the connection.
        /// </summary>
        /// <param name="data">The data to send through.</param>
        /// <returns></returns>
        public Task SendAsync(ArraySegment<byte> data)
        {
            if (_client is null || _client.IsClosing || _cancellationToken.IsCancellationRequested) return null;

            Libuv2kNGLogger.Log("Libuv2kConnection client: send data=" + BitConverter.ToString(data.Array));

            _client?.Send(data);

            return Task.CompletedTask;
        }

        /// <summary>
        ///     reads a message from connection
        /// </summary>
        /// <param name="buffer">buffer where the message will be written</param>
        /// <returns>true if we got a message, false if we got disconnected</returns>
        public async Task<bool> ReceiveAsync(MemoryStream buffer)
        {
            try
            {
                while (!(_client is null) && !_cancellationToken.IsCancellationRequested)
                {
                    while (_queuedData.TryDequeue(out byte[] data))
                    {
                        buffer.SetLength(0);

                        Libuv2kNGLogger.Log(
                            $"Libuv2kConnection processing message: {BitConverter.ToString(data)}");

                        await buffer.WriteAsync(data, 0, data.Length);

                        return true;
                    }

                    await Task.Delay(1);
                }

                return false;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        /// <summary>
        ///     Disconnect this connection
        /// </summary>
        public void Disconnect()
        {
            Libuv2kNGLogger.Log("libuv client: closed connection");

            _connectedComplete?.TrySetCanceled();
            _cancellationToken.Cancel();
            _clientLoop?.Dispose();
            _client?.CloseHandle();
        }

        /// <summary>
        ///     the address of endpoint we are connected to
        ///     Note this can be IPEndPoint or a custom implementation
        ///     of EndPoint, which depends on the transport
        /// </summary>
        /// <returns></returns>
        public EndPoint GetEndPointAddress()
        {
            return _client?.GetPeerEndPoint();
        }

        #endregion
    }
}
