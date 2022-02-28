namespace Tool.Compet.Photon {
	using System;
	using System.Threading;
	using System.Threading.Tasks;
	using MessagePack;
	using Tool.Compet.Core;
	using Tool.Compet.Log;

	/// The hub is for streaming.
	public abstract class DkPhotonStreamHub : PhotonHub {
		/// Use this to make multiple hubs can communicate with server via one connector.
		/// In general, we just need one connection between server and client while streaming.
		/// For eg,. it is useful for case we jump to next screen, but still want to use same connection.
		internal static PhotonStreamConnector photonConnector;

		/// For cancel stream.
		internal static CancellationTokenSource cancellationTokenSource;

		/// @MainThread
		public DkPhotonStreamHub(int id, object terminal, DkPhotonConnectionSetting setting) : base(id, terminal) {
			if (photonConnector == null) {
				photonConnector = new(setting.inBufferSize);
			}
			// Register this hub to the connector.
			// Note that: each connector will hold multiple hubs.
			photonConnector.hubs[id] = this;
		}

		/// Connect to the realtime server with given setting.
		/// The app normally call this at start time, to communicate (send/receive data) with remote server.
		public override async Task ConnectAsync(DkPhotonConnectionSetting setting) {
			cancellationTokenSource = setting.cancellationTokenSource;

			// Connect to realtime server if not yet
			if (!photonConnector.connected) {
				await photonConnector.ConnectAsync(setting);
			}

			if (setting.allowPingServer) {
				///
			}
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
				msgPackObj, // method's parameters wrapper
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
				msgPackObj, // method's parameter wrapper
			});

			await photonConnector.SendAsync(outData);
		}

		/// @MainThread
		/// Close connection to remote server.
		public async void Disconnect() {
			try {
				cancellationTokenSource.Cancel();
				await photonConnector.CloseAsync();
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
			photonConnector.hubs.Remove(this.id);
		}
	}
}
