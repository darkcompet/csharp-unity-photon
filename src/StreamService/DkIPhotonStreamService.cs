namespace Tool.Compet.Photon {
	using System.Threading.Tasks;

	/// Each stream service can be implemented by multiple hubServices.
	public interface DkIPhotonStreamService<TServiceResponse> {
		/// Remote Procedure Call.
		///
		/// In general, when the client call this method, it will send to remote server,
		/// then server will relay message to other players.
		///
		/// Since when the client define a service-method in sub-interface,
		/// remote server must implement it, but in general, a lot of methods have same implements
		/// that just send a client's message to other clients.
		/// That is why we create this method to avoid definition for general RPC service-method in remote server.
		public Task RPC(string methodName, DkPhotonRpcTarget rpcTarget, params object[] parameters);

		/// Called when the terminal got destroyed.
		public void OnDestroy();
	}
}
