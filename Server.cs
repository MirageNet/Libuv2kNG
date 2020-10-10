#region Statements

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using libuv2k;
using libuv2k.Native;
using UnityEngine;

#endregion

namespace Mirror.Libuv2kNG
{
    public class Server
    {
        #region Fields

        protected internal readonly ConcurrentQueue<TcpStream> QueuedConnections = new ConcurrentQueue<TcpStream>();

        // Libuv state
        //
        // IMPORTANT: do NOT create new Loop & Client here, otherwise a loop is
        //            also allocated if we run a test while a scene with this
        //            component on a GameObject is openened.
        //
        //            we need to create it when needed and dispose when we are
        //            done, otherwise dispose isn't called until domain reload.
        //
        // TODO what if we use one loop for both?
        private readonly Loop _serverLoop;
        private TcpStream _server;
        private CancellationTokenSource _cancellationToken;

        #endregion

        #region Class Specific

        /// <summary>
        ///     We must tick through to receive connection status.
        /// </summary>
        private async void Tick(int tickRate)
        {
            // tick client
            while (_serverLoop != null && _server != null && !_server.IsClosing)
            {
                // Run with UV_RUN_NOWAIT returns 0 when nothing to do, but we
                // should avoid deadlocks via LibuvMaxTicksPerFrame
                if (_serverLoop.Run(uv_run_mode.UV_RUN_NOWAIT) == 0)
                {
                }

                await Task.Delay(tickRate);
            }
        }

        /// <summary>
        ///     Queue up connections that are trying to connect.
        /// </summary>
        /// <param name="handle">The stream which is trying to connect to server.</param>
        /// <param name="error"></param>
        private void OnLibuvServerConnected(TcpStream handle, Exception error)
        {
            Libuv2kNGLogger.Log("libuv server: client connected =" + handle.GetPeerEndPoint().Address);

            // close if errors (AFTER setting up onClosed callback!)
            if (error != null)
            {
                Libuv2kNGLogger.Log($"libuv server: client connection failed {error}");

                handle.CloseHandle();

                return;
            }

            QueuedConnections.Enqueue(handle);
        }

        /// <summary>
        ///     Initialize new <see cref="Server"/>.
        /// </summary>
        /// <param name="port">The port we want to bind listening connections on.</param>
        /// <param name="tickRate">The rate at which we will delay before processing more data.</param>
        public Server(int port, int tickRate)
        {
            _cancellationToken = new CancellationTokenSource();

            _serverLoop = new Loop();

            ListenAsync(port);

            _ = Task.Run(() => Tick(tickRate), _cancellationToken.Token);
        }

        /// <summary>
        ///     Start listening for incoming connections.
        /// </summary>
        /// <param name="port">The port to bind to listen connections on.</param>
        private void ListenAsync(int port)
        {
            // start server
            IPEndPoint endPoint = new IPEndPoint(IPAddress.Any, port);

            Libuv2kNGLogger.Log("libuv server: starting TCP..." + endPoint);

            _server = new TcpStream(_serverLoop);
            _server.SimultaneousAccepts(true);
            _server.Listen(endPoint, OnLibuvServerConnected);

            Libuv2kNGLogger.Log("libuv server: TCP started!");
        }

        /// <summary>
        ///     Shut down the server.
        /// </summary>
        public void Shutdown()
        {
            if (_server != null)
            {
                _cancellationToken.Cancel();
                _serverLoop?.Dispose();
                _server?.Dispose();
                _server = null;

                Libuv2kNGLogger.Log("libuv server: TCP stopped!");
            }
        }

        #endregion
    }
}
