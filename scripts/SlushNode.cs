using Godot;
using System;
using System.Collections.Generic;

/*
A node that implements a variation of the Slush protocol described in:
"Scalable and Probabilistic Leaderless BFT Consensus through Metastability"
Simple, easy to implement, and since we don't expect Byzantine nodes, safe.
*/
public class SlushNode : Node
{

    private T Clamp<T>(T input, T lower, T upper)
    where T : System.IComparable<T>
    {
        if(input.CompareTo((T)lower)<0)
            return lower;
        else if(input.CompareTo((T)upper)>0)
            return upper;
        return input;
    }

    //Caches votes.
    private class NodeStatus
    {
        public bool proposal;
        public bool consensus;
        public SignaledPeer peer;
        public NodeStatus(SignaledPeer peer)
        {
            this.peer = peer;
        }

        public static int count(int multiplier, bool proposal, bool consensus)
        {
            return multiplier * (proposal ? 1 : 0) + (consensus ? 1 : 0);
        }

        public int count(int multiplier)
        {
            return count(multiplier, proposal, consensus);
        }
        //For keeping track of whether this peer's values are likely to be well updated.
    }
    private Dictionary<int, NodeStatus> nodes = new Dictionary<int, NodeStatus>();
    
    public bool proposal = false;  //Our preferred outcome
    /*  We treat proposals essentially as nodes that have already gone past the point of no return.
        This means that you need a larger sample size, since even after consensus is reached,
        it is possible to sample a subset of nodes with proposals outweighing consensus.
    */
    private bool consensus = false; //What we currently believe to be the consensus.
    public int multiplier = 2; //How much weight should we give the proposal compared to the consensus?
    
    private int confidence0 = 0; //Confidence counters.
    private int confidence1 = 0;
    
    Networking networking;
    System.Timers.Timer pollTimer = new System.Timers.Timer(1000);

    [Remote]
    private void UpdateNodeStatus(bool proposal, bool consensus)
    {
        NodeStatus node;
        int sender = GetTree().GetRpcSenderId();
        if(!nodes.ContainsKey(sender))
        {
            node = new NodeStatus(networking.SignaledPeers[sender]);
            nodes[sender] = node;
        }
        else
            node = nodes[sender];
            
        node.proposal = proposal;
        node.consensus = consensus;
    }

    private void poll(object source, System.Timers.ElapsedEventArgs e)
    {


        #region CONSENSUS
        //This is our vote.
        int voteCount = NodeStatus.count(multiplier, proposal, consensus);
        int totalCount = multiplier +1;
        //We are just going to poll everyone since we don't yet expect large N.
        //When we start to test this on larger peer counts, we will add subsampling.
        foreach(NodeStatus status in nodes.Values)
        {
            if(status.peer.currentState == SignaledPeer.ConnectionStateMachine.NOMINAL)
            {
                voteCount+= status.count(multiplier);
                totalCount += multiplier + 1;
            }
        }

        bool sampleConsensus = voteCount > totalCount/2;


        if( sampleConsensus != consensus)
        {
            consensus = sampleConsensus;
            Rpc("UpdateNodeStatus", proposal, consensus);
        }
        #endregion
        
        #region CONFIDENCE
        if(consensus)
        {
            confidence1+=1;
            confidence0-=2;
        }
        else
        {
            confidence0-=2;
            confidence1+=1;
        }
        confidence0 = Clamp(confidence0,0,10);
        confidence1 = Clamp(confidence0,0,10);
        #endregion
    }

    public override void _Ready()
    {
        networking = (Networking) GetNode("/root/GameRoot/Networking");
        pollTimer.AutoReset = true;
        pollTimer.Start();
        pollTimer.Elapsed+=poll;
        
    }

//  // Called every frame. 'delta' is the elapsed time since the previous frame.
//  public override void _Process(float delta)
//  {
//      
//  }
}
