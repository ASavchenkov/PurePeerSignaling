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

    //used to initialize every peer with some stun servers.
    public Godot.Collections.Dictionary RTCInitializer = new Godot.Collections.Dictionary();
    
    public WebRTCMultiplayer RTCMP = new WebRTCMultiplayer();

    
    public HandshakeServer handshakeServer;
    public HandshakeClient handshakeClient;


    //The list of peers we have yet to send offers to.
    //maps the uid of the peer to the uid of the relay peer.
    public Dictionary<int,int> peerRelays = new Dictionary<int,int>();


    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        GD.Print("Running Ready");
        //build the initializer dictionary since dictionary literals aren't a thing in c#
        var stunServerArr = new Godot.Collections.Array(new String [] {"stun:stun.l.google.com:19302"});
        var stunServerDict= new Godot.Collections.Dictionary();
        stunServerDict.Add("urls",stunServerArr);
        RTCInitializer.Add("iceServers", stunServerDict);
        
        handshakeServer = (HandshakeServer) GetNode("HandshakeServer");
        handshakeClient = (HandshakeClient) GetNode("HandshakeClient");
        
        RTCMP.Initialize(1,false);
        GetTree().NetworkPeer = RTCMP;
    }
    
    public void _JoinMesh()
    {
        GD.Print("_JoinMesh");
        //first you need to remove yourself from the current mesh.
        RTCMP.Close();
        //and stop your handshakeServer if you have one running.
        handshakeServer._StopServer();
        //Then tell the handshakeClient to do the handshake.
        var textBox = (TextEdit) GetNode("../UI/TextEdit");
        handshakeClient.Handshake(textBox.Text);
    }

    public Godot.Collections.Array intToGArr(int input)
    {
        return new Godot.Collections.Array(new int[] {input});
    }

    public WebRTCPeerConnection AddPeer(Godot.Object signalReceiver, int peerID)
    {
        
        var peer = new WebRTCPeerConnection();
        peer.Initialize(RTCInitializer);
        peer.Connect("session_description_created", signalReceiver, "_OfferCreated",intToGArr(peerID));
        peer.Connect("ice_candidate_created", signalReceiver, "_IceCandidateCreated",intToGArr(peerID));
        
        RTCMP.AddPeer(peer, peerID);
        return peer;
    }

    /* END SETUP RESPONSIBILITIES */
    /* START RESPONSE RESPONSIBILITIES */
    
    //This should only be called for those we have yet to connect to
    //so we always need to relay offers.
    public void _OfferCreated(String type, String sdp, int uid)
    {
        var peer = (WebRTCPeerConnection) RTCMP.GetPeer(uid)["connection"];
        peer.SetLocalDescription(type, sdp);
        
        this.RpcId(peerRelays[uid],"RelayOffer", RTCMP.GetUniqueId(), uid, type, sdp);
        
        GD.Print("NETWORKING OFFER CREATED");
    }

    [Remote]
    public void RelayOffer(int senderUID, int uid, string type, string sdp)
    {
        this.RpcId(uid, "ReceiveOffer", senderUID ,type, sdp);
    }

    //this only ever happens as a response to a peer relaying an offer to us
    [Remote]
    public void ReceiveOffer(int uid, string type, string sdp)
    {
        GD.Print("RECEIVE OFFER");
        var peer = this.AddPeer(this, uid);
        peer.SetRemoteDescription(type, sdp);

        //we make sure to use the same peer to send packets back for now
        //since it's the only one we know is connected to that peer for sure.
        if(type == "offer")
            peerRelays.Add(uid, GetTree().GetRpcSenderId());
    }

    [Remote]
    public void AddIceCandidate( int senderUID, string media, int index, string name)
    {
        GD.Print("ADDING ICE CANDIDATE OVER RPC");
        var peer = (WebRTCPeerConnection) RTCMP.GetPeer(senderUID)["connection"];
        var err = peer.AddIceCandidate(media, index, name);
        GD.Print("ERR: ", err);
    }

    public void _IceCandidateCreated(String media, int index, String name, int uid)
    {
        if((bool)RTCMP.GetPeer(uid)["connected"])
            this.RpcId(uid,"AddIceCandidate", RTCMP.GetUniqueId(), media, index, name);
        //we don't need to check connectivity  for the relay
        //because peers only get added to peerRelays when ["connected"] is true.
        else if (peerRelays.ContainsKey(uid))
        {
            this.RpcId(peerRelays[uid],"RelayIceCandidate", RTCMP.GetUniqueId(), media, index, name, uid);
        }
        GD.Print("NETWORKING ICE CANDIDATE CREATED");
    }


    [Remote]
    public void RelayIceCandidate( int senderUID, string media, int index, string name, int uid)
    {
        GD.Print("NETWORKING ICE CANDIDATE RELAYED");
        this.RpcId(uid,"AddIceCandidate", senderUID, media, index, name);
        
    }


    /* END RESPONSE RESPONSIBILITIES */
    /* START CONNECTIVITY RESPONSIBILITIES */


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
                WebRTCPeerConnection newPeer = this.AddPeer(this, uid);
                //Immediately create the offer since we're the ones offering.
                newPeer.CreateOffer();
                peerRelays.Add(uid,referrer);
            }
        }
    }

    [Remote]
    public void GetPeerUIDs()
    {
        int requester = GetTree().GetRpcSenderId();

        //maybe there's a way to cast non-generic collections
        //to generic collections?
        //For now this will have to do.
        List<int> peerList = new List<int>();
        foreach( int uid in RTCMP.GetPeers().Keys)
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

    public void _DispPeers()
    {
        string peerString = "";
        foreach(int uid in RTCMP.GetPeers().Keys)
        {
            peerString += uid.ToString() + ": " + ((bool) RTCMP.GetPeer(uid)["connected"]).ToString() + "\n";
        }
        RichTextLabel display = (RichTextLabel) GetNode("../UI/PeerList");
        display.Text  = peerString;
    }

    public override void _Process(float delta)
    {
        //For each peer in our dictionary mapping peers to relays
        ArrayList toRemove = new ArrayList();
        foreach( int uid in peerRelays.Keys)
        {
            //check if they're connected.
            if ( (bool)RTCMP.GetPeer(uid)["connected"])
            {
                //if they are, then ask them for their peers
                this.RpcId(uid,"GetPeerUIDs");
                toRemove.Add(uid);
            }
        }
        
        //and remove them from the dictionary.
        foreach( int uid in toRemove)
            peerRelays.Remove(uid);

        
        _DispPeers();
    }

}
