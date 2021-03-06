using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Rem.Attributes;
using Rem.Extensions;
using Rem.Services;
using Rem.Utilities;
using SQLite;

namespace Rem.Bot
{
    public class DiscordBot
    {
        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly BotState _state;
        private readonly SQLiteAsyncConnection _dbContext;
        private readonly TaskCompletionSource<Task> _completionSource;
        private IServiceProvider _services;

        public DiscordBot(string version, string configPath, string dbPath)
        {
            _state = BotState.Initialize(version, configPath);
            _dbContext = new SQLiteAsyncConnection(dbPath);

            _completionSource = new TaskCompletionSource<Task>();
            _client = new DiscordSocketClient();
            _client.Log += Log;
            _client.Ready += () =>
            {
                _completionSource.SetResult(Task.CompletedTask);
                Log($"Running as user {_client.CurrentUser.Username} ({_client.CurrentUser.Id})");
                return Task.CompletedTask;
            };

            _commands = new CommandService();
            _services = null;
        }

        private static Task Log(LogMessage msg)
        {
            Console.WriteLine(msg.ToString());
            return Task.CompletedTask;
        }

        private static Task Log(string msg)
        {
            Console.WriteLine(msg);
            return Task.CompletedTask;
        }

        private async Task InstallServices()
        {
            await _dbContext.CreateTableAsync<Models.Image>();

            var types = Assembly.GetEntryAssembly().FindTypesWithAttribute<ServiceAttribute>().ToImmutableArray();

            _services = new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(_commands)
                .AddSingleton(_dbContext)
                .AddSingleton(_state)
                .AddSingleton(MemeLoader.LoadAllTemplates())
                .AddServices(types)
                .BuildServiceProvider();
        }

        private async Task InstallCommands()
        {
            _client.MessageReceived += MessageReceived;
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly());
        }

        private async Task MessageReceived(SocketMessage messageParam)
        {
            // Don't process the command if it was a System Message
            var message = messageParam as SocketUserMessage;
            if (message == null) return;
            // Create a number to track where the prefix ends and the command begins
            var argPos = 0;
            // Determine if the message is a command, based on if it starts with '!' or a mention prefix
            if (!(message.HasStringPrefix(_state.Prefix, ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos))) return;
            // Create a Command Context
            var context = new CommandContext(_client, message);
            // Execute the command. (result does not indicate a return value, 
            // rather an object stating if the command executed successfully)
            var result = await _commands.ExecuteAsync(context, argPos, _services);
            /*
            if (!result.IsSuccess)
                await context.Channel.SendMessageAsync(result.ErrorReason);
            */
            if (!result.IsSuccess)
            {
                switch (result.Error)
                {
                    case CommandError.Exception:
                        if (result is ExecuteResult execResult)
                        {
                            //Debugger.Break();
                            await Console.Error.WriteLineAsync($"Error encountered when handling command {message}:");
                            await Console.Error.WriteLineAsync(execResult.Exception.ToString());
                        }

                        break;
                    case CommandError.UnknownCommand:
                        break;
                    case CommandError.ParseFailed:
                        break;
                    case CommandError.BadArgCount:
                        break;
                    case CommandError.ObjectNotFound:
                        break;
                    case CommandError.MultipleMatches:
                        break;
                    case CommandError.UnmetPrecondition:
                        break;
                    case CommandError.Unsuccessful:
                        break;
                    default:
                        break;
                }
            }
            
            await _state.PersistState();
        }

        private async Task UpdateDiscordStatus()
        {
            await _client.SetGameAsync($"{_state.Version}");
            // await _client.SetGameAsync($"{_state.PatCount} pats given, {_state.BribeCount} mods bribed");
        }

        public async Task Start()
        {
            await InstallServices();
            await InstallCommands();
            await _client.LoginAsync(TokenType.Bot, _state.ClientSecret);
            await _client.StartAsync();

            await _completionSource.Task;
            _services.RunInitMethods();

            await UpdateDiscordStatus();
        }
    }
}
