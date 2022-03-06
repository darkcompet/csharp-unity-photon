namespace Tool.Compet.Photon {
	using System;
	using System.Threading;
	using System.Threading.Tasks;
	using MessagePack;
	using Tool.Compet.Log;

	/// This hub is for frame-based streaming like game, video,...
	/// This class holds terminalClassId to support multiple clients (terminals),
	/// so it can be considered as TerminalStreamHub.
	public abstract class DkPhotonStreamHub : PhotonHub {
		/// Terminal presents for an end-user who works directly with this hub.
		/// In general, each hub can associate with multiple terminals via same hub-id.
		/// Each stream hub will associate with some terminal.
		/// To separate which terminal is associated with this hub, we use this id.
		protected int terminalId;

		/// Initialized by `DkPhotonStreamClient.Install()`.
		/// Use this to make multiple hubs can communicate with server via one connector.
		/// In general, we just need one connection between server and client while streaming.
		/// For eg,. it is useful for case we jump to next screen, but still want to use same connection.
		internal static PhotonStreamConnector photonConnector;

		/// For cancel stream.
		private CancellationToken cancellationToken;

		/// @MainThread
		public DkPhotonStreamHub(int id, int terminalId) : base(id) {
			this.terminalId = terminalId;

			if (photonConnector == null) {
				throw new Exception("Stream client is not initialized ! Please call `DkPhotonStreamClient.ConnectAsync()` first.");
			}

			// Register this hub to the connector.
			// It will replace current terminalHub with this hub.
			// Note: each connector can hold multiple hubs.
			photonConnector.RegisterHub(id, terminalId, this);
		}

		/// Send to server with message type as `DkPhotonMessageType.SERVICE`.
		/// @param msgPackObj: Pass null to indicate no-param in the method.
		internal async Task SendAsServiceAsync(int methodId, object? msgPackObj) {
			if (!photonConnector.connected) {
				DkLogs.Warning("this", "Skip send while not connect to server.");
				return;
			}

			var outData = MessagePackSerializer.Serialize(new object[] {
				(byte)DkPhotonMessageType.SERVICE, // message type as SERVICE
				(byte)this.id, // hub id
				(byte)this.terminalId, // terminal id
				(short)methodId, // method id
				msgPackObj, // method's parameters wrapper
			});

			await photonConnector.SendAsync(outData);
		}

		/// Send to server with message type as `DkPhotonMessageType.RPC`.
		internal async Task SendAsRpcAsync(int methodId, DkPhotonRpcTarget rpcTarget, object? msgPackObj) {
			if (!photonConnector.connected) {
				DkLogs.Warning(this, "Skip send while not connect to server.");
				return;
			}
			var outData = MessagePackSerializer.Serialize(new object[] {
				(byte)DkPhotonMessageType.RPC, // message type as RPC
				(byte)this.id, // hub id
				(byte)this.terminalId, // terminal id
				(short)methodId, // method id
				(byte)rpcTarget, // which clients should be targeted
				msgPackObj, // method's parameter wrapper
			});

			await photonConnector.SendAsync(outData);
		}

		/// @MainThread
		/// Cleanup this hub resource.
		public void OnDestroy() {
			// Tell connector release this hub.
			photonConnector.UnregisterHub(this.id, this.terminalId);
		}
	}
}
