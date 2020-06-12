using Godot;
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting;
using MessagePack;

//uses WebSocketServer to connect to first peer
//then uses WebRTCPeer and SignalServer rpc calls
//to connect to rest of peers.
public class HandshakeClient : Node
{
    
    private bool authenticated = false;
    
    private Networking networking;
    public WebSocketClient WSClient = new WebSocketClient();
    private WebRTCPeerConnection handshakePeer;
    private int handshakeCounterpart = -1;
    private string secret;

    public override void _Ready()
    {
        WSClient.Connect("data_received", this, "_OnData");
        WSClient.Connect("connection_established",this, "_Connected");
        WSClient.Connect("connection_closed", this, "_ConnectionClosed");
        networking = (Networking) GetNode("..");
    }
    public void _ConnectionClosed(bool wasCleanClose)
    {
        GD.Print("_ConnectionClosed");
    }
    public Error Handshake(string serverLink, string _secret)
    {
        GD.Print("Handshake");
        SetProcess(true);
        handshakeCounterpart = -1;
        secret = _secret;
        
        return WSClient.ConnectToUrl(serverLink);
    }

    //called when the WSClient emits "connection_established" signal
    //send the secret to the server so they know we're the invited peer.
    private Error _Connected(String protocol)
    {
        GD.Print("_Connected");
        WSClient.GetPeer(1).SetWriteMode(WebSocketPeer.WriteMode.Binary);
        
        var authDict = new Dictionary<string, dynamic>();
        authDict["type"]= "authentication";
        authDict["secret"] = secret;

        byte[] authPayload = MessagePackSerializer.Serialize(authDict);
        
        var err = WSClient.GetPeer(1).PutPacket(authPayload);
        GD.Print(err);
        return err;
    }

    private void _OnData()
    {
        byte[] packet = WSClient.GetPeer(1).GetPacket();
        Dictionary<string, dynamic> data = MessagePackSerializer.Deserialize<Dictionary<string,dynamic>>(packet);
        
        if(!(handshakePeer is null))
            GD.Print(handshakePeer.GetConnectionState());
        
        if(authenticated)
        {
            if(data["type"] == "answer")
            {
                handshakePeer.SetRemoteDescription( data["type"], data["sdp"]);
            }
            else if(data["type"] == "iceCandidate")
            {
                handshakePeer.AddIceCandidate(
                    data["media"],
                    data["index"],
                    data["name"]
                    );
            }
        }
        else{
            if(data["type"] == "authentication" && data["status"] == "success")
            {
                GD.Print("Authentication successful");
                authenticated = true;
                
                //we need to cast deserialized integers
                //because this seems to serialize to uint32 by default.
                //This will not be an issue when we move away from dynamics.
                networking.RTCMP.Initialize( (int) data["assignedUID"],false);
                GetTree().NetworkPeer = networking.RTCMP;
                //Now we can call RPCs on peers when we get some.

                //create a peer and link it to us.
                handshakeCounterpart = data["uid"];
                handshakePeer = networking.AddPeer(this, data["uid"]);
                handshakePeer.CreateOffer();
            }
        }
        
    }

    //_OfferCreated should never be of type "answer" during handshake phase.
    public void _OfferCreated(String type, String sdp, int uid)
    {
        GD.Print("_OfferCreated");
        handshakePeer.SetLocalDescription(type, sdp);
        
        var offerDict = new Dictionary<string,dynamic>();
        offerDict.Add("type",type);
        offerDict.Add("sdp",sdp);
        offerDict.Add("uid",networking.RTCMP.GetUniqueId());
        
        byte[] payload =  MessagePackSerializer.Serialize(offerDict);
        WSClient.GetPeer(1).PutPacket(payload);
    }

    public void _IceCandidateCreated(String media, int index, String name, int uid)
    {
        GD.Print("ICE CANDIDATE CREATED");
        var iceDict = new Dictionary<string,dynamic>();
        iceDict.Add("type","iceCandidate");
        //this is our ID we're sending them, not the id we have them registered under
        iceDict.Add("uid",networking.RTCMP.GetUniqueId());
        iceDict.Add("media",media);
        iceDict.Add("index",index);
        iceDict.Add("name", name);
        byte[] payload =  MessagePackSerializer.Serialize(iceDict);
        WSClient.GetPeer(1).PutPacket(payload);
    }

    //when the WebRTCPeerConnection is CONNECTED, we hand off communication to it.
    private void _FinishHandshake()
    {
        GD.Print("_FinishHandshake");
        WSClient.DisconnectFromHost(reason:"Handshake Complete");
        handshakePeer.Disconnect("session_description_created",this,"_OfferCreated");
        handshakePeer.Disconnect("ice_candidate_created",this,"_IceCandidateCreated");
        handshakePeer.Connect("session_description_created",networking,"_OfferCreated",networking.intToGArr(handshakeCounterpart));
        handshakePeer.Connect("ice_candidate_created",networking,"_IceCandidateCreated",networking.intToGArr(handshakeCounterpart));
        
        SetProcess(false); //Our job is done here.
        networking.StartPeerSearch(handshakeCounterpart);
    }

    public override void _Process(float delta)
    {
        WSClient.Poll();
        if(!(handshakePeer is null) && (bool) networking.RTCMP.GetPeer(handshakeCounterpart)["connected"])
        {
            _FinishHandshake();
        }
    }
}
