namespace Tool.Compet.Photon {
	using System;
	using System.Collections.Generic;
	using System.Net.WebSockets;
	using System.Threading;
	using System.Threading.Tasks;
	using Tool.Compet.Core;
	using Tool.Compet.Log;

	/// Stateful structure (lobby-server will decide which realtime server will host the client).
	/// By this structure, when a client want to realtime with other players, it will request lobby-server to create new room,
	/// after new room was created, realtime-server info will be sent back to the client, so that client can begin to contact
	/// with target realtime-server from that time. We can image it as below:
	///      --> --> Lobby server --> (2) -->
	///     /       /        \               \
	///   (1)     (4)        (3)
	///   /       /            \               \
	/// Client <--             <-- Room server <--
	///
	/// In general,
	/// - Lobby server will handle all client connections.
	/// - Room server will relay message between players in a room.

	/// For realtime communication, each side of client and server will hold PhotonConnector
	/// to handle message between hubs as below:
	/// [Client]                          [Room Server]
	///  Hub 1 \                             / Hub 1
	///  Hub 2  <----> PhotonConnector <----> Hub 2
	///  Hub 3 /                             \ Hub 3

	/// Socket (TCP vs UDP): https://en.wikipedia.org/wiki/Nagle%27s_algorithm
	/// TCP test for Unity client: https://gist.github.com/danielbierwirth/0636650b005834204cb19ef5ae6ccedb
	/// Raw socket impl server side: https://stackoverflow.com/questions/36526332/simple-socket-server-in-unity
	/// Unity websocket-based socketio: https://github.com/itisnajim/SocketIOUnity
	/// Unity client websocket: https://devblogs.microsoft.com/xamarin/developing-real-time-communication-apps-with-websocket/
	/// Tasking with ThreadPool in C#: https://stackoverflow.com/questions/7889746/creating-threads-task-factory-startnew-vs-new-thread
	/// Compare with Ktor websocket: https://ktor.io/docs/websocket.html#api-overview
	/// Websocket client: https://github.com/Marfusios/websocket-client
	/// MessagePack for SignalR: https://docs.microsoft.com/en-us/aspnet/core/signalr/messagepackhubprotocol?view=aspnetcore-6.0
	/// SignalR for client: https://www.nuget.org/packages/Microsoft.AspNetCore.SignalR.Client/6.0.1
	/// Call methods via attribute: https://stackoverflow.com/questions/46359351/how-to-call-methods-with-method-attributes
	/// [From NuGet PM] Super websocket: https://www.supersocket.net/
	/// Talk: https://fmgamer99.wordpress.com/2018/10/22/nhat-ky-lam-game-online-realtime-no-1-chon-cong-nghe-unitysocketio/
	/// Servers architecture: https://qiita.com/naoya-kishimoto/items/0d913a4b65ec0c4088a6
	public class PhotonConnector {
		public bool inRoom;

		/// Check connection is ready for use (send, receive,...) or not.
		public bool connected => socket.State == WebSocketState.Open;

		/// Use this to check which hub will consume the data when received data from remote server.
		/// This is mapping between hubId vs hubInstance.
		internal readonly Dictionary<int, PhotonHub> hubs = new();

		/// Communicator between server and client
		private ClientWebSocket socket;

		/// To avoid allocate new array when receive message from server.
		private ArraySegment<byte> inBuffer;

		/// Cancel event from outside.
		private CancellationToken cancellationToken;

		internal protected PhotonConnector(int inBufferSize = 1 << 12) {
			this.socket = new();
			this.inBuffer = new ArraySegment<byte>(new byte[inBufferSize], 0, inBufferSize);
		}

		/// Connect to lobby server which contains all client connections.
		/// TechNote: we make this as `async Task` instead of `async void`
		/// to let caller can use `await` on this method.
		internal async Task ConnectAsync(string url, CancellationToken cancellationToken, string? authorization = null) {
			// [Connect to server]
			// Url must be started with `wss` since server using `HTTPS`
			// For cancellation token, also see `CancellationTokenSource, CancellationTokenSource.CancelAfter()` for detail.
			if (authorization != null) {
				socket.Options.SetRequestHeader("Authorization", authorization);
			}
			await socket.ConnectAsync(new Uri(url), cancellationToken);
		}

		/// Send binary raw data to server.
		/// Notice: Caller must check connection state is connected before call this.
		/// Otherwise an exception will be thrown.
		/// TechNote: we make this as `async Task` instead of `async void`
		/// to let caller can use `await` on this method.
		internal async Task SendAsync(byte[] outData) {
			await socket.SendAsync(new ArraySegment<byte>(outData), WebSocketMessageType.Binary, true, CancellationToken.None);
		}

		/// TechNote: we make this as `async Task` instead of `async void`
		/// to let caller can use `await` on this method.
		internal async Task<(byte[], int)> ReceiveAsync() {
			var inBuffer = this.inBuffer;

			// Action `send` must be performed while socket connection is open.
			// Otherwise we get exception.
			if (socket.State != WebSocketState.Open) {
				DkLogs.Warning(this, $"Ignored receive while socket-state is NOT open, current state: {socket.State}");
				return (null, 0);
			}

			// Read server-message and fill full into the buffer.
			// If we wanna looping to read as chunks (XXX bytes), we can check with `serverResult.EndOfMessage`
			// to detect when reading message (line) get completed.
			var inResult = await socket.ReceiveAsync(
				inBuffer,
				CancellationToken.None
			);

			// Server closes the connection
			if (inResult.CloseStatus.HasValue) {
				if (DkBuildConfig.DEBUG) { DkLogs.Debug(this, "Skip read message since socket was closed"); }
				return (null, 0);
			}

			// [Parse server's message]
			// We can handle various of data type (binary, text,...) but
			// for better performance, we only handle `binary` raw data to avoid conversion.
			switch (inResult.MessageType) {
				case WebSocketMessageType.Binary: {
					return (inBuffer.Array, inResult.Count);
				}
				default: {
					if (DkBuildConfig.DEBUG) { DkLogs.Debug(this, $"Unhandled inResult.MessageType: {inResult.MessageType}"); }
					return (null, 0);
				}
			}
		}

		/// Close the connection to remote server.
		/// TechNote: we make this as `async Task` instead of `async void`
		/// to let caller can use `await` on this method.
		internal async Task CloseAsync() {
			try {
				// Tell server release the socket connection.
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "OK", CancellationToken.None);
			}
			finally {
				// Cleanup socket resource.
				socket.Dispose();
			}
		}
	}
}
