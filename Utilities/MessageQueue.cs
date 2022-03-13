using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;

namespace Utilities {
	public class MessageQueue {
		private readonly IMyIntergridCommunicationSystem IGC;

		private readonly List<IMyMessageProvider> messageProviders = new List<IMyMessageProvider>();
		private bool unicastRegistered = false;

		public MessageQueue(IMyIntergridCommunicationSystem igc, bool autoRegisterUnicast = true) {
			IGC = igc;
			if (autoRegisterUnicast) {
				RegisterUnicastProvider();
			}
		}

		public void RegisterBroadcastProvider(string channel, bool registerCallback = true) {
			var listener = IGC.RegisterBroadcastListener(channel);
			if (registerCallback) {
				listener.SetMessageCallback(channel);
			}
			messageProviders.Add(listener);
		}

		public void RegisterUnicastProvider() {
			if (unicastRegistered) {
				return;
			}
			unicastRegistered = true;
			IGC.UnicastListener.SetMessageCallback("UNICAST");
		}

		public IEnumerable<IGCMessage> GetMessages() {
			if (unicastRegistered) {
				while (IGC.UnicastListener.HasPendingMessage) {
					yield return new IGCMessage(true, IGC.UnicastListener.AcceptMessage());
				}
			}
			foreach (var provider in messageProviders) {
				while (provider.HasPendingMessage) {
					yield return new IGCMessage(false, provider.AcceptMessage());
				}
			}
		}

		public void SendMessage<T>(BroadcastMessage<T> message) {
			IGC.SendBroadcastMessage(message.Tag, message.Payload, message.TransmissionDistance);
		}

		public void SendMessage<T>(UnicastMessage<T> message) {
			IGC.SendUnicastMessage(message.Target, message.Tag, message.Payload);
		}

		public void SendMessage<T>(Message<T> message) {
			if (message == null) {
				return;
			}
			var broadcast = message as BroadcastMessage<T>;
			if (broadcast != null) {
				SendMessage(broadcast);
				return;
			}
			var unicast = message as UnicastMessage<T>;
			if (unicast != null) {
				SendMessage(unicast);
				return;
			}
			throw new System.Exception("Could not determine message type.");
		}

		public class IGCMessage {
			public bool IsUnicast { get; }
			public MyIGCMessage Message { get; }

			internal IGCMessage(bool unicast, MyIGCMessage message) {
				IsUnicast = unicast;
				Message = message;
			}
		}

		public abstract class Message<T> {
			public T Payload { get; set; }
			public string Tag { get; set; }

			public Message(T payload, string tag) {
				Payload = payload;
				Tag = tag;
			}

			public override string ToString() {
				return "base class; don't use";
			}
		}

		public class BroadcastMessage<T> : Message<T> {
			public TransmissionDistance TransmissionDistance { get; set; }

			public BroadcastMessage(T payload, string tag, TransmissionDistance distance = TransmissionDistance.AntennaRelay) : base(payload, tag) {
				TransmissionDistance = distance;
			}

			public override string ToString() {
				return $"broadcast - {Tag} - {Payload} - {TransmissionDistance}";
			}
		}

		public class UnicastMessage<T> : Message<T> {
			public long Target { get; set; }

			public UnicastMessage(T payload, string tag, long target) : base(payload, tag) {
				Target = target;
			}

			public override string ToString() {
				return $"unicast - {Tag} - {Payload} - {Target}";
			}
		}
	}
}
