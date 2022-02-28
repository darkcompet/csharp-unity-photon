namespace Tool.Compet.Photon {
	using System.Threading;

	public class DkPhotonConnectionSetting {
		/// Url of remote realtime server.
		/// Normally, this is socket url (for eg,. wss://darkcompet.com/gaming)
		public string url;

		/// Default read-buffer for incoming data.
		/// The app should careful choose max-size of data in communication with remote server.
		/// Normally, 4 KB is enough, but it is dependent on the app requirement.
		public int inBufferSize = 1 << 12;

		/// By default, hubs which has same type (stream, chat, audio) will use same connection.
		public bool useDefaultConnector = true;

		/// Caller should provide it if authentication is
		/// required by server, for eg,. "Bearer your_access_token"
		public string? authorization;

		/// Use this to cancel communication or disconnect with remote server.
		public CancellationTokenSource cancellationTokenSource = new();

		/// Allow Photon can ping the realtime server to check connection.
		public bool allowPingServer = true;

		/// If `allowPingServer` was enabled, then Photon will ping in each this milliseconds (default 5 seconds).
		public int pingIntervalMillis = 5000;
	}
}
