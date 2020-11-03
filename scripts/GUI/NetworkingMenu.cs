using Godot;
using System;
using MessagePack;

public class NetworkingMenu : CenterContainer
{
    TextEdit InputField;
    TextEdit OutputField; //This one is read only for the user.
    VBoxContainer PeerList;
    Label IDLabel;

    [Export]
    NodePath networkingPath;
    Networking networking;
    PeerItem SelectedItem = null;

    public override void _Ready()
    {
        InputField = (TextEdit) GetNode("Columns/IO Column/Input Field/TextEdit");
        OutputField = (TextEdit) GetNode("Columns/IO Column/Output Field/TextEdit");
        PeerList =  (VBoxContainer) GetNode("Columns/Information Column/PeerList Panel/Scroll Container/PeerList");
        IDLabel = (Label) GetNode("Columns/Information Column/ID Panel/ID Data/ID String");
        networking = (Networking) GetNode(networkingPath);
    }
    public void OnPeerAdded(SignaledPeer peer)
    {
        GD.Print("OnPeerAdded Called");
        PackedScene scene = GD.Load<PackedScene>("res://addons/PurePeerSignaling/PeerItem.tscn");
        PeerItem peerItem = (PeerItem) scene.Instance();
        PeerList.AddChild(peerItem);
        peerItem.Init(peer, this);
        if(peer.CurrentState == SignaledPeer.ConnectionStateMachine.MANUAL)
            SelectItem(peerItem);
    }

    public void SelectItem(PeerItem item)
    {

        SelectedItem?.peer.Disconnect(nameof(SignaledPeer.BufferedOfferUpdated),this,nameof(UpdateOutput));
        SelectedItem = item;
        SelectedItem.peer.Connect(nameof(SignaledPeer.BufferedOfferUpdated),this, nameof(UpdateOutput));
        if(SelectedItem.peer.BufferedOffer != null)
            UpdateOutput(SelectedItem.peer.BufferedOffer);
    }
    public void UpdateOutput(SignaledPeer.Offer offer)
    {
        GD.Print(offer.assignedUID);
        var offerPacket = MessagePackSerializer.Serialize(offer);
        
        OutputField.Text = MessagePackSerializer.ConvertToJson(offerPacket);
        //OutputField.Text = System.Text.Encoding.ASCII.GetString(offerPacket);
    }

    public void OnJoinMeshButton()
    {
        var packet = MessagePackSerializer.ConvertFromJson(InputField.Text);
        networking.JoinMesh(packet);
    }

    public void OnAddPeerButton()
    {
        networking.ManualAddPeer();
    }

    //Only gets pressed if you're entering the response of a peer you've added.
    public void OnSubmitButton()
    {
        if(SelectedItem == null)
            return;
        var packet = MessagePackSerializer.ConvertFromJson(InputField.Text);
        var offer = MessagePackSerializer.Deserialize<SignaledPeer.Offer>(packet);   
        SelectedItem.peer.SetRemoteDescription(offer.type, offer.sdp);
        foreach(SignaledPeer.BufferedCandidate c in offer.ICECandidates)
            SelectedItem.peer.BufferIceCandidate(c.media, c.index, c.name);
    }

    public void OnMeshJoined(int uid)
    {
        IDLabel.Text = uid.ToString("X4");
    }
}
