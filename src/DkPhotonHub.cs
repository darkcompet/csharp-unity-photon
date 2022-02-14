namespace Tool.Compet.Photon {
	using System;
	using System.Collections.Generic;
	using System.Reflection;
	using System.Threading.Tasks;
	using UnityEngine;

	public abstract class DkPhotonHub {
		/// Mapping of methodName vs methodId inside the RPC-container.
		protected abstract Dictionary<string, int> rpcMethodIds { get; }

		/// Deserialize parameters for the method from incoming data.
		public abstract object DeserializeMethodParams(int methodId, byte[] data, long offset);

		/// Id to itendify each hub.
		/// PhotonConnector will use this id to pass incoming event to target hub.
		internal readonly int id;

		/// RPC-methods container.
		protected MonoBehaviour mono;

		/// Mapping between methodId vs methodInfo inside the RPC-container.
		private Dictionary<int, MethodInfo> rpcMethods;

		protected DkPhotonHub(int id, MonoBehaviour mono) {
			// [Assign]
			this.id = id;
			this.mono = mono;

			// [Registher this hub to hubList]
			DkPhotonConnector.hubs[id] = this;

			// [Collect RPC-methods]
			// Note: to find public method, we must combine with `BindingFlags.Instance` flag.
			var rpcMethods = this.rpcMethods = new();
			var rpcAnnotation = typeof(DkPhotonRPC);
			var monoMethods = mono.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public);

			// Loops over public-methods to target on RPC-annotated methods
			for (var index = monoMethods.Length - 1; index >= 0; --index) {
				var method = monoMethods[index];

				if (Attribute.GetCustomAttribute(method, rpcAnnotation, false) != null) {
					rpcMethods.Add(rpcMethodIds[method.Name], method);
				}
			}
		}

		public void OnDestroy() {
			DkPhotonConnector.OnHubDestroyed(this);
		}

		/// Call a remote (even locally if optioned) method.
		/// @param methodName: Name of target RPC method which be called by remote or even locally.
		/// @param msgPackObj: Object which be annotated with `MessagePackObject`
		public async Task RPC(string methodName, object msgPackObj) {
			if (!rpcMethodIds.TryGetValue(methodName, out var methodId)) {
				throw new Exception($"Not found methodId for method: {methodName}");
			}
			await DkPhotonConnector.SendRPC(this.id, methodId, msgPackObj);
		}

		internal object InvokeRpcMethod(int methodId, object[] parameters) {
			// mono.SendMessage(this.rpcMethods[methodId].Name, parameters);
			return this.rpcMethods[methodId].Invoke(this.mono, parameters);
		}
	}
}
