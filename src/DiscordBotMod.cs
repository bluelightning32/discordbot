using Discord;
using Discord.WebSocket;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using System.Collections.Concurrent;
using Action = System.Action;
using System;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Config;
using System.Text.RegularExpressions;

[assembly: ModInfo("DiscordBot", "discordbot",
    Description = "Server side discord chat integration.",
    Version = "1.0.0",
    Authors = new[] { "bluelightning32" },
    Side = "Server"
    )]

namespace DiscordBot
{
    internal class ChannelInfo
    {
        public ChannelInfo(int gameChannel, ChannelOverride config)
        {
            this.gameChannel = gameChannel;
            this.config = config;
        }
        public SocketTextChannel channel = null;

        public int gameChannel;

        public ChannelOverride config;
    }

    public class DiscordBotMod : ModSystem
    {
        ICoreServerAPI api;
        DiscordBotConfig config;
        Dictionary<int, ChannelInfo> gameChannelInfo = new Dictionary<int, ChannelInfo>();
        Dictionary<ulong, ChannelInfo> discordChannelInfo = new Dictionary<ulong, ChannelInfo>();

        DiscordSocketClient client;
        ChannelInfo defaultChannel = null;

        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;
            config = api.LoadModConfig<DiscordBotConfig>("discordbot.json");
            if (config == null)
            {
                config = new DiscordBotConfig();
                api.StoreModConfig<DiscordBotConfig>(config, "discordbot.json");
            }
            defaultChannel = new ChannelInfo(GlobalConstants.GeneralChatGroup, config.DefaultChannel);
            discordChannelInfo.Add(defaultChannel.config.DiscordChannel, defaultChannel);
            foreach (KeyValuePair<string, ChannelOverride> item in config.ChannelOverrides)
            {
                PlayerGroup group = api.Groups.GetPlayerGroupByName(item.Key);
                if (group == null)
                {
                    api.Server.LogWarning("[discordbot] Skipping channel override for unknown group {0}.", item.Key);
                    continue;
                }
                ChannelInfo info = new ChannelInfo(group.Uid, item.Value);
                gameChannelInfo.Add(group.Uid, info);
                discordChannelInfo.Add(info.config.DiscordChannel, info);
            }

            StartDiscord();

            api.Event.PlayerChat += OnPlayerChat;
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;

            api.Event.PlayerDisconnect += OnPlayerDisconnect;

            api.Event.PlayerDeath += OnPlayerDeath;

            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, OnRunGame);

            api.Event.ServerRunPhase(EnumServerRunPhase.Shutdown, OnShutdown);

            api.Server.Logger.EntryAdded += OnLogMessage;
            api.Server.LogNotification("[discordbot] loaded");
        }

        public override void Dispose()
        {
            api.Server.LogNotification("[discordbot] shutting down");

            api.Event.PlayerChat -= OnPlayerChat;
            api.Event.PlayerNowPlaying -= OnPlayerNowPlaying;

            api.Event.PlayerDisconnect -= OnPlayerDisconnect;

            api.Event.PlayerDeath -= OnPlayerDeath;

            api.Server.Logger.EntryAdded -= OnLogMessage;

            base.Dispose();
        }

        async void StartDiscord()
        {
            client = new DiscordSocketClient();

            await client.LoginAsync(TokenType.Bot, config.DiscordToken);

            await client.StartAsync();

            TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();
            client.Ready += () =>
            {
                api.Server.LogNotification("[discordbot] connection ready.");
                ready.SetResult(true);
                return Task.CompletedTask;
            };
            await ready.Task;

            foreach (KeyValuePair<ulong, ChannelInfo> item in discordChannelInfo)
            {
                item.Value.channel = client.GetChannel(item.Key) as SocketTextChannel;
                if (item.Value.channel == null)
                {
                    api.Server.LogWarning("[discordbot] Cannot resolve channel {0}.", item.Key);
                    continue;
                }
            }

            client.MessageReceived += DiscordMessageReceived;
        }

        private Task DiscordMessageReceived(SocketMessage arg)
        {
            ulong channelId = arg.Channel.Id;
            if (!discordChannelInfo.ContainsKey(channelId))
            {
                return Task.CompletedTask;
            }
            ChannelInfo info = discordChannelInfo[channelId];
            if (!info.config.ChatToGame)
            {
                return Task.CompletedTask;
            }
            if (client.CurrentUser.Id == arg.Author.Id)
            {
                return Task.CompletedTask;
            }
            foreach (string user in config.IgnoreDiscordUsers)
            {
                if (arg.Author.Username == user)
                {
                    return Task.CompletedTask;
                }
            }
            api.SendMessageToGroup(info.gameChannel, String.Format("[{0}]: {1}", arg.Author.Username, arg.Content), EnumChatType.Notification);
            return Task.CompletedTask;
        }

        private void OnLogMessage(EnumLogType logType, string message, object[] args)
        {
            if (defaultChannel.channel == null) return;

            if (config.LogScrapeRegexes.Count == 0) return;

            string formatted = string.Format(message, args);
            foreach (KeyValuePair<string, string> item in config.LogScrapeRegexes)
            {
                Match m = Regex.Match(formatted, item.Key);
                if (!m.Success)
                {
                    continue;
                }

                string result = m.Result(item.Value);
                if (result.Length != 0) defaultChannel.channel.SendMessageAsync(result);
                return;
            }
        }

        private void OnShutdown()
        {
            if (defaultChannel.channel == null) return;
            if (config.ServerShutdownMessage.Length == 0) return;

            defaultChannel.channel.SendMessageAsync(config.ServerShutdownMessage);
        }

        private void OnRunGame()
        {
            if (defaultChannel.channel == null) return;
            if (config.ServerStartMessage.Length == 0) return;

            defaultChannel.channel.SendMessageAsync(config.ServerStartMessage);
        }

        private void OnPlayerDeath(IServerPlayer byPlayer, DamageSource damageSource)
        {
            if (defaultChannel.channel == null) return;
            if (!config.PlayerDeathToDiscord) return;

            EnumDamageSource type;
            if (damageSource == null)
            {
                type = EnumDamageSource.Suicide;
            }
            else
            {
                type = damageSource.Source;
            }
            string message;
            switch (type)
            {
                case EnumDamageSource.Block:
                    message = "{0} was killed by a block.";
                    break;
                case EnumDamageSource.Player:
                    message = "{0} was killed by {1}.";
                    break;
                case EnumDamageSource.Entity:
                    message = "{0} was killed by a {1}.";
                    break;
                case EnumDamageSource.Fall:
                    message = "{0} fell too far.";
                    break;
                case EnumDamageSource.Drown:
                    message = "{0} drowned.";
                    break;
                case EnumDamageSource.Explosion:
                    message = "{0} blew up.";
                    break;
                case EnumDamageSource.Suicide:
                    message = "{0} couldn't take it anymore.";
                    break;
                default:
                    message = "{0}'s death is a mystery.";
                    break;
            }
            string sourceEntity = damageSource?.SourceEntity?.GetName() ?? "null";
            defaultChannel.channel.SendMessageAsync(string.Format(message, byPlayer.PlayerName, sourceEntity));
        }

        private void OnPlayerDisconnect(IServerPlayer byPlayer)
        {
            UpdatePresence();

            if (defaultChannel.channel == null) return;
            if (config.PlayerLeaveMessage.Length == 0) return;

            defaultChannel.channel.SendMessageAsync(string.Format(config.PlayerLeaveMessage, byPlayer.PlayerName));
        }

        private void OnPlayerNowPlaying(IServerPlayer byPlayer)
        {
            UpdatePresence();

            if (defaultChannel.channel == null) return;
            if (config.PlayerJoinMessage.Length == 0) return;

            defaultChannel.channel.SendMessageAsync(string.Format(config.PlayerJoinMessage, byPlayer.PlayerName));
        }
        private void UpdatePresence()
        {
            int count = api.World.AllOnlinePlayers.Length;
            string message;
            if (count == 1)
            {
                message = "with 1 player";
            }
            else
            {
                message = string.Format("with {0} players", count);
            }
            client.SetGameAsync(message);
        }

        private void OnPlayerChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data, BoolRef consumed)
        {
            ChannelInfo channel = defaultChannel;
            if (gameChannelInfo.ContainsKey(channelId))
            {
                channel = gameChannelInfo[channelId];
            }
            if (!channel.config.ChatToDiscord) return;

            AllowedMentions allowedMentions;
            if (config.AllowMentions)
            {
                allowedMentions = AllowedMentions.All;
            }
            else
            {
                allowedMentions = AllowedMentions.None;
            }
            string stripped = Regex.Replace(message, @"^(<font.*>.*</font> )?(.*)$", "$2");
            channel.channel.SendMessageAsync(string.Format("**[{0}]** {1}", byPlayer.PlayerName, stripped), allowedMentions: allowedMentions);
        }
    }
}
