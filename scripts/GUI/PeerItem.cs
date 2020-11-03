using Godot;
using System;

public class PeerItem : HBoxContainer
{

    private Label label;
    private Button clickableName;
    public SignaledPeer peer {get; private set;}

    public override void _Ready()
    {
        label = (Label) GetNode("Status/Label");
        clickableName = (Button) GetNode("Clickable Name");
    }
    public void Init(SignaledPeer peer, NetworkingMenu menu)
    {
        this.peer = peer;
        clickableName.Text = peer.UID.ToString("X4");
        peer.Connect(nameof(SignaledPeer.StatusUpdated),this, nameof(OnStatusChanged));
        OnStatusChanged(peer.CurrentState);
        clickableName.Connect("pressed", menu, nameof(NetworkingMenu.SelectItem),
            new Godot.Collections.Array(new object[] {this}));
        peer.Connect(nameof(SignaledPeer.Delete),this,"queue_free");
    }

    public void OnStatusChanged(SignaledPeer.ConnectionStateMachine state)
    {
        label.Text = state.ToString();
    }
}
