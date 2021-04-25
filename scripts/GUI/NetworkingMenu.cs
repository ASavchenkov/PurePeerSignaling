using Godot;
using System;
using MessagePack;

using Serilog;

public class NetworkingMenu : CenterContainer
{
    TextEdit InputField;
    TextEdit OutputField; //This one is read only for the user.
    VBoxContainer PeerList;
    Label IDLabel;

    [Export]
    String networkingPath = "/root/Networking";
    Networking networking;
    PeerItem SelectedItem = null;

    public override void _Ready()
    {
        InputField = (TextEdit) GetNode("Columns/IO Column/Input Field/TextEdit");
        OutputField = (TextEdit) GetNode("Columns/IO Column/Output Field/TextEdit");
        PeerList =  (VBoxContainer) GetNode("Columns/Information Column/PeerList Panel/Scroll Container/PeerList");
        IDLabel = (Label) GetNode("Columns/Information Column/ID Panel/ID Data/ID String");

        networking = (Networking) GetNode(networkingPath);
        networking.Connect(nameof(Networking.ConnectedToSession),this, nameof(OnMeshJoined));
        networking.Connect(nameof(Networking.PeerAdded), this, nameof(OnPeerAdded));
    }
    public void OnPeerAdded(SignaledPeer peer)
    {

        PackedScene scene = GD.Load<PackedScene>("res://addons/PurePeerSignaling/PeerItem.tscn");
        PeerItem peerItem = (PeerItem) scene.Instance();
        PeerList.AddChild(peerItem);
        peerItem.Init(peer, this);
        if(peer.CurrentState == SignaledPeer.ConnectionStateMachine.MANUAL)
        {
            SelectItem(peerItem);
            Log.Information("manual peer selected; {UID}", peer.UID);
        }
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
        
        var offerPacket = MessagePackSerializer.Serialize(offer);
        OutputField.Text = MessagePackSerializer.ConvertToJson(offerPacket);
        Log.Information("Updated output; {UID}, {L}", offer.assignedUID, OutputField.Text.Length);
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
            SelectedItem.peer.BufferIceCandidate(c);
    }

    public void OnMeshJoined(int uid)
    {
        IDLabel.Text = uid.ToString("X4");
    }
}
