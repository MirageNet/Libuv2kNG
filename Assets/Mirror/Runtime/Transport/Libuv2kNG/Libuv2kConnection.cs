#region Statements

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Authentication.ExtendedProtection;
using System.Threading;
using Cysharp.Threading.Tasks;
using libuv2k;
using libuv2k.Native;
using UnityEngine;

#endregion

namespace Mirror.Libuv2kNG
{
    public class Libuv2kConnection : IConnection
    {
        #region Fields

        private readonly ConcurrentQueue<Message> _queuedIncomingData = new ConcurrentQueue<Message>();
        private readonly ConcurrentQueue<Message> _queueOutgoingData = new ConcurrentQueue<Message>();
        private TcpStream _client;
        private readonly Loop _clientLoop;
        private UniTaskCompletionSource _connectedComplete;
        private readonly CancellationTokenSource _cancellationToken;
        // libuv can be ticked multiple times per frame up to max so we don't
        // deadlock
        public const int LibuvMaxTicksPerFrame = 100;

        #endregion

        #region Class Specific

        /// <summary>
        ///     Initialize new <see cref="Libuv2kConnection"/>.
        /// </summary>
        /// <param name="noDelay"></param>
        /// <param name="client"></param>
        public Libuv2kConnection(bool noDelay, TcpStream client = null)
        {
            _cancellationToken = new CancellationTokenSource();

            if (client == null)
            {
                _clientLoop = new Loop();
                _client = new TcpStream(_clientLoop);

                _ = UniTask.RunOnThreadPool(Tick, false, _cancellationToken.Token);
            }
            else
            {
                _client = client;

                // setup callbacks
                client.onMessage = OnLibuvClientMessage;
                client.onError = OnLibuvClientError;
                client.onClosed = OnLibuvClientClosed;
            }

            _ = UniTask.RunOnThreadPool(ProcessOutgoingMessages, false, _cancellationToken.Token);

            _client.NoDelay(noDelay);
        }

        /// <summary>
        ///     Connect to server.
        /// </summary>
        /// <param name="uri">The server <see cref="Uri"/> to connect to.</param>
        /// <returns>Returns back a new <see cref="Libuv2kConnection"/> when connected or null when failed.</returns>
        public async UniTask<IConnection> ConnectAsync(Uri uri)
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

                _connectedComplete = new UniTaskCompletionSource();
                UniTask connectedCompleteTask = _connectedComplete.Task;

                while (await UniTask.WhenAny(connectedCompleteTask,
                           UniTask.Delay(TimeSpan.FromSeconds(Math.Max(1, 30)))) != 0)
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

            _connectedComplete.TrySetResult();
        }

        /// <summary>
        ///     We must tick through to receive connection status.
        /// </summary>
        private UniTask Tick()
        {
            // tick client
            while (!(_clientLoop is null) && !(_client is null))
            {
                // Run with UV_RUN_NOWAIT returns 0 when nothing to do, but we
                // should avoid deadlocks via LibuvMaxTicksPerFrame
                for (int i = 0; i < LibuvMaxTicksPerFrame; ++i)
                {
                    if (_clientLoop.Run(uv_run_mode.UV_RUN_NOWAIT) == 0)
                    {
                        Debug.Log("Client libuv ticked only " + i + " times");
                        break;
                    }
                }
            }

            Libuv2kNGLogger.Log("Shutting down tick task.");

            return UniTask.CompletedTask;
        }

        private UniTask ProcessOutgoingMessages()
        {
            while(!_cancellationToken.IsCancellationRequested)
            {
                for (int messageIndex = 0; messageIndex < _queueOutgoingData.Count; messageIndex++)
                {
                    if(_queueOutgoingData.TryDequeue(out Message outgoing))
                    {
                        _client?.Send(new ArraySegment<byte>(outgoing.Data));
                    }

                    UniTask.Delay(1);
                }

                UniTask.Delay(1);
            }

            Libuv2kNGLogger.Log("Shutting down processing task");

            return UniTask.CompletedTask;
        }

        /// <summary>
        ///     Callback when connection receives a new message.
        /// </summary>
        /// <param name="handle">The stream handle we used to connect with server with.</param>
        /// <param name="segment">The data that has come in with it.</param>
        private void OnLibuvClientMessage(TcpStream handle, ArraySegment<byte> segment)
        {
            Libuv2kNGLogger.Log($"libuv client callback received: data= {BitConverter.ToString(segment.Array)}");

            byte[] data = new byte[segment.Count];

            Array.Copy(segment.Array, segment.Offset, data, 0, segment.Count);

            _queuedIncomingData.Enqueue(new Message
            {
                Data = data
            });
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
        ///     Send data with channel specific settings. (NOOP atm until mirrorng links it)
        /// </summary>
        /// <param name="data">The data to be sent.</param>
        /// <param name="channel">The channel to send it on.</param>
        /// <returns></returns>
        public UniTask SendAsync(ArraySegment<byte> data, int channel)
        {
            if (_client is null || _client.IsClosing || _cancellationToken.IsCancellationRequested) return UniTask.CompletedTask;

            Libuv2kNGLogger.Log("Libuv2kConnection client: send data=" + BitConverter.ToString(data.Array));

            byte[] buffer = new byte[data.Count];

            Array.Copy(data.Array, data.Offset, buffer, 0, data.Count);

            _queueOutgoingData.Enqueue(new Message
            {
                Data = buffer,
                Channel = channel
            });

            return UniTask.CompletedTask;
        }

        /// <summary>
        ///     reads a message from connection
        /// </summary>
        /// <param name="buffer">buffer where the message will be written</param>
        /// <returns>true if we got a message, false if we got disconnected</returns>
        public async UniTask<int> ReceiveAsync(MemoryStream buffer)
        {
            try
            {
                while (!_cancellationToken.IsCancellationRequested)
                {
                    while (_queuedIncomingData.TryDequeue(out Message message))
                    {
                        Libuv2kNGLogger.Log(
                            $"Libuv2kConnection processing message: {BitConverter.ToString(message.Data)}");

                        buffer.SetLength(0);

                        buffer.Write(message.Data, 0, message.Data.Length);

                        return message.Channel;
                    }

                    await UniTask.Delay(1);
                }

                throw new EndOfStreamException();
            }
            catch (EndOfStreamException)
            {
                throw new EndOfStreamException();
            }
        }

        /// <summary>
        ///     Disconnect this connection
        /// </summary>
        public void Disconnect()
        {
            Libuv2kNGLogger.Log("libuv client: closed connection");

            _cancellationToken.Cancel();
            _connectedComplete?.TrySetCanceled();
            _client?.CloseHandle();

            while (_queuedIncomingData.TryDequeue(out _))
            {
                // do nothing   
            }

            while (_queueOutgoingData.TryDequeue(out _))
            {
                // do nothing   
            }

            _clientLoop?.Dispose();
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
