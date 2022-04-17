namespace Tool.Compet.Photon {
	using System.Collections.Generic;
	using System.Reflection;
	using System.Threading;
	using System.Threading.Tasks;

	/// Hub is a client presenter which communicates with server.
	/// Client will use a hub to send/receive data to/from the server.
	/// To do that, each hub will contain a `connector (communicator)` and a `terminal (client)`
	/// to perform communication. See below:
	///                                <---> Hub_1
	///                               /
	/// Server <---> PhotonConnector <---> Hub_2
	///                               \
	///                                <---> Hub_3
	public abstract class PhotonHub {
		/// This is called from the photon-connector when we get incoming-data from remote server.
		/// Subclass can deserialize the incoming data, then call the target method in the terminal.
		/// In general, subclass will use `SynchronizationContext` or `PhotonHandler` to call target
		/// terminal's method at main thread, like be `SynchronizationContext.Post()` or `PhotonHandler.Post()`.
		///
		/// TechNote: to copy different array-types, consider use `Buffer.BlockCopy`.
		public abstract void HandleResponse(SynchronizationContext context, int methodId, byte[] data, int offset);

		/// Id to identity this hub.
		/// Use-case: PhotonConnector uses this id to pass incoming event to target hub.
		public readonly int id;

		protected PhotonHub(int id) {
			this.id = id;

			// // [Collect RPC-methods inside terminal]
			// // TechNote: to retrieve methods, we must combine with `BindingFlags.Instance` flag.
			// // Ref: https://docs.microsoft.com/en-us/dotnet/api/system.type.getmethods?view=net-6.0
			// var responseMethods = this.responseMethods = new();
			// var repsonseMethodIds = this.responseMethodIds;
			// var terminalMethods = terminal.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);

			// // Find RPC-methods inside the terminal
			// for (var index = terminalMethods.Length - 1; index >= 0; --index) {
			// 	var terminalMethod = terminalMethods[index];
			// 	var terminalMethodName = terminalMethod.Name;

			// 	// Checking `DkPhotonRPC` annotation on each method.
			// 	// if (Attribute.GetCustomAttribute(method, typeof(DkPhotonRPC), false) != null) {

			// 	// Instead of checking annotation, we use pre-generated `rpcMethodIds` to target on RPC-methods.
			// 	if (repsonseMethodIds.ContainsKey(terminalMethodName)) {
			// 		responseMethods.TryAdd(repsonseMethodIds[terminalMethodName], terminalMethod);
			// 	}
			// }
		}
	}
}
