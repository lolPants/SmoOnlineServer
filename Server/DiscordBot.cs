﻿using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Shared;

namespace Server;

public class DiscordBot {
    private DiscordClient? DiscordClient;
    private string? Token;
    private Settings.DiscordTable Config => Settings.Instance.Discord;
    private string Prefix => Config.Prefix;
    private readonly Logger Logger = new Logger("Discord");
    private DiscordChannel? LogChannel;
    private bool Reconnecting;

    public DiscordBot() {
        Token = Config.Token;
        Logger.AddLogHandler(Log);
        CommandHandler.RegisterCommand("dscrestart", _ => {
            // this should be async'ed but i'm lazy
            Reconnecting = true;
            Task.Run(Reconnect);
            return "Restarting Discord bot";
        });
        if (Config.Token == null) return;
        Settings.LoadHandler += SettingsLoadHandler;
    }

    private async Task Reconnect() {
        if (DiscordClient != null) // usually null prop works, not here though...`
            await DiscordClient.DisconnectAsync();
        await Run();
    }

    private async void SettingsLoadHandler() {
        try {
            if (DiscordClient == null || Token != Config.Token)
                await Run();
            if (Config.LogChannel != null)
                LogChannel = await (DiscordClient?.GetChannelAsync(ulong.Parse(Config.LogChannel)) ??
                                    throw new NullReferenceException("Discord client not setup yet!"));
        } catch (Exception e) {
            Logger.Error($"Failed to get log channel \"{Config.LogChannel}\"");
            Logger.Error(e);
        }
    }

    private static List<string> SplitMessage(string message, int maxSizePerElem = 2000)
    {
        List<string> result = new List<string>();
        for (int i = 0; i < message.Length; i += maxSizePerElem) 
        {
            result.Add(message.Substring(i, message.Length - i < maxSizePerElem ? message.Length - i : maxSizePerElem));
        }
        return result;
    }

    private async void Log(string source, string level, string text, ConsoleColor _) {
        try {
            if (DiscordClient != null && LogChannel != null) {
                foreach (string mesg in SplitMessage(Logger.PrefixNewLines(text, $"{level} [{source}]"), 1994)) //room for 6 '`'
                    await DiscordClient.SendMessageAsync(LogChannel, $"```{mesg}```");
            }
        } catch (Exception e) {
            // don't log again, it'll just stack overflow the server!
            if (Reconnecting) return; // skip if reconnecting
            await Console.Error.WriteLineAsync("Exception in discord logger");
            await Console.Error.WriteLineAsync(e.ToString());
        }
    }

    public async Task Run() {
        Token = Config.Token;
        DiscordClient?.Dispose();
        if (Config.Token == null) {
            DiscordClient = null;
            return;
        }

        try {
            DiscordClient = new DiscordClient(new DiscordConfiguration {
                Token = Config.Token,
                MinimumLogLevel = LogLevel.None
            });
            await DiscordClient.ConnectAsync(new DiscordActivity("Hide and Seek", ActivityType.Competing));
            SettingsLoadHandler();
            Logger.Info(
                $"Discord bot logged in as {DiscordClient.CurrentUser.Username}#{DiscordClient.CurrentUser.Discriminator}");
            Reconnecting = false;
            string mentionPrefix = $"{DiscordClient.CurrentUser.Mention} ";
            DiscordClient.MessageCreated += async (_, args) => {
                if (args.Author.IsCurrent) return;
                try {
                    DiscordMessage msg = args.Message;
                    string? resp = null;
                    if (string.IsNullOrEmpty(Prefix)) {
                        await msg.Channel.TriggerTypingAsync();
                        resp = string.Join('\n', CommandHandler.GetResult(msg.Content).ReturnStrings);
                    } else if (msg.Content.StartsWith(Prefix)) {
                        await msg.Channel.TriggerTypingAsync();
                        resp = string.Join('\n', CommandHandler.GetResult(msg.Content[Prefix.Length..]).ReturnStrings);
                    } else if (msg.Content.StartsWith(mentionPrefix)) {
                        await msg.Channel.TriggerTypingAsync();
                        resp = string.Join('\n', CommandHandler.GetResult(msg.Content[mentionPrefix.Length..]).ReturnStrings);
                    }
                    if (resp != null)
                    {
                        foreach (string mesg in SplitMessage(resp))
                            await msg.RespondAsync(mesg);
                    }
                } catch (Exception e) {
                    Logger.Error(e);
                }
            };
            DiscordClient.ClientErrored += (_, args) => {
                Logger.Error("Discord client caught an error in handler!");
                Logger.Error(args.Exception);
                return Task.CompletedTask;
            };
            DiscordClient.SocketErrored += (_, args) => {
                Logger.Error("Discord client caught an error on socket!");
                Logger.Error(args.Exception);
                return Task.CompletedTask;
            };
        } catch (Exception e) {
            Logger.Error("Exception occurred in discord runner!");
            Logger.Error(e);
        }
    }
}