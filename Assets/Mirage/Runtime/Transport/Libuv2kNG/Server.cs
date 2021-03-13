#region Statements

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using Cysharp.Threading.Tasks;
using libuv2k;
using libuv2k.Native;

#endregion

namespace Mirage.Libuv2kNG
{
    public class Server
    {
        #region Fields

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
        private readonly CancellationTokenSource _cancellationToken;
        // libuv can be ticked multiple times per frame up to max so we don't
        // deadlock
        public const int LibuvMaxTicksPerFrame = 100;

        private readonly Transport _transport;

        #endregion

        #region Class Specific

        /// <summary>
        ///     We must tick through to receive connection status.
        /// </summary>
        private async void Tick()
        {
            // tick client
            while (_serverLoop != null && _server != null)
            {
                // Run with UV_RUN_NOWAIT returns 0 when nothing to do, but we
                // should avoid deadlocks via LibuvMaxTicksPerFrame
                for (int i = 0; i < LibuvMaxTicksPerFrame; ++i)
                {
                    if (_serverLoop.Run(uv_run_mode.UV_RUN_NOWAIT) == 0)
                    {
                        break;
                    }
                }

                await UniTask.Delay(1);
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

                handle.Dispose();

                return;
            }

            var newClient = new Libuv2kConnection(true, handle);

            _transport.Connected.Invoke(newClient);
        }

        /// <summary>
        ///     Initialize new <see cref="Server"/>.
        /// </summary>
        /// <param name="port">The port we want to bind listening connections on.</param>
        /// <param name="transport">Transport to attach to.</param>
        public Server(int port, Transport transport)
        {
            _cancellationToken = new CancellationTokenSource();

            _serverLoop = new Loop();

            ListenAsync(port);

            _transport = transport;

            _ = UniTask.RunOnThreadPool(Tick, false, _cancellationToken.Token);

            _transport.Started.Invoke();
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
            _server.onServerConnect = OnLibuvServerConnected;
            _server.Listen(endPoint);

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
                _server?.Dispose();
                _server = null;

                Libuv2kNGLogger.Log("libuv server: TCP stopped!");

                _serverLoop?.Dispose();
            }
        }

        #endregion
    }
}
