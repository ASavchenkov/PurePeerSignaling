# PurePeerSignaling

## Installation

1. If you're not just building for the web, you need [The WebRTC gdnative plugin](https://github.com/godotengine/webrtc-native).
2. Clone this repository into your project.
3. This library uses dynamic types and the MessagePack library. This requires editing your projects csproj file.
	a.  Copy '<Reference Include="Microsoft.CSharp" />'
	b.  And
	'''
	<ItemGroup>
    	<PackageReference Include="MessagePack">
      		<Version>2.1.115</Version>
    	</PackageReference>
  	</ItemGroup>
  	'''
	As they are in PurePeerSignaling.csproj
