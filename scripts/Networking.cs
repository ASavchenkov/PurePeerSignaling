using Godot;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using MessagePack;

/*
Overall mode of operation:

1) HandshakeServer connects a new peer to us.
	They then request a list of all our peers, and ask us to relay
	sdp and ICE messages back and forth.

2) Handshake Client connects us to a single member of the mesh.
	We request a list of all peers they're connected to.
	We call RPCs through them to relay signaling info to these peers
	We then ask those peers for their peers until we have connected to everyone.
*/

public class Networking : Node
{

	[Signal]
	public delegate void ConnectedToSession(int uid);

	
	public WebRTCMultiplayer RTCMP = new WebRTCMultiplayer();
	public Dictionary<int, SignaledPeer> SignaledPeers = new Dictionary<int, SignaledPeer>();
	
	HandshakeServer handshakeServer;
	HandshakeClient handshakeClient;

	string url = "ws://192.168.1.143:3476";
	string secret = "secret";

	public System.Timers.Timer PollTimer = new System.Timers.Timer(1000);

	private List<int> UnsearchedPeers = new List<int>();
	
	#region SETUP
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Running Ready");
		handshakeServer = (HandshakeServer) GetNode("HandshakeServer");
		handshakeClient = (HandshakeClient) GetNode("HandshakeClient");
		
		RTCMP.Initialize(1,false);
		GetTree().NetworkPeer = RTCMP;

		PollTimer.AutoReset = true;
		PollTimer.Start();
		PollTimer.Elapsed+=LaunchPing;

	}
	
	public void _SetURL(string url)
	{
		this.url = url;
	}

	public void _SetSecret(string secret)
	{
		this.secret = secret;
	}

	public void _JoinMesh()
	{
		GD.Print("_JoinMesh");
		//first you need to remove yourself from the current mesh.
		RTCMP.Close();
		SignaledPeers = new Dictionary<int, SignaledPeer>();
		//and stop your handshakeServer if you have one running.
		handshakeServer._StopServer();
		//Then tell the handshakeClient to do the handshake.
		handshakeClient.Handshake(url, secret);
	}

	public void _StartServer()
	{
		handshakeServer._StartServer(secret);
	}
	public void _StopServer()
	{
		handshakeServer._StopServer();
	}
	
	#endregion

	#region SIGNALING

	[Remote]
	public void CheckRelay(int uid)
	{
		if (SignaledPeers[uid].currentState == SignaledPeer.ConnectionState.NOMINAL)
			RpcId(GetTree().GetRpcSenderId(), "RelayConfirmed", uid);
	}

	[Remote]
	public void RelayConfirmed(int uid)
	{
		SignaledPeers[uid].RelayConfirmed(GetTree().GetRpcSenderId());
	}

	[Remote]
	public void RelayOffer(int uid, string type, string sdp)
	{
		this.RpcId(uid, "ReceiveOffer", GetTree().GetRpcSenderId() ,type, sdp);
	}

	//Should never be called from anywhere other than RelayOffer
	[Remote]
	public void ReceiveOffer(int uid, string type, string sdp)
	{
		GD.Print("RECEIVE OFFER: ", type);

		SignaledPeer peer;
		
		if(type == "offer")
		{
			//So we have them as a peer, and want to reset the connection.
			if (SignaledPeers.ContainsKey(uid))
			{
				peer = SignaledPeers[uid];
				peer.ResetConnection();
			}
			else
			{
				peer = new SignaledPeer(uid, this, SignaledPeer.ConnectionState.RELAY_SEARCH, PollTimer);
			}
			peer.RelayConfirmed(GetTree().GetRpcSenderId());
		}
		else
		{
			//It's an answer, so we should already have a peer in the system,
			//and shouldn't need to do anything funky with it.
			peer = SignaledPeers[uid];
		}

		peer.SetRemoteDescription(type,sdp);
	}

	[Remote]
	public void RelayIceCandidate(string media, int index, string name, int uid)
	{
		GD.Print("NETWORKING ICE CANDIDATE RELAYED");
		this.RpcId(uid,"AddIceCandidate", GetTree().GetRpcSenderId(), media, index, name);
	}

	[Remote]
	public void AddIceCandidate( int senderUID, string media, int index, string name)
	{

		GD.Print("ADDING ICE CANDIDATE");
		var peer = SignaledPeers[senderUID];
		peer.BufferIceCandidate(media, index, name);
		
	}
	#endregion


	#region PEER SEARCH
	[Remote]
	public void AddPeers(byte[] packet)
	{
		int referrer = GetTree().GetRpcSenderId();
		List<int> newPeers = MessagePackSerializer.Deserialize<List<int>>(packet);
		foreach(int uid in newPeers)
		{
			//add them if we don't already have them.
			if (!SignaledPeers.ContainsKey(uid) && !(uid == RTCMP.GetUniqueId()))
			{
				GD.Print("ADDING THIS PEER: ", uid);
				SignaledPeer newPeer = new SignaledPeer(uid, this, SignaledPeer.ConnectionState.RELAY_SEARCH, PollTimer);
				SignaledPeers.Add(uid, newPeer);
				UnsearchedPeers.Add(uid);
			}
		}
	}

	//This can relay peers that have connected but have yet to ask for peers,
	//resulting in both peers getting each other's uids, and sending offers.
	//May need to cache peerUIDs immediately on getting a fresh offer.
	[Remote]
	public void GetPeerUIDs()
	{
		int requester = GetTree().GetRpcSenderId();
		
		GD.Print("Getting peers for: ", requester);
		foreach( var key in SignaledPeers.Keys)
			GD.Print(key);
		
		//maybe there's a way to cast non-generic collections
		//to generic collections?
		//For now this will have to do.
		List<int> peerList = new List<int>();
		foreach( int uid in SignaledPeers.Keys)
			peerList.Add(uid);
		
		byte[] packet = MessagePackSerializer.Serialize(peerList);
		GD.Print("SENDING BACK PEER UIDS");
		this.RpcId(requester,"AddPeers", packet);
	}



	//handshakeCounterpart is our first point of contact.
	public void StartPeerSearch(int handshakeCounterpart)
	{
		this.RpcId(handshakeCounterpart,"GetPeerUIDs");
	}
	#endregion

	public void LaunchPing(object source, System.Timers.ElapsedEventArgs e)
	{
		Rpc("Ping");
	}

	[Remote]
	public void Ping()
	{
		GD.Print("receiving ping: ", GetTree().GetRpcSenderId());
		SignaledPeers[GetTree().GetRpcSenderId()].LastPing = System.DateTime.Now;
	}


	public override void _Process(float delta)
	{

		//For each peer in our dictionary mapping peers to relays
		List<int> toRemove = new List<int>();
		foreach( int uid in UnsearchedPeers)
		{
			//check if they're connected.
			if ( SignaledPeers[uid].currentState == SignaledPeer.ConnectionState.NOMINAL)
			{
				//if they are, then ask them for their peers
				this.RpcId(uid,"GetPeerUIDs");
				//then remove them from the list.
				toRemove.Add(uid);
			}
		}
		
		//and remove them from the dictionary.
		foreach( int uid in toRemove)
			UnsearchedPeers.Remove(uid);
	}

}
