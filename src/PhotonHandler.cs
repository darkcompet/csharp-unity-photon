namespace Tool.Compet.Photon {
	using System;
	using System.Collections.Generic;
	using UnityEngine;

	/// Make Photon can work with Unity main thread (Update loop).
	/// By using this handler, we can send task in background to main thread via `Post()` method,
	/// so they will be executed at update loop (late update, fixed update,...).
	internal class PhotonHandler : MonoBehaviour {
		internal static PhotonHandler instance;

		/// Indicate some action was queued and are in pending state.
		/// This is NOT equivalent to check with `pendingActions.Count > 0`
		/// since we have wrapped `pendingActions` and `runningActions` in `ExecutePendingActions()`.
		private volatile bool hasPendingAction;

		/// Actions are pending for execution.
		private List<Action> pendingActions = new(8);

		/// Actions will be executed at update loop.
		private List<Action> runningActions = new(8);

		/// We instantiate instance before use.
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Initialize() {
			if (instance == null) {
				instance = new GameObject("PhotonHandler").AddComponent<PhotonHandler>();
				DontDestroyOnLoad(instance.gameObject);
			}
		}

		// Note: This is commented out since if it was called at background, it causes problem
		// when working with Unity engine that we must call Unity methods at main thread.
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
				hasPendingAction = true;
			}
		}

#if !PHOTON_RUN_AT_FIXED_UPDATE && !PHOTON_RUN_AT_LATE_UPDATE
		/// Called by Unity engine.
		private void Update() {
			if (hasPendingAction) {
				ExecutePendingActions();
			}
		}
#endif

		/// Called by Unity engine.
#if PHOTON_RUN_AT_FIXED_UPDATE
		private void FixedUpdate() {
			if (hasPendingAction) {
				ExecutePendingActions();
			}
		}
#endif

		/// Called by Unity engine.
#if PHOTON_RUN_AT_LATE_UPDATE
		private void LateUpdate() {
			if (hasPendingAction) {
				ExecutePendingActions();
			}
		}
#endif

		/// Execute pending actions which posted from other threads.
		/// For better performance, should check `pendingActions` is not empty before call this.
		/// TechNote: this is optimized by reducing multiple lock times to one lock when handle with `pendingActions`.
		/// Ref: https://answers.unity.com/questions/305882/how-do-i-invoke-functions-on-the-main-thread.html
		private void ExecutePendingActions() {
			// Switch pending repo to running repo.
			// So we are free to work with pending repo without locking.
			// After below lock, aother threads will post to own running repo instead.
			List<Action> pendingRepo;

			lock (pendingActions) {
				pendingRepo = pendingActions;
				pendingActions = runningActions;
				runningActions = pendingRepo;

				// At next step, we will execute all pending actions and clear them.
				// But we MUST mark pending action are empty here since we are in safe (lock state).
				// If we mark at outside of this lock, this flag was maybe set AFTER some thread
				// at `Post()` method, will cause this flag does not indicate exact state of pending action.
				hasPendingAction = false;
			}

			// At this time, we are free to do/modify pending repo since
			// other threads are looking at running repo :)
			for (int index = 0, N = pendingRepo.Count; index < N; ++index) {
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

		// Just for reference.
		// public static void RunAsync(Action action) {
		// 	System.Threading.ThreadPool.QueueUserWorkItem(o => action());
		// }

		// Just for reference.
		// public static void RunAsync(Action<object> action, object state) {
		// 	System.Threading.ThreadPool.QueueUserWorkItem(o => action(o), state);
		// }
	}
}
