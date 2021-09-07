This is a server side Vintage Story mod that provides a Discord bridge. Features:
* Players join and leave messages are sent to Discord.
* Players death messages are sent to Discord.
* Messages from discord are sent to the game.
* Server startup and shutdown messages are sent to Discord.
* Discord messages from specific users can be ignored (useful for bots).
* Configurable log scraping to Discord. By default this is used for temporal storm messages.
* In game player groups can be configured to connect to different Discord channels.
* Individual features can be enabled or disabled through the config file.

# Installation

1. Put the `discordbot.zip` file in the `Mods` folder on the server-side only.
2. Start the server to create a default `ModConfig/discordbot.json` file in your VS config folder. Note that until the correct login information is added to the config file, you will see errors in the server log about 'The server sent close 4004: "Authentication failed.'"
3. [Create a discord application](https://discord.com/developers/applications/) for your server. The application name will become the bot's username.
4. Click on the Bot tab of the application, then click Add Bot.
5. On the Bot tab, copy the token (you can use the Copy button). Paste that token between the quotes in the `DiscordToken` field in `discordbot.json`. Note that the client secret on the OAuth2 tab is different from the token and cannot be used as a substitue for the token.
6. Give the bot permissions to interact with your server on the OAuth2 tab. First click the bot checkbox under scopes, then check Send Messages below.
7. Go to the generated URL shown at the end of the scopes section. Select the server to add your bot to.
8. In Discord, right click the channel the Bot should listen to, and select Copy ID in the context menu. Paste that channel into the `DefaultChannel/DiscordChannel` field of `discordbot.json`. The channel id should not be surrounded by quotes.
9. Restart the server for the mod settings to take effect.

# Per group channels

The text from specifc in game groups can be redirected to their own discord channel. In-game groups are connected to the default channel, unless it has a channel override. This is configured through the `ChannelOverrides` section in `discordbot.json`. The default config has a sample no-op entry that you may edit or delete. The name of the group in-game is the key for the entries in the dict. `DiscordChannel` is the id of the Discord channel to connect the in-game group to.

Server start/stop, death messages, log scrapes, etc. are only sent to the default channel.

# Building

This project includes Discord.Net through a git submodule. If the Discord.Net directory appears empty, run this command to populate it:
```
$ git submodule update --init --recursive
```

For debugging purposes, the project can be run from VSCode, on Linux or Windows. Follow [Copygirl's guide](https://github.com/copygirl/howto-example-mod/) on how to setup VSCode, then load the project. Press F5 to run it in the debugger.

If OmniSharp is not working (provides things like "Go to Definition") in VSCode, then hit F1, type "OmniSharp: Select Project", hit enter, then make sure discordbot.csproj has a checkmark in the drop dwon.

To build a release zip, run the following command from the terminal. If successful, `discordbot/bin/discordbot.zip` will be created.
```
$ dotnet build -c Release discordbot/
```
