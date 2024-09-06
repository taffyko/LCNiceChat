All features work client-side unless otherwise mentioned.

- Can now **scroll** through chat history (once there's enough history, just scroll with mousewheel while the chat input is active!)
	- Chat history is no longer limited to 4 messages
	- A scrollbar shows your position in history (can be disabled in config)
- **Dead, spectating players** can now chat with one another.
	- NOTE: For this, the mod must be installed on the host.
	- Living players cannot hear dead players, unless the host enables it.
	- The names of dead players are color-coded grey.
- Increases the 30 character message limit (default: 1000, configurable by host)
	- NOTE: For this, the mod also needs to be installed by the host.  
	Without it, the limit can only be increased to ~50.
- Increases the size of the chat area (toggleable)
	- Changes the default font size so that more text can fit (configurable)
- Removes the apostrophes (`'`) around chat messages.
- Can now use `Shift+Enter` to insert line breaks in your messages.
- Chat fades away completely when not in use.
	- You can fully configure how many seconds the chat remains visible before fading, and how much the chat fades.
	- Compatible with mods like LethalLoudnessMeter that add UI elements to the bottom-left corner.
- Messages you receive while the clock is visible will now have timestamps (can be disabled in config)
- You can now say the same thing multiple times in a row
	- NOTE: Only players with the mod installed will be able to see repeat messages
- Can customize the range at which players can hear eachother (configurable by host)
	- NOTE: All players must have the mod for this feature to work properly
- Changes the chat input text to a more readable color (configurable)
- When both you and the sender of a message are holding an active walkie-talkie, the name is color-coded green.
	- This is to clarify a vanilla feature where any players holding active walkies can hear each other's messages at any distance.

Chat history scroll demo:  
![Animated GIF demonstrating scrolling](https://i.postimg.cc/BbQcRHVm/lc-chat-scroll-demo-3.gif)