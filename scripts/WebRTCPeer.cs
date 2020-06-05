using Godot;
using System;
using System.Collections.Generic;
using System.Text;

//Exclusively handles creating and maintaining WebRTC connections
//and routing communications through the appropriate peerConnection.
public class WebRTCPeer : Node
{
    //used to initialize every peer with some stun servers.
    public Godot.Collections.Dictionary RTCInitializer = new Godot.Collections.Dictionary();
    
    //NOTE: RTCMP is a public variable.
    //Other nodes WILL modify/add peer data channels when VOIP gets involved.
    public WebRTCMultiplayer RTCMP = new WebRTCMultiplayer();
    
    //disconnect from everything and clear all peers.
    public void _OnClearMesh()
    {
        RTCMP.Close();
    }

    public void AddPeer(int ID)
    {
        var peer = new WebRTCPeerConnection();
        peer.Initialize(RTCInitializer);
        peer.Connect("session_description_created", this, "_OfferCreated");
        peer.Connect("ice_candidate_created", this, "_IceCandidateCreated");
        RTCMP.AddPeer(peer, ID);
        //note that we do not create an offer here.
        //This is handled by a separate node
        //hence why RTCMP is public
    }

    public void _OfferCreated()
    {

    }

    public void _IceCandidateCreated()
    {

    }

    public override void _Ready()
    {
        //build the initializer dictionary since dictionary literals aren't a thing in c#
        var stunServerArr = new Godot.Collections.Array(new String [] {"stun:stun.l.google.com:19302"});
        var stunServerDict= new Godot.Collections.Dictionary();
        stunServerDict.Add("urls",stunServerArr);
        RTCInitializer.Add("iceServers", stunServerDict);
    }

  // Called every frame. 'delta' is the elapsed time since the previous frame.
  public override void _Process(float delta)
  {

  }
}
