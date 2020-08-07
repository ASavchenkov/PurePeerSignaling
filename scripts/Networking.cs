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

	System.Timers.Timer connectivityTimer = new System.Timers.Timer(1000);

	private List<int> UnsearchedPeers = new List<int>();

	public SignaledPeer AddPeer(Godot.Object signalReceiver, int peerID)
	{
		var peer = new SignaledPeer(peerID, RTCMP);
		connectivityTimer.Elapsed+=peer.CheckTimeout;
		var peerConnection = peer.PeerConnection;
        peerConnection.Connect("session_description_created", signalReceiver, "_OfferCreated",SignaledPeer.intToGArr(peerID));
		peerConnection.Connect("ice_candidate_created", signalReceiver, "_IceCandidateCreated",SignaledPeer.intToGArr(peerID));
        
		SignaledPeers.Add(peerID,peer);
		return peer;
	}
	
	#region SETUP
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		GD.Print("Running Ready");
		handshakeServer = (HandshakeServer) GetNode("HandshakeServer");
		handshakeClient = (HandshakeClient) GetNode("HandshakeClient");
		
		RTCMP.Initialize(1,false);
		GetTree().NetworkPeer = RTCMP;

		connectivityTimer.AutoReset = true;
		connectivityTimer.Start();

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
	
	//This should only be called for those we have yet to connect to
	//so we always need to relay offers.
	public void _OfferCreated(String type, String sdp, int uid)
	{
		SignaledPeers[uid].SetLocalDescription(type,sdp);
		this.RpcId(SignaledPeers[uid].relayUID,"RelayOffer",  uid, RTCMP.GetUniqueId(), type, sdp);
		
		GD.Print("NETWORKING OFFER CREATED");
	}

	[Remote]
	public void RelayOffer(int uid, int senderUID, string type, string sdp)
	{
		this.RpcId(uid, "ReceiveOffer", senderUID ,type, sdp);
	}

	
	[Remote]
	public void ReceiveOffer(int uid, string type, string sdp)
	{
		GD.Print("RECEIVE OFFER: ", type);

		//if this is the case, we don't yet have them as a peer.
		SignaledPeer peer;
		if(type == "offer")
		{
			peer = AddPeer(this, uid);
			
			//we make sure to use the same peer to send packets back for now
			//since it's the only one we know is connected to that peer for sure.
			peer.relayUID = GetTree().GetRpcSenderId();
		}
		else
		{
			//It's an answer, so we should already have a peer in the system.
			peer = SignaledPeers[uid];
		}

		peer.SetRemoteDescription(type,sdp);
	}

	[Remote]
	public void AddIceCandidate( int senderUID, string media, int index, string name)
	{
		GD.Print("ADDING ICE CANDIDATE");
		var peer = SignaledPeers[senderUID];
		peer.BufferIceCandidate(media, index, name);
		
	}

	public void _IceCandidateCreated(String media, int index, String name, int uid)
	{
		if((bool)RTCMP.GetPeer(uid)["connected"])
			this.RpcId(uid,"AddIceCandidate", RTCMP.GetUniqueId(), media, index, name);
		//we don't need to check connectivity  for the relay
		//because peers only get added to peerRelays when ["connected"] is true.
		else if (SignaledPeers.ContainsKey(uid))
		{
			this.RpcId(SignaledPeers[uid].relayUID,"RelayIceCandidate", RTCMP.GetUniqueId(), media, index, name, uid);
		}
		GD.Print("NETWORKING ICE CANDIDATE CREATED");
	}


	[Remote]
	public void RelayIceCandidate( int senderUID, string media, int index, string name, int uid)
	{
		GD.Print("NETWORKING ICE CANDIDATE RELAYED");
		this.RpcId(uid,"AddIceCandidate", senderUID, media, index, name);
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
			if (!RTCMP.HasPeer(uid) && !(uid == RTCMP.GetUniqueId()))
			{
				GD.Print("ADDING THIS PEER: ", uid);
				SignaledPeer newPeer = this.AddPeer(this, uid);
				//Immediately create the offer since we're the ones offering.
				newPeer.PeerConnection.CreateOffer();
				newPeer.relayUID = referrer;
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
		GD.Print(SignaledPeers.Keys);
		
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


	[Remote]
	public void Ping()
	{
		//GD.Print("Got Ping");
		SignaledPeers[GetTree().GetRpcSenderId()].LastPing = System.DateTime.Now;
	}

	public override void _Process(float delta)
	{

		//For each peer in our dictionary mapping peers to relays
		List<int> toRemove = new List<int>();
		foreach( int uid in UnsearchedPeers)
		{
			//check if they're connected.
			if ( (bool)RTCMP.GetPeer(uid)["connected"])
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
