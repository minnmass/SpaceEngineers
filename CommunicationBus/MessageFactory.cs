using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using Utilities;

namespace IngameScript {
	public static class MessageFactory {
		// broadcast_all tag1,tag2, payload with spaces
		// unicast_123456 tag1,tag2 payload with spaces
		public static List<MessageQueue.Message<string>> GetMessages(string input) {
			try {
				var messages = new List<MessageQueue.Message<string>>();

				var tagStart = input.IndexOf(' ') + 1;
				var tagStop = input.IndexOf(' ', tagStart);
				var tags = input.Substring(tagStart, tagStop - tagStart).Split(',');

				var payload = input.Substring(tagStop + 1);

				var type = input.Substring(0, tagStart - 1);

				if (type.StartsWith("unicast")) {
					var typeChunks = type.Split('_');
					var target = long.Parse(typeChunks[1]);
					foreach (var tag in tags) {
						messages.Add(new MessageQueue.UnicastMessage<string>(payload, tag, target));
					}
					return messages;
				} else if (!type.StartsWith("broadcast")) {
					throw new Exception("Invalid type.");
				}

				TransmissionDistance distance = TransmissionDistance.AntennaRelay;
				if (type.EndsWith("_current")) {
					distance = TransmissionDistance.CurrentConstruct;
				} else if (type.EndsWith("_connected")) {
					distance = TransmissionDistance.ConnectedConstructs;
				}

				foreach (var tag in tags) {
					messages.Add(new MessageQueue.BroadcastMessage<string>(payload, tag, distance));
				}

				return messages;
			} catch (Exception ex) {
				throw new Exception($"Invalid input: {input}", ex);
			}
		}
	}
}
