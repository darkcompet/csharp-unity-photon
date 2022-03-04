namespace Tool.Compet.Photon {
	using System;
	using System.Threading.Tasks;
	using Tool.Compet.Core;
	using Tool.Compet.Log;

	public class DkPhotonStreamClient {
		/// Still connecting, not yet ready for use hub service.
		public static bool connecting => DkPhotonStreamHub.photonConnector.connecting;

		/// Ready to use hub service.
		public static bool connected => DkPhotonStreamHub.photonConnector.connected;

		/// Time (in milliseconds) between ping (request to server) and pong (response from server).
		public static long ping => DkPhotonStreamHub.photonConnector.roundTripTime;

		/// @MainThread
		/// Establish (create, connect,...) connection to the remote server.
		public static async Task ConnectAsync(DkPhotonConnectionSetting setting) {
			var photonConnector = DkPhotonStreamHub.photonConnector;
			if (photonConnector == null) {
				photonConnector = DkPhotonStreamHub.photonConnector = new(setting);
			}
			// Connect to realtime server if not yet
			if (!photonConnector.connected) {
				await photonConnector.ConnectAsync(setting);
			}
		}

		/// Create stream hub service instance which can be used to communicate with server.
		public static TService CreateService<TService>(object terminal) {
			return PhotonStreamServiceRegistry.CreateStreamHubService<TService>(terminal);
		}

		/// Close (cancel, dispose,...) the connection with remote server.
		public static async Task CloseAsync() {
			try {
				var photonConnector = DkPhotonStreamHub.photonConnector;
				if (photonConnector != null) {
					await photonConnector.CloseAsync();
					if (DkBuildConfig.DEBUG) { DkLogs.Debug("this", "Closed connection and Disposed socket"); }
				}
			}
			catch (Exception e) {
				DkLogs.Warning("this", $"Could not close server connection, error: {e.Message}");
			}
		}
	}
}
