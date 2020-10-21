using Godot;
using System;

public class PeerList : VBoxContainer
{

    public void OnPeerAdded(SignaledPeer peer)
    {
        PackedScene scene = GD.Load<PackedScene>("res://addons/PurePeerSignaling/PeerItem.tscn");
        PeerItem peerItem = (PeerItem) scene.Instance();
        AddChild(peerItem);
        peerItem.Init(peer);
    }
}
