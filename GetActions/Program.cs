using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using System.Collections.Generic;
using Utilities;

namespace IngameScript {
	partial class Program : MyGridProgram {
		private readonly Logger logger;

		private readonly List<ITerminalAction> actions = new List<ITerminalAction>();

		public Program() {
			logger = new Logger(Me);
			Runtime.UpdateFrequency = UpdateFrequency.None;
		}

		public void Main(string argument, UpdateType updateSource) {
			var block = GridTerminalSystem.GetBlockWithName(argument);
			if (block == null) {
				logger.Log("No block found.", append: false);
				return;
			}

			block.GetActions(actions);
			logger.Log("Found actions:", append: false);
			foreach (var action in actions) {
				logger.Log(action.Name.ToString());
			}
			actions.Clear();
		}
	}
}
