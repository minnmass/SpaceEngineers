using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using VRage.Game.ModAPI.Ingame.Utilities;

namespace IngameScript {
	partial class Program : MyGridProgram {
		#region mdk preserve
		// config here
		// end config
		#endregion

		private const string legalCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_";
		private readonly MyIni config = new MyIni();

		private string CommsChannel;
		private static readonly MyIniKey CommsChannelKey = new MyIniKey("Config", "CommsChannel");
		private IReadOnlyCollection<string> CommsChannels;

		private readonly Logger logger;
		private readonly MessageQueue messageQueue;

		public Program() {
			logger = new Logger(Me);
			logger.Log($"My address is: {Me.EntityId}", append: false);
			logger.Log(DateTime.UtcNow.ToString());

			if (!ParseCustomData()) {
				Save();
				Echo("Error initializing. Bailing.");
				return;
			}

			messageQueue = new MessageQueue(IGC, true);
			foreach (var channel in CommsChannels) {
				logger.Log($"registering {channel}");
				messageQueue.RegisterBroadcastProvider(channel);
			}
		}

		public void Save() {
			config.Set(CommsChannelKey, CommsChannel);
			Me.CustomData = config.ToString();
		}

		public void Main(string argument, UpdateType updateSource) {
			if ((updateSource & UpdateType.IGC) == 0) {
				if (String.IsNullOrWhiteSpace(argument)) {
					logger.Log("Attempted to run with an empty argument.");
					return;
				}
				logger.Log(argument);
				foreach (var message in MessageFactory.GetMessages(argument)) {
					logger.Log(message.ToString());
					messageQueue.SendMessage(message);
				}
			}

			logger.Log("Running.");

			foreach (var message in messageQueue.GetMessages()) {
				logger.Log(message.IsUnicast ? "Unicast" : "Broadcast");
				logger.Log(message.Message.Data as string);
			}
		}

		private bool ParseCustomData() {
			MyIniParseResult ini;
			if (!config.TryParse(Me.CustomData, out ini)) {
				logger.Log($"Error parsing custom data: {ini.Error}");
				return false;
			}

			if (!config.Get(CommsChannelKey).TryGetString(out CommsChannel)) {
				logger.Log("Error parsing comms channel.");
				CommsChannel = "SetCommsChannelNamesHere";
				return false;
			}

			CommsChannels = CommsChannel.Split(',');
			if (CommsChannels.Any(channel => channel.Any(c => !legalCharacters.Contains(c)))) {
				logger.Log("Illegal character in comms channel name. Must be alphanumeric, dash, or underscore.");
				logger.Log(legalCharacters);
				CommsChannel = "SetCommsChannelNamesHere";
				return false;
			}
			return true;
		}
	}
}
