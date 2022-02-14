namespace Tool.Compet.Photon {
	using System;
	using System.Threading;
	using System.Threading.Tasks;

	using Tool.Compet.Core;
	using Tool.Compet.Log;
	using MessagePack;
	using System.Collections;
	using System.Collections.Generic;

	/// This is connector (router) to the realtime server.
	/// One app has only one connection (we use static keyword) to realtime server,
	/// and can contain many hub instances associated with this connector.

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
	public static class DkPhotonConnector {
		/// Indicate the app is connected or not to the realtime server.
		public static bool connected;

		public static bool inRoom;

		/// Contains all hubs of the app.
		/// When receiving data from remote server, we use this to check
		/// which hub is targeted for the data.
		internal static readonly Dictionary<int, DkPhotonHub> hubs = new();

		/// Use this to communicate (send/receive data) with remote server.
		private static PhotonPeer photonPeer;

		/// Indicate caller has requested close realtime network.
		private static bool disconnectRequested;

		private static long lastSentTime;

		/// @param socketUrl: Url of server socket, for eg,. wss://darkcompet.com/gaming
		public static async Task ConnectAsync(ConnectionSetting setting) {
			photonPeer = new(setting.bufferSize);

			var cancellationToken = CancellationToken.None;
			await photonPeer.ConnectAsync(setting, cancellationToken);

			// Mark as connected
			connected = true;

			// [Listen server's events]
			// Start new long-running background task to listen events from server.
			// We have to loop interval to check/receive message from server even though server has sent it to us.
			// See: https://devblogs.microsoft.com/xamarin/developing-real-time-communication-apps-with-websocket/
			await Task.Factory.StartNew(async () => {
				while (!disconnectRequested) {
					// Wait and Read server's message
					await ReceiveAsync();
				}
			}, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		}

		// static long sendCount;
		/// Run in background.
		/// bytes: Encoding.UTF8.GetBytes(DkJsons.Obj2Json(new UserProfileResponse()))
		/// Send obj: https://stackoverflow.com/questions/15012549/send-typed-objects-through-tcp-or-sockets
		// public async Task SendAsync(params object[] parameters) {
		// 	if (this.disconnectRequested) {
		// 		if (DkBuildConfig.DEBUG) { DkLogs.Debug(this, "Skip sending since disconnectRequested"); }
		// 		return;
		// 	}

		// 	var socket = this.socket;

		// 	// Action `send` must be performed while socket connection is open.
		// 	// Otherwise we get exception.
		// 	if (socket.State == WebSocketState.Open) {
		// 		this.lastSentTime = DkUtils.CurrentUnixTimeInMillis();

		// 		var outBytes = Encoding.UTF8.GetBytes("client test message");

		// 		await socket.SendAsync(
		// 			new ArraySegment<byte>(outBytes),
		// 			WebSocketMessageType.Text,
		// 			true,
		// 			CancellationToken.None
		// 		);
		// 		if (DkBuildConfig.DEBUG) { DkLogs.Debug(this, $"{++sendCount}-SendAsync done {outBytes.Length} bytes"); }
		// 	}
		// 	else {
		// 		DkLogs.Warning(this, $"Ignored send while socket-state is NOT open, current state: {socket.State}");
		// 	}
		// }

		internal static async Task SendRPC(int hubId, int methodId, object msgPackObj) {
			if (disconnectRequested) {
				if (DkBuildConfig.DEBUG) { DkLogs.Debug("this", "Skip SendRPC since disconnectRequested"); }
				return;
			}

			lastSentTime = DkUtils.CurrentUnixTimeInMillis();

			// var outBytes = this.Serialize(data);
			var outData = MessagePackSerializer.Serialize(new object[] {
				hubId, // 1st: hub id
				methodId, // 2nd: method id
				msgPackObj, // 3rd: method's parameter
			});

			await photonPeer.SendAsync(outData);
		}

		static int receiveCount;
		/// [Background]
		private static async Task ReceiveAsync() {
			try {
				var (buffer, count) = await photonPeer.ReceiveAsync();
				if (buffer != null) {
					ConsumeData(buffer, count);
				}
			}
			catch (Exception e) {
				if (DkBuildConfig.DEBUG) { DkLogs.Debug("this", "Could not receive incoming data, error: " + e.Message); }
			}
		}

		/// Still in background worker so can NOT call directly such methods: ToString(), GetName(),... of MonoBehaviour.
		/// [Background]
		private static void ConsumeData(byte[] buffer, int count) {
			var inData = new byte[count];
			Array.Copy(buffer, 0, inData, 0, count);
			var reader = new MessagePackReader(inData);

			// Incoming data format: [hubId, methodId, parameter]
			// Before parse all incoming data to some unknown object, we read head-values without parsing all data
			// to determine which method will be targeted (this is nice feature of MessagePack)
			var arrLength = reader.ReadArrayHeader();
			if (arrLength == 3) {
				// Get RPC-methods inside the hub.
				// Note: we must use combination of [BindingFlags.Instance | BindingFlags.Public] when retrieve public-method.
				// From doc: According to the documentation, the overload Type.GetMethods (BindingFlags) requires
				// that you either specify `BindingFlags.Static` or `BindingFlags.Instance` in order to get a return.
				// Ref: https://docs.microsoft.com/en-us/dotnet/api/system.type.getmethods?view=net-6.0
				var hubId = reader.ReadInt32();
				var methodId = reader.ReadInt32();
				var offset = reader.Consumed;

				// Deserialize rest of coming data to method's parameter (object which be annotated with `MessagePackObject`).
				var hub = hubs[hubId];
				var msgPackObj = hub.DeserializeMethodParams(methodId, inData, offset);

				// Post to main thread and call target RPC-method in the MonoBehaviour.
				// Do extras call if the method returned a coroutine instance.
				PhotonHandler.instance.Post(() => {
					var returnedValue = hub.InvokeRpcMethod(methodId, new object[] { msgPackObj });
					if (returnedValue is IEnumerator) {
						PhotonHandler.instance.StartCoroutine((IEnumerator)returnedValue);
					}
				});

				DkLogs.Info("this", $"{++receiveCount}-ReceiveAsync done after {DkUtils.CurrentUnixTimeInMillis() - lastSentTime} ms");
			}
		}

		/// [MainThread]
		internal static async void Disconnect() {
			try {
				if (DkBuildConfig.DEBUG) { DkLogs.Debug("this", "Caller has requested disconnect"); }

				disconnectRequested = true;

				await photonPeer.CloseAsync();

				// Mark as disconnected
				connected = false;

				if (DkBuildConfig.DEBUG) { DkLogs.Debug("this", "Closed connection and Disposed socket"); }
			}
			catch (Exception e) {
				DkLogs.Warning("this", $"Error when dispose socket, error: {e.Message}");
			}
		}

		/// [MainThread]
		internal static void OnHubDestroyed(DkPhotonHub hub) {
			// Remove the hub from hubs
			hubs.Remove(hub.id);
		}

		public class ConnectionSetting {
			public string url;
			public int bufferSize = 1 << 12;

			/// Caller should provide it if authentication is
			/// required by server, for eg,. "Bearer your_access_token"
			public string? authorization;
		}
	}
}
