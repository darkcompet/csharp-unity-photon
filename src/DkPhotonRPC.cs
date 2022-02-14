namespace Tool.Compet.Photon {
	using System;

	/// Remote Procedure Call.
	/// Each hub client will annotate this attribute on each method
	/// to make it callable from remote server.
	/// TechNote: Extends `Attribute` to make this class is collectable via reflection.
	public class DkPhotonRPC : Attribute { }
}
