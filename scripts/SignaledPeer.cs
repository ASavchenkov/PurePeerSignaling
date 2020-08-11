using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
/*
TL;DR Poor Mans Polymorphism

Since we can't extend WebRTCPeerConnection
(Because GDNative scripts can't be extended outside of GDNative)
This class has a reference to the connection itself
and a bunch of administrative information/functionality
that is needed to keep things connected.
*/
public class SignaledPeer : Godot.Object
{
    public static Godot.Collections.Array intToGArr(int input)
	{
		return new Godot.Collections.Array(new int[] {input});
	}

    public struct BufferedCandidate
	{
		public string media;
		public int index;
		public string name;
	}

    [Signal]
    public delegate void ConnectionLost();

    int UID;
    static Godot.Collections.Dictionary RTCInitializer = new Godot.Collections.Dictionary();
    public WebRTCPeerConnection PeerConnection;
    Networking networking;
    List<BufferedCandidate> buffer = new List<BufferedCandidate>();

    public int relayUID;
    public bool remoteReady = false;
    public bool localReady = false;
    public bool AskForPeers = true;

    public static TimeSpan ANNOUNCE_TIME = new TimeSpan(0,0,3);
    public static TimeSpan RESET_TIME = new TimeSpan(0,0,6);
    public DateTime LastPing;

    public enum ConnectionState { NOMINAL, RELAY, RELAY_SEARCH, MANUAL};
    public ConnectionState currentState;
    System.Timers.Timer pollTimer;

    private string offerType;
    private string offerSdp;

    private Queue<int> relayCandidates;

    static SignaledPeer()
    {
        var stunServerArr = new Godot.Collections.Array(new String [] {"stun:stun.l.google.com:19302"});
		var stunServerDict= new Godot.Collections.Dictionary();
		stunServerDict.Add("urls",stunServerArr);
		RTCInitializer.Add("iceServers", stunServerDict);
    }

    //Please never call this.
    //Godot has a known issue with not having parameterless constructors.
    public SignaledPeer()
    {
    }

    //Networking is basically a singleton.
    //(Just poorly implemented).
    public SignaledPeer(int _UID, Networking networking, ConnectionState startingState, System.Timers.Timer _pollTimer)
    {
        UID = _UID;
        this.networking = networking;
        currentState = startingState;

        pollTimer = _pollTimer;
        pollTimer.Elapsed+= this.Poll;

        PeerConnection = new WebRTCPeerConnection();
        PeerConnection.Connect("session_description_created", this, "_OfferCreated");
	    PeerConnection.Connect("ice_candidate_created", this, "_IceCandidateCreated");
        
        PeerConnection.Initialize(RTCInitializer);
        networking.RTCMP.AddPeer(PeerConnection, UID);
        
        LastPing = DateTime.Now;
    }

    public void ResetConnection()
    {
        PeerConnection.Close();
        PeerConnection.Initialize(RTCInitializer);
        LastPing = DateTime.Now;
        currentState = ConnectionState.RELAY_SEARCH;
    }

    public void SetLocalDescription(string type, string sdp)
    {
        PeerConnection.SetLocalDescription(type, sdp);
        localReady = true;
        if(ReadyForIce())
            ReleaseBuffer();
    }
    public void SetRemoteDescription(string type, string sdp)
    {
        PeerConnection.SetRemoteDescription(type, sdp);
        remoteReady = true;
        if(ReadyForIce())
            ReleaseBuffer();
    }

    public void _OfferCreated(string type, string sdp)
    {
        SetLocalDescription(type,sdp);
        if(currentState == ConnectionState.RELAY)
            networking.RpcId(relayUID, "RelayOffer", UID, type, sdp);   
    }

    public void _IceCandidateCreated(string media, int index, string name)
    {
        switch(currentState)
        {
            case ConnectionState.NOMINAL:
                networking.RpcId(UID, "AddIceCandidate", networking.GetTree().GetNetworkUniqueId(), media, index, name);
                break;
            case ConnectionState.RELAY:
                networking.RelayIceCandidate(media, index, name, UID);
                break;
        }
    }

    private void ShuffleRelayCandidates()
    {
        Random r = new Random();
        relayCandidates = (Queue<int>) from i in networking.SignaledPeers.Keys.OrderBy(x => r.Next()) select i;
    }

    #region ICE_BUFFERING
    public bool ReadyForIce()
    {
        return remoteReady && localReady;
    }

    public void ReleaseBuffer()
    {
        foreach( BufferedCandidate candidate in buffer)
		    PeerConnection.AddIceCandidate(candidate.media, candidate.index, candidate.name);
    }
    //Automaticall skips buffering if ready for ice
    public void BufferIceCandidate(string media, int index, string name)
    {
        if(ReadyForIce())
            PeerConnection.AddIceCandidate(media,index, name);
        else
            buffer.Add(new BufferedCandidate{media = media, index = index, name = name});
    }
    #endregion

    public void RelayLost()
    {
        if(currentState == ConnectionState.RELAY)
        {
            currentState = ConnectionState.RELAY_SEARCH;
        }
    }

    //If nothing is there, don't do anything.
    private void TryDisconnect(Godot.Object original, string signal, Godot.Object target, string method)
    {
        if(original.IsConnected(signal, target, method))
            original.Disconnect(signal,target,method);
    }

    public void RelayConfirmed(int uid)
    {
        //If someone gets back to us late, we ignore them.
        if(currentState == ConnectionState.RELAY_SEARCH)
        {
            
            if(networking.SignaledPeers.ContainsKey(relayUID))
                TryDisconnect(networking.SignaledPeers[relayUID],"ConnectionLost",this, "RelayLost");
            
            relayUID = uid;
            networking.SignaledPeers[relayUID].Connect("ConnectionLost",this, "RelayLost");
            currentState = ConnectionState.RELAY;
            PeerConnection.CreateOffer();
        }
        
    }

    public void Poll(object source, System.Timers.ElapsedEventArgs e)
    {
        
        switch(currentState)
        {
            case ConnectionState.NOMINAL:
                if(RESET_TIME < (DateTime.Now - LastPing))
                {
                    PeerConnection.Close();
                    PeerConnection.Initialize(RTCInitializer);
                    remoteReady = false;
                    localReady = false;
                    buffer = new List<BufferedCandidate>();
                    ShuffleRelayCandidates();
                    EmitSignal("ConnectionLost");
                    currentState = ConnectionState.RELAY_SEARCH;
                    
                }
                break;
            case ConnectionState.RELAY_SEARCH:
                if(relayCandidates.Count == 0)
                    ShuffleRelayCandidates();

                //If it's still zero, then there's no relay candidates
                //so we should do nothing.
                if(relayCandidates.Count!=0)
                {
                    int nextCandidate = relayCandidates.Dequeue();
                    networking.RpcId(nextCandidate, "CheckRelay", UID);
                }
                break;
        }

        if((bool)networking.RTCMP.GetPeer(UID)["connected"])
        {

            if(networking.SignaledPeers.ContainsKey(relayUID))
                TryDisconnect(networking.SignaledPeers[relayUID], "ConnectionLost", this, "RelayLost");
            LastPing = DateTime.Now;
            currentState = ConnectionState.NOMINAL;
        }


    }


    //We handle all of our own interactions with the RTCMP singleton.
    ~SignaledPeer()
    {
        try{
        PeerConnection.Close();
        networking.RTCMP.RemovePeer(UID);
        }catch(Exception e)
        {
            GD.Print(e.ToString());
        }
    }
}
