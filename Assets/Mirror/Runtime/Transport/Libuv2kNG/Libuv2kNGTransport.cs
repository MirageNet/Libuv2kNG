#region Statements

using System;
using System.Collections.Generic;
using System.Net;
using Cysharp.Threading.Tasks;
using libuv2k;
using Mirror.Libuv2kNG;
using UnityEngine;

#endregion

namespace Mirror.Libu2kNG
{
    public class Libuv2kNGTransport : Transport
    {
        #region Unity Methods

        private void Start()
        {
            // configure logging.
            Libuv2kNGLogger.LogType = _logType;
            Log.Info = s => Libuv2kNGLogger.Log(s);
            Log.Warning = s => Libuv2kNGLogger.Log(s, LogType.Warning);
            Log.Error = s=> Libuv2kNGLogger.Log(s, LogType.Error);

        }

        private void OnDestroy()
        {
            libuv2k.libuv2k.Shutdown();
        }

        #endregion

        #region Fields

        [Header("Transport Options")]
        public ushort Port = 7777;
        [Tooltip("Nagle Algorithm can be disabled by enabling NoDelay")]
        public bool NoDelay = true;

        [Header("Debug Options")]
        [SerializeField] private LogType _logType = LogType.Warning;

        private Server _server;
        private Libuv2kConnection _client;

        #endregion

        #region Overrides of Transport

        public override IEnumerable<string> Scheme => new[] {"tcp4"};

        /// <summary>
        ///     Open up the port and listen for connections
        ///     Use in servers.
        /// </summary>
        /// <exception>If we cannot start the transport</exception>
        /// <returns></returns>
        public override UniTask ListenAsync()
        {
            if (_server != null)
            {
                Disconnect();
                _server = null;
            }

            _server = new Server(Port);

            return UniTask.CompletedTask;
        }

        /// <summary>
        ///     Stop listening to the port
        /// </summary>
        public override void Disconnect()
        {
            _server?.Shutdown();
            _server = null;
            _client?.Disconnect();
            _client = null;
        }

        /// <summary>
        ///     Determines if this transport is supported in the current platform
        /// </summary>
        /// <returns>true if the transport works in this platform</returns>
        public override bool Supported
        {
            // and only compiled for some platforms at the moment
            get
            {
                return Application.platform == RuntimePlatform.OSXEditor ||
                       Application.platform == RuntimePlatform.OSXPlayer ||
                       Application.platform == RuntimePlatform.WindowsEditor ||
                       Application.platform == RuntimePlatform.WindowsPlayer ||
                       Application.platform == RuntimePlatform.LinuxEditor ||
                       Application.platform == RuntimePlatform.LinuxPlayer;
            }
        }

        /// <summary>
        ///     Connect to a server located at a provided uri
        /// </summary>
        /// <param name="uri">address of the server to connect to</param>
        /// <returns>The connection to the server</returns>
        /// <exception>If connection cannot be established</exception>
        public override async UniTask<IConnection> ConnectAsync(Uri uri)
        {
            _client = new Libuv2kConnection(NoDelay);

            UriBuilder connection = new UriBuilder {Scheme = uri.Scheme, Host = uri.Host, Port = Port};

            return await _client.ConnectAsync(connection.Uri);
        }

        /// <summary>
        ///     Accepts a connection from a client.
        ///     After ListenAsync completes,  clients will queue up until you call AcceptAsync
        ///     then you get the connection to the client
        /// </summary>
        /// <returns>The connection to a client</returns>
        public override async UniTask<IConnection> AcceptAsync()
        {
            try
            {
                while (!(_server is null))
                {
                    while(_server.QueuedConnections.TryDequeue(out Libuv2kConnection client))
                    {
                        return client;
                    }

                    await UniTask.Delay(1);
                }

                return null;
            }
            catch (ObjectDisposedException)
            {
                // expected,  the connection was closed
                return null;
            }
        }

        /// <summary>
        ///     Retrieves the address of this server.
        ///     Useful for network discovery
        /// </summary>
        /// <returns>the url at which this server can be reached</returns>
        public override IEnumerable<Uri> ServerUri()
        {
            var builder = new UriBuilder {Scheme = "tcp4", Host = Dns.GetHostName(), Port = Port};

            return new[] {builder.Uri};
        }

        #endregion
    }
}
