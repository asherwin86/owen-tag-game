# Tag Game server

Run your own game server (needs Node.js 18+):

- Windows: double-click `start-server.bat`
- Mac/Linux: `./start-server.sh`

It prints the address to type into the game's Multiplayer screen (choose "My own server").
`ws://localhost:8080` works on the same computer; friends on your network use the `ws://192.168...` address it prints.
Note: browsers only allow plain `ws://` addresses (other than localhost) when the game page itself is not https, so for friends on Wi-Fi use the Windows/Mac build or open the game over http.
