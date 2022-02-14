namespace Tool.Compet.Photon {
	using System;
	using System.Net.WebSockets;
	using System.Threading;
	using System.Threading.Tasks;
	using Tool.Compet.Core;
	using Tool.Compet.Log;

	internal class PhotonPeer {
		/// Communicator between server and client
		private ClientWebSocket socket;

		/// To avoid allocate new array when receive message from server.
		private ArraySegment<byte> inBuffer;

		internal PhotonPeer(int bufferSize) {
			this.socket = new();
			this.inBuffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);
		}

		internal async Task ConnectAsync(DkPhotonConnector.ConnectionSetting conSetting, CancellationToken cancellationToken) {
			// [Connect to server]
			// Url must be started with `wss` since server using `HTTPS`
			// For cancellation token, also see `CancellationTokenSource, CancellationTokenSource.CancelAfter()` for detail.
			var authorization = conSetting.authorization;
			if (authorization != null) {
				socket.Options.SetRequestHeader("Authorization", authorization);
			}
			await socket.ConnectAsync(
				new Uri(conSetting.url),
				cancellationToken
			);
		}

		/// Check socket is in open state or not.
		/// @return `true` if current socket status is open.
		internal bool isConnecting => socket.State == WebSocketState.Open;

		int sendRpcCount;
		internal async Task SendAsync(byte[] outBytes) {
			// Action `send` must be performed while socket connection is open.
			// Otherwise we get exception.
			if (socket.State == WebSocketState.Open) {
				// lastSentTime = DkUtils.CurrentUnixTimeInMillis();

				await socket.SendAsync(new ArraySegment<byte>(outBytes), WebSocketMessageType.Binary, true, CancellationToken.None);

				if (DkBuildConfig.DEBUG) { DkLogs.Debug(this, $"{++sendRpcCount}-SendRPC Sent {outBytes.Length} bytes"); }
			}
			else {
				DkLogs.Warning(this, $"Ignored SendRPC while socket-state is NOT open, current state: {socket.State}");
			}
		}

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
			switch (inResult.MessageType) {
				case WebSocketMessageType.Text: {
					// dkopt: avoid re-allocate new array by impl new deserialization method
					var fromArr = inBuffer.Array;
					// var toArr = new byte[inResult.Count];
					// Array.Copy(fromArr, toArr, inResult.Count);
					// var result = MessagePackSerializer.Deserialize<T>(resultArr);

					// var result = Encoding.UTF8.GetString(fromArr, 0, inResult.Count);
					// DkLogs.Info(this, $"{++receiveCount}-Got text after {DkUtils.CurrentUnixTimeInMillis() - lastSentTime} millis, result: {result}");
					return (fromArr, inResult.Count);
				}
				case WebSocketMessageType.Binary: {
					// dkopt: avoid re-allocate new array by impl new deserialization method
					// To copy different array-types, consider use `Buffer.BlockCopy`.
					var fromArr = inBuffer.Array;

					// var result = this.Deserialize(fromArr, 0, inResult.Count);
					return (fromArr, inResult.Count);
				}
				default: {
					if (DkBuildConfig.DEBUG) { DkLogs.Debug(this, $"Unhandled inResult.MessageType: {inResult.MessageType}"); }
					return (null, 0);
				}
			}
		}

		internal async Task CloseAsync() {
			// [Close connection]
			// Tell server release the socket connection.
			await socket.CloseAsync(
				WebSocketCloseStatus.NormalClosure,
				"OK",
				CancellationToken.None
			);

			// [Dispose socket]
			// Cleanup socket resource.
			socket.Dispose();
		}
	}
}
