namespace Tool.Compet.Photon {
	using System;
	using System.Collections;
	using System.Threading;
	using System.Threading.Tasks;
	using MessagePack;
	using Tool.Compet.Core;
	using Tool.Compet.Log;

	/// The app should extend this and provide unimplemented methods like serialize/deserialize method's parameters.
	public abstract class DkPhotonStreamHub : PhotonHub {
		/// Use this to make multiple hubs can communicate with server via one connector.
		/// In general, we just need one connection between server and client while streaming.
		/// For eg,. it is useful for case we jump to next screen, but still want to use same connection.
		protected static PhotonConnector defaultPhotonStreamConnector;
		private CancellationTokenSource cancellationTokenSource;

		/// @MainThread
		public DkPhotonStreamHub(int id, object terminal, DkPhotonConnectionSetting setting) : base(id, terminal) {
			if (setting.useDefaultConnector) {
				if (defaultPhotonStreamConnector == null) {
					defaultPhotonStreamConnector = new(setting.inBufferSize);
				}
				photonConnector = defaultPhotonStreamConnector;
			}
			else {
				photonConnector = new PhotonConnector(setting.inBufferSize);
			}

			// Each connector can hold multiple hubs.
			photonConnector.hubs.TryAdd(id, this);
		}

		/// Connect to the realtime server with given setting.
		/// The app normally call this at start time, to communicate (send/receive data) with remote server.
		public override async Task ConnectAsync(DkPhotonConnectionSetting setting) {
			var mainThreadContext = SynchronizationContext.Current;
			var cancellationTokenSource = this.cancellationTokenSource = setting.cancellationTokenSource;

			// [Connect to Photon server]
			// Normally, the app always connect to lobby-server, so lobby server
			// can handle connections from all clients.
			await photonConnector.ConnectAsync(setting.url, cancellationTokenSource.Token, setting.authorization);

			// [Listen server's events]
			// Start new long-running background task to listen events from server.
			// We have to loop interval to check/receive message from server even though server has sent it to us.
			// See: https://devblogs.microsoft.com/xamarin/developing-real-time-communication-apps-with-websocket/
			await Task.Factory.StartNew(async () => {
				await ReceiveAsync(mainThreadContext);
			}, cancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
		}

		/// Send to server with message type as `DkPhotonMessageType.SERVICE`.
		/// @param msgPackObj: Pass null to indicate no-param in the method.
		internal async Task SendAsync(int methodId, object? msgPackObj) {
			if (!photonConnector.connected) {
				DkLogs.Warning("this", "Skip send while not connect to server.");
				return;
			}

			var outData = MessagePackSerializer.Serialize(new object[] {
				(byte)DkPhotonMessageType.SERVICE, // message type as SERVICE
				this.id, // hub id
				methodId, // method id
				msgPackObj, // method's parameters
			});

			await photonConnector.SendAsync(outData);
		}

		/// Send to server with message type as `DkPhotonMessageType.RPC`.
		internal async Task RpcAsync(int methodId, DkPhotonRpcTarget rpcTarget, object? msgPackObj) {
			if (!photonConnector.connected) {
				DkLogs.Warning(this, "Skip send while not connect to server.");
				return;
			}
			var outData = MessagePackSerializer.Serialize(new object[] {
				(byte)DkPhotonMessageType.RPC, // message type as RPC
				this.id, // hub id
				methodId, // method id
				(byte)rpcTarget, // which clients should be targeted
				msgPackObj, // method's parameter
			});

			await photonConnector.SendAsync(outData);
		}

		/// [Run in background]
		private async Task ReceiveAsync(SynchronizationContext context) {
			while (!this.cancellationTokenSource.IsCancellationRequested) {
				try {
					// This will wait (block while loop) until read message from server.
					// Note: do NOT write log here since it causes weird problem for receiving data...
					var (buffer, count) = await photonConnector.ReceiveAsync();
					if (buffer != null) {
						// We have to switch to new method since MessagePack specification requires that.
						ConsumeIncomingData(context, buffer, 0, count);
					}
				}
				catch (Exception e) {
					DkLogs.Warning(this, $"Could not receive incoming data, error: {e.Message}");
				}
			};
		}

		/// [Background]
		/// Still in background worker so can NOT call directly such methods: ToString(), GetName(),... of MonoBehaviour.
		private void ConsumeIncomingData(SynchronizationContext context, byte[] buffer, int offset, int count) {
			// dkopt: can make MessagePack accept offset to avoid allocate/copyto new array?
			var inData = new byte[count];
			Array.Copy(buffer, offset, inData, 0, count);
			var reader = new MessagePackReader(inData);

			// [Read header info]
			// Before parse all incoming data to some unknown object, we read head-values without parsing all data
			// to determine which method will be targeted (this is nice feature of MessagePack)
			var arrLength = reader.ReadArrayHeader();

			var messageType = (DkPhotonMessageType)reader.ReadByte();
			var hubId = reader.ReadByte();
			var methodId = reader.ReadInt16();

			// Deserialize rest of coming data to method's parameter (object which be annotated with `MessagePackObject`).
			var hub = photonConnector.hubs[hubId];

			// Incoming data format: [messageType, hubId, methodId, msgPackObj]
			if (messageType == DkPhotonMessageType.SERVICE) {
			}
			// Incoming data format: [messageType, hubId, methodId, clientTarget, msgPackObj]
			else if (messageType == DkPhotonMessageType.RPC) {
				// just read, not use
				var clientTarget = (DkPhotonRpcTarget)reader.ReadByte();
			}

			// Deserialize rest of coming data to method's parameter (object which be annotated with `MessagePackObject`).
			var paramsOffset = reader.Consumed;
			var parameters = hub.ConvertToMethodParams(methodId, inData, paramsOffset);

			// Post to main thread and call target RPC-method in the MonoBehaviour.
			// Do extras call if the method returned a coroutine instance.
			// Discussion: It is okie if use `SynchronizationContext.Post()` instead of `PhotonHandler.Post()`?
			// PhotonHandler.instance.Post(() => {
			context.Post(state => {
				// Nullable returned value
				var returnedValue = hub.CallMethod(methodId, parameters);
				if (returnedValue is IEnumerator) {
					PhotonHandler.instance.StartCoroutine((IEnumerator)returnedValue);
				}
			}, null);
		}

		/// @MainThread
		/// Close connection to remote server.
		public async void Disconnect() {
			try {
				this.cancellationTokenSource.Cancel();
				await this.photonConnector.CloseAsync();
				if (DkBuildConfig.DEBUG) { DkLogs.Debug("this", "Closed connection and Disposed socket"); }
			}
			catch (Exception e) {
				DkLogs.Warning("this", $"Error when dispose socket, error: {e.Message}");
			}
		}

		/// @MainThread
		/// Cleanup this hub resource.
		public override void OnDestroy() {
			base.OnDestroy();

			// Since each hub is bind with a mono,
			// so when the mono got destroyed, this hub should be considered as destroyed.
			this.photonConnector.hubs.Remove(this.id);
		}
	}
}
