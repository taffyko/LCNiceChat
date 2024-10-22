# 1.2.7
Max message history is now configurable and defaults to 15 for performance reasons

# 1.2.6
(Targeting v62/v64)
- The range at which players can hear each other is now customizable by the host.
- **Dead, spectating players** can now chat with one another.
	- Living players cannot hear dead players, unless the host enables it.
	- The names of dead players are color-coded grey.
- When both you and the sender of a message are holding an active walkie-talkie, the name is color-coded green.
	- This is to clarify a vanilla feature where any players holding active walkies can hear each other's messages at any distance.

# 1.2.5
- Fixes an issue where the character limit would not increase for clients when playing with TooManyEmotes
- Chat now fully fades away by default
- Adds a workaround to improve compatibility with mods like LethalLoudnessMeter that add HUD elements in the bottom-left corner,
  preventing their HUD elements from fading when the chat fades.
- The color of the text in the input field can now be configured, and defaults to white for better visibility

# 1.2.4
- Adds a workaround for an incompatibility with MirrorDecor

# 1.2.3
- Scrollbar now only appears while the chat input is focused
- Scrollbar can now be hidden completely in config ([#4](https://github.com/taffyko/LCNiceChat/issues/4))
- Can now configure the fade-out opacity of the chat UI & time before fading out

# 1.2.2
- Bug-fixes, including an issue related to MoreCompany cosmetics ([#2](https://github.com/taffyko/LCNiceChat/issues/2))

# 1.2.1
- Fixes some bugs when playing with MoreCompany

# 1.2.0
- A scrollbar indicator has been added showing your position in history
- Messages you receive while the clock is visible will now have timestamps (can be disabled in config)
- Fixes some bugs related to keeping your place in history & seeing the latest messages

# 1.1.0
- Message history is now scrollable
- Character limit can now be configured by the host. (defaults to 1000)

# 1.0.0
Released for v45