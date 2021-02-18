using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
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

    [MessagePackObject(keyAsPropertyName:true)]
    public struct BufferedCandidate
	{
        
		public string media;
		public int index;
		public string name;

        public BufferedCandidate(string media, int index, string name)
        {
            this.media = media;
            this.index = index;
            this.name = name;
        }
	}

    [Signal]
    public delegate void ConnectionLost();

    [Signal]
    public delegate void BufferedOfferUpdated(Offer bufferedOffer);

    [Signal]
    public delegate void StatusUpdated(ConnectionStateMachine currentState);
    
    [Signal]
    public delegate void Delete(); 

    public int UID {get; private set;}
    static Godot.Collections.Dictionary RTCInitializer = new Godot.Collections.Dictionary();
    public WebRTCPeerConnection PeerConnection;
    Networking networking;
    List<BufferedCandidate> buffer = new List<BufferedCandidate>();

    public int relayUID;
    private bool remoteReady = false;
    private bool localReady = false;
    private bool AskForPeers = true;

    public static TimeSpan VOTE_TIME = new TimeSpan(0,0,3);
    public static TimeSpan RESET_TIME = new TimeSpan(0,0,6);
    public DateTime LastPing;
    private SlushNode slushNode;
    private System.Timers.Timer PollTimer;
    public enum ConnectionStateMachine { NOMINAL, RELAY, RELAY_SEARCH, MANUAL};
    
    private ConnectionStateMachine _CurrentState;
    public ConnectionStateMachine CurrentState
    {
        get{return _CurrentState;}
        set
        {
            _CurrentState = value;
            EmitSignal(nameof(StatusUpdated),_CurrentState);
        }
    }

    private bool initiator = false;

    private Queue<int> relayCandidates = new Queue<int>();

    [MessagePackObject(keyAsPropertyName: true)]
    public class Offer : Godot.Object
    {

        public int UID;
        public int assignedUID = -1; //Will be -1 if this is a response.
        public string type;
        public string sdp;
        public List<BufferedCandidate> ICECandidates = new List<BufferedCandidate>();

        public Offer(int UID, int assignedUID, string type, string sdp)
        {
            this.UID = UID;
            this.assignedUID = assignedUID;
            this.type = type;
            this.sdp = sdp;
        }
    }
    public Offer BufferedOffer {get; private set;}

    static SignaledPeer()
    {
        var stunServerArr = new Godot.Collections.Array(new String [] {"stun:stun.l.google.com:19302"});
		var stunServerDict= new Godot.Collections.Dictionary();
		stunServerDict.Add("urls",stunServerArr);
		RTCInitializer.Add("iceServers", stunServerDict);
    }

    //PLEASE NEVER CALL THIS.
    //Godot has a known issue with not having parameterless constructors.
    public SignaledPeer()
    {
    }

    //Networking is basically a singleton.
    public SignaledPeer(int _UID, Networking networking, ConnectionStateMachine startingState, System.Timers.Timer _PollTimer, bool _initiator)
    {
        
        UID = _UID;
        this.networking = networking;
        CurrentState = startingState;
        initiator = _initiator;
        PollTimer = _PollTimer;
        PollTimer.Elapsed += Poll;

        PeerConnection = new WebRTCPeerConnection();
        PeerConnection.Connect("session_description_created", this, "_OfferCreated");
	    PeerConnection.Connect("ice_candidate_created", this, "_IceCandidateCreated");
        PeerConnection.Initialize(RTCInitializer);
        
        //Setting up the voting mechanism.
        PackedScene slushScene = GD.Load<PackedScene>("res://addons/PurePeerSignaling/SlushNode.tscn");
        slushNode = (SlushNode) slushScene.Instance();
        slushNode.Name = UID.ToString();
        networking.GetNode("SlushNodes").AddChild(slushNode);
        slushNode.proposal = false;
        Connect(nameof(Delete),slushNode,"queue_free");
        
        LastPing = DateTime.Now;
        GD.Print("SIGNALED PEER CONSTRUCTOR");
    }


    public void ResetConnection()
    {
        PeerConnection.Close();
        PeerConnection.Initialize(RTCInitializer);
        remoteReady = false;
        localReady = false;
        buffer = new List<BufferedCandidate>();
        ShuffleRelayCandidates();
        CurrentState = ConnectionStateMachine.RELAY_SEARCH;
        Poll();
    }

    public void SetLocalDescription(string type, string sdp)
    {
        PeerConnection.SetLocalDescription(type, sdp);
        localReady = true;
        if(ReadyForIce())
            ReleaseBuffer();
        GD.Print("SET LOCAL DESCRIPTION");
        Poll();
    }
    public void SetRemoteDescription(string type, string sdp)
    {
        PeerConnection.SetRemoteDescription(type, sdp);
        remoteReady = true;
        if(ReadyForIce())
            ReleaseBuffer();
        GD.Print("SET REMOTE DESCRIPTION");
        Poll();
    }

    public void _OfferCreated(string type, string sdp)
    {
        SetLocalDescription(type,sdp);
        if(CurrentState == ConnectionStateMachine.RELAY)
            networking.RpcId(relayUID, "RelayOffer", UID, type, sdp);
        else if(CurrentState == ConnectionStateMachine.MANUAL)
        {
            GD.Print("manual offer created");
            BufferedOffer = new Offer(networking.RTCMP.GetUniqueId(), UID, type, sdp);
            EmitSignal(nameof(BufferedOfferUpdated), BufferedOffer);
        }

        GD.Print("OFFER CREATED: ", type); 
    }

    public void _IceCandidateCreated(string media, int index, string name)
    {
        switch(CurrentState)
        {
            case ConnectionStateMachine.NOMINAL:
                networking.RpcId(UID, "AddIceCandidate", networking.GetTree().GetNetworkUniqueId(), media, index, name);
                break;
            case ConnectionStateMachine.RELAY:
                networking.RpcId(relayUID,"RelayIceCandidate",media, index, name, UID);
                break;
            case ConnectionStateMachine.MANUAL:
                BufferedOffer.ICECandidates.Add(new BufferedCandidate(media, index, name));
                EmitSignal(nameof(BufferedOfferUpdated), BufferedOffer);
                break;
            default:
                GD.Print("ICE GENERATION IGNORED");
                break;
        }
    }

    private void ShuffleRelayCandidates()
    {
        Random r = new Random();
        IEnumerable<int> filtered = networking.SignaledPeers.Keys.Where(uid => networking.SignaledPeers[uid].CurrentState == ConnectionStateMachine.NOMINAL);
        relayCandidates = new Queue<int>(filtered.OrderBy(x => x));
        GD.Print("ShuffleRelayCandidates: ", relayCandidates.Count);
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
        Poll();
    }
    //Automaticall skips buffering if ready for ice
    public void BufferIceCandidate(string media, int index, string name)
    {
        if(ReadyForIce())
            PeerConnection.AddIceCandidate(media,index, name);
        else
            buffer.Add(new BufferedCandidate{media = media, index = index, name = name});
        Poll();
    }
    #endregion

    public void RelayLost()
    {
        if(CurrentState == ConnectionStateMachine.RELAY)
        {
            CurrentState = ConnectionStateMachine.RELAY_SEARCH;
            Poll();
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
        if(CurrentState == ConnectionStateMachine.RELAY_SEARCH)
        {
            //Disconnect the previous relay
            //If it's even still there
            if(networking.SignaledPeers.ContainsKey(relayUID))
                TryDisconnect(networking.SignaledPeers[relayUID],nameof(ConnectionLost),this, nameof(RelayLost));
            
            //swap to new relay
            relayUID = uid;

            networking.SignaledPeers[relayUID].Connect(nameof(ConnectionLost),this, nameof(RelayLost));
            if(initiator)
                PeerConnection.CreateOffer();

            CurrentState = ConnectionStateMachine.RELAY;
            GD.Print("RELAY CONFIRMED");
            Poll();
        }
        
    }

    public void DeleteSelf()
    {
        GD.Print("Goodbye :( ", this.UID);
        EmitSignal(nameof(Delete));
        PollTimer.Elapsed-=Poll;
    }

    public void Poll(object source, System.Timers.ElapsedEventArgs e)
    {
        Poll();
    }

    public void Poll()
    {

        if(VOTE_TIME < (DateTime.Now - LastPing) && CurrentState != ConnectionStateMachine.MANUAL)
            slushNode.proposal = true;
        else
            slushNode.proposal = false;

        //If enough others have voted to DC this peer, DC immediately.
        //Integer math is fine here, since the threshold is an integer anyways.
        GD.Print(slushNode.Name, " Votes: ", slushNode.consensus, slushNode.confidence0, " ", slushNode.confidence1);
        
        if(slushNode.consensus && slushNode.confidence1 == 10)
        {
            DeleteSelf();
            return;
        }


        switch(CurrentState)
        {
            case ConnectionStateMachine.NOMINAL:

                if(RESET_TIME < (DateTime.Now - LastPing))
                {
                    ResetConnection();
                    GD.Print("emitting CONNECTION LOST");
                    EmitSignal(nameof(ConnectionLost));
                    CurrentState = ConnectionStateMachine.RELAY_SEARCH;
                }
                break;
            case ConnectionStateMachine.RELAY_SEARCH:
                if(relayCandidates.Count == 0)
                    ShuffleRelayCandidates();
                //If it's still zero, then there's no relay candidates
                //so we should do nothing.
                if(relayCandidates.Count!=0)
                {
                    int nextCandidate = relayCandidates.Dequeue();
                    if(
                        !networking.SignaledPeers.ContainsKey(nextCandidate) ||
                        !(networking.SignaledPeers[nextCandidate].CurrentState == ConnectionStateMachine.NOMINAL)
                    ) break;
                    //No use testing a candidate that _we_ aren't even connected to.
                    //Realistically can only happen if someone leaves while you're joining,
                    //or if a peer that's still joining ends up here.
                    //So should be a very uncommon occurrence.

                    GD.Print("NEXT CANDIDATE:", nextCandidate);
                    networking.RpcId(nextCandidate, "CheckRelay", UID);
                }
                break;
        }

        if(networking.RTCMP.HasPeer(UID) && PeerConnection.GetConnectionState() == WebRTCPeerConnection.ConnectionState.Connected && CurrentState != ConnectionStateMachine.NOMINAL)
        {
            if(networking.SignaledPeers.ContainsKey(relayUID))
                TryDisconnect(networking.SignaledPeers[relayUID], nameof(ConnectionLost), this, nameof(RelayLost));
            LastPing = DateTime.Now;
            CurrentState = ConnectionStateMachine.NOMINAL;
            
        }

    }

}
