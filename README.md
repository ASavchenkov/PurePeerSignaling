# PurePeerSignaling

## Adding to Your Project

1. You will need [The WebRTC gdnative plugin](https://github.com/godotengine/webrtc-native).
2. 'git submodule add' this repository to your addons folder.
3. This library uses dynamic types and the MessagePack library. This requires editing your projects csproj file. ExampleCSProject.csproj has comments indicating which lines to copy.


## Example Project Usage

### Inviting a peer.

1) Click "Add Peer";
2) Copy the text in the output field, and send to desired user via messaging app.
3) (Continued after instructions to join for your counterpart) ...

### Joining a Session

1) Copy the text sent earlier into the input field.
2) Click "Join Mesh"
3) Copy the text in the output field and send to the person inviting you.

### Inviting a peer (cont.)

3) Copy the text sent by your invitee into the input field
4) Click "Submit"

### Working End State

The peer should be listed with a hexadecimal ID, and it's status to the left of it should read "NOMINAL".
