using Godot;
using System;

public class Example : Spatial
{

	private Networking networking;
	public override void _Ready()
	{
		networking = (Networking) GetNode("Networking");
	}
   	public void _DispPeers()
	{
		string peerString = "";
		foreach(int uid in networking.RTCMP.GetPeers().Keys)
		{
			peerString += uid.ToString() + ": " + ((bool) networking.RTCMP.GetPeer(uid)["connected"]).ToString() + "\n";
		}
		RichTextLabel display = (RichTextLabel) GetNode("UI/PeerList");
		display.Text  = peerString;
	}
}
