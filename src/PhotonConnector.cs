namespace Tool.Compet.Photon {
	using System.Collections.Generic;

	public abstract class PhotonConnector {
		/// Use this to check which hub will consume the data when received data from remote server.
		/// This is mapping between hubId vs hubInstance.
		internal readonly Dictionary<int, PhotonHub> hubs = new();
	}
}
