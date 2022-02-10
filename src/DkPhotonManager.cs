namespace Tool.Compet.Photon {
	using System;
	
	public class DkPhotonManager {
		public static readonly DkPhotonManager instance = new();
	
		public void LeaveRoom(Action onCompleted = null) {
			onCompleted?.Invoke();
		}
	}
}
