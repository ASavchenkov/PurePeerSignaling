using Godot;
using System;
using System.Collections.Generic;

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


    int UID;
    static Godot.Collections.Dictionary RTCInitializer = new Godot.Collections.Dictionary();
    public WebRTCPeerConnection PeerConnection;
    WebRTCMultiplayer RTCMP;
    List<BufferedCandidate> buffer = new List<BufferedCandidate>();

    public int relayUID;
    public bool remoteReady = false;
    public bool localReady = false;

    public static TimeSpan ANNOUNCE_TIME = new TimeSpan(0,0,3);
    public static TimeSpan RESET_TIME = new TimeSpan(0,0,6);
    public DateTime LastPing;
    
    public void ReleaseBuffer()
    {
        foreach( BufferedCandidate candidate in buffer)
		    PeerConnection.AddIceCandidate(candidate.media, candidate.index, candidate.name);
    }

    static SignaledPeer()
    {
        var stunServerArr = new Godot.Collections.Array(new String [] {"stun:stun.l.google.com:19302"});
		var stunServerDict= new Godot.Collections.Dictionary();
		stunServerDict.Add("urls",stunServerArr);
		RTCInitializer.Add("iceServers", stunServerDict);
        
    }

    public SignaledPeer(int _UID, WebRTCMultiplayer _RTCMP)
    {
        UID = _UID;
        RTCMP = _RTCMP;
        PeerConnection = new WebRTCPeerConnection();
        LastPing = DateTime.Now;
        // peerConnection.Connect("session_description_created", this, "_OfferCreated");
		// peerConnection.Connect("ice_candidate_created", this, "_IceCandidateCreated");
        
        RTCMP.AddPeer(PeerConnection, UID);
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

    public bool ReadyForIce()
    {
        return remoteReady && localReady;
    }

    //Automaticall skips buffering if ready for ice
    public void BufferIceCandidate(string media, int index, string name)
    {
        if(ReadyForIce())
            PeerConnection.AddIceCandidate(media,index, name);
        else
            buffer.Add(new BufferedCandidate{media = media, index = index, name = name});
    }

    
    public void CheckTimeout(object source, System.Timers.ElapsedEventArgs e)
    {
        if( RESET_TIME < (DateTime.Now - LastPing) )
        {   //then we start the reset process. Not written yet.
            
        }
    }
    //We handle all of our own interactions with the RTCMP singleton.
    ~SignaledPeer()
    {
        PeerConnection.Close();
        RTCMP.RemovePeer(UID);
    }
}
