namespace Tool.Compet.Photon {
	using System.Collections.Generic;
	using System.Reflection;
	using System.Threading;
	using System.Threading.Tasks;

	/// Hub is a gate which communicates with server via a connector, and directly handle with a client (terminal).
	/// We can consider a hub as a communicator between server and client.
	/// Inside each hub, it contain a connector for communicating with server,
	/// and wraps a terminal (client) object as below:
	/// Server <-- (PhotonConnector) --> Hub (Contains a Terminal as ClientWrapper)
	public abstract class PhotonHub {
		/// This is used for RPC communication method via `this.RPC()`.
		/// Subclass should provide mapping of name vs id of RPC-method inside the terminal.
		protected abstract Dictionary<string, int> responseMethodIds { get; }

		/// Convert incoming data to method's parameters. This is considered as deserializing process.
		/// dkopt: Subclass should consider when implement this method to avoid re-allocate new array when deserializing
		/// method's params to object array.
		/// Note: to copy different array-types, consider use `Buffer.BlockCopy`.
		///
		/// @return Array of parameter of target method.
		public abstract object[] ConvertToMethodParams(int methodId, byte[] data, long offset);

		/// Subclass should add `async` to make this method become awaitable.
		public abstract Task RPC(string rpcMethodName, DkPhotonRpcTarget clientTarget, params object[] parameters);

		/// Connect to remote server. Normally, connect to socket server.
		/// Subclass should add `async` to make this method become awaitable.
		public abstract Task ConnectAsync(DkPhotonConnectionSetting setting);

		/// Id to itendify each hub.
		/// Use-case eg,. PhotonConnector uses this id to pass incoming event to target hub.
		protected readonly int id;

		/// Connector for send/receive message to/from remote server.
		protected PhotonConnector photonConnector;

		/// Terminal presents for the client, is wrapped inside the hub.
		/// It will be released when the client got destroyed.
		/// Normally, this is MonoBehaviour in Unity, or Controller in server,...
		protected object? terminal;

		/// Mapping between methodId vs methodInfo inside the terminal.
		/// This is used for RPC communication method via `this.RPC()`.
		protected Dictionary<int, MethodInfo> responseMethods;

		protected PhotonHub(int id, object terminal) {
			this.id = id;
			this.terminal = terminal;

			// [Collect RPC-methods inside terminal]
			// TechNote: to retrieve methods, we must combine with `BindingFlags.Instance` flag.
			// Ref: https://docs.microsoft.com/en-us/dotnet/api/system.type.getmethods?view=net-6.0
			var responseMethods = this.responseMethods = new();
			var repsonseMethodIds = this.responseMethodIds;
			var terminalMethods = terminal.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);

			// Find RPC-methods inside the terminal
			for (var index = terminalMethods.Length - 1; index >= 0; --index) {
				var terminalMethod = terminalMethods[index];
				var terminalMethodName = terminalMethod.Name;

				// Checking `DkPhotonRPC` annotation on each method.
				// if (Attribute.GetCustomAttribute(method, typeof(DkPhotonRPC), false) != null) {

				// Instead of checking annotation, we use pre-generated `rpcMethodIds` to target on RPC-methods.
				if (repsonseMethodIds.ContainsKey(terminalMethodName)) {
					responseMethods.TryAdd(repsonseMethodIds[terminalMethodName], terminalMethod);
				}
			}
		}

		/// Called by PhotonConnector when received a message from server.
		/// This will call method inside the terminal.
		///
		/// @return Nullable value returned from target RPC-method in the terminal. Note that, in Unity, it is
		/// maybe IEnumberator, so caller should start coroutine to execute the method.
		internal object? CallMethod(int methodId, object[] parameters) {
			// Avoid call if the terminal was destroyed.
			if (this.terminal == null) {
				return null;
			}
			// Method 1: ((IServiceResponse) terminal).OnXXX(parameters);
			// Method 2: mono.SendMessage(this.rpcMethods[methodId].Name, parameters);
			return this.responseMethods[methodId].Invoke(this.terminal, parameters);
		}

		/// Called when the terminal get destroyed.
		/// The app should call this when override.
		public virtual void OnDestroy() {
			// Release the terminal to avoid memory leak.
			this.terminal = null;
		}
	}
}
