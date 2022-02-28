namespace Tool.Compet.Photon {
	using System.Threading.Tasks;

	public class DkPhotonStreamClient {
		public static bool connecting => DkPhotonStreamHub.photonConnector.connecting;
		public static bool connected => DkPhotonStreamHub.photonConnector.connected;

		public static async Task<TService> ConnectAsync<TService>(object terminal, DkPhotonConnectionSetting setting) where TService : class {
			var hub = PhotonServiceRegistry.CreateService<TService>(terminal, setting);
			await (hub as PhotonHub).ConnectAsync(setting);
			return hub;
		}
	}
}
