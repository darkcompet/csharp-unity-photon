namespace Tool.Compet.Photon {
	using System;
	using System.Collections.Generic;
	using UnityEngine;

	internal class PhotonHandler : MonoBehaviour {
		internal static PhotonHandler instance;

		private List<Action> pendingActions = new(8);
		private List<Action> runningActions = new(8);

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Initialize() {
			if (instance == null) {
				instance = new GameObject("PhotonHandler").AddComponent<PhotonHandler>();
				DontDestroyOnLoad(instance.gameObject);
			}
		}

		// public static PhotonHandler Instance {
		// 	get {
		// 		if (instance == null) {
		// 			instance = FindObjectOfType<PhotonHandler>();
		// 			if (instance == null) {
		// 				instance = new GameObject("PhotonHandler").AddComponent<PhotonHandler>();
		// 			}
		// 		}
		// 		return instance;
		// 	}
		// }

		/// Lock and Queue an action to pending repo.
		public void Post(Action action) {
			lock (pendingActions) {
				pendingActions.Add(action);
			}
		}

#if !PHOTON_RUN_AT_FIXED_UPDATE && !PHOTON_RUN_AT_LATE_UPDATE
		/// Called by Unity engine.
		private void Update() {
			if (pendingActions.Count > 0) {
				ExecutePendingActions();
			}
		}
#endif

		/// Called by Unity engine.
#if PHOTON_RUN_AT_FIXED_UPDATE
		private void FixedUpdate() {
			if (pendingActions.Count > 0) {
				ExecutePendingActions();
			}
		}
#endif

		/// Called by Unity engine.
#if PHOTON_RUN_AT_LATE_UPDATE
		private void LateUpdate() {
			if (pendingActions.Count > 0) {
				ExecutePendingActions();
			}
		}
#endif

		/// Execute pending actions which posted from other threads.
		/// For better performance, should check `pendingActions` is not empty before call this.
		/// TechNote: this is optimized by reducing multiple lock times to one lock when handle with `pendingActions`.
		/// Ref: https://answers.unity.com/questions/305882/how-do-i-invoke-functions-on-the-main-thread.html
		private void ExecutePendingActions() {
			// Switch own running repo to pending repo.
			// We make other threads post to own running repo instead.
			List<Action> pendingRepo;
			lock (pendingActions) {
				pendingRepo = pendingActions;
				pendingActions = runningActions;
				runningActions = pendingRepo;
			}

			// At this time, we are free to do/modify pending repo since
			// other threads are looking at running repo :)
			for (int index = 0, N = pendingRepo.Count - 1; index < N; ++index) {
				pendingRepo[index]();
			}
			pendingRepo.Clear();
		}

		/// When Unity quits, it destroys objects in a random order.
		/// In principle, a Singleton is only destroyed when application quits.
		/// If any script calls Instance after it have been destroyed,
		/// it will create a buggy ghost object that will stay on the Editor scene
		/// even after stopping playing the Application.
		/// -> It is good to cancel/stop all pending method-invocation at this time.
		public void OnDestroy() {
			this.CancelInvoke();
			this.StopAllCoroutines();
		}
	}
}
