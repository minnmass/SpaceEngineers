using Sandbox.ModAPI.Ingame;
using System.Collections.Generic;
using Utilities;
using VRage;

namespace IngameScript {
	partial class Program : MyGridProgram {
		#region mdk preserve
		private const string DHDName = "GateStation Output DHD";
		private const string EjectorName = "GateStation Ejector";
		private const string StatusLCD = "Stargate";
		private const float MaxFillPercentage = 0.9f;
		private const bool CloseIrisAfterDraining = true;
		#endregion

		private readonly IMyTerminalBlock _dhd;
		private readonly IMyTextPanel _statusLcd;
		private readonly IMyShipConnector _ejector;
		private readonly Logger _logger;

		private Status _status;

		public Program() {
			_logger = new Logger(Me);

			_dhd = GridTerminalSystem.GetBlockWithName(DHDName);
			var textPanelBlocks = new List<IMyTextPanel>();
			GridTerminalSystem.GetBlocksOfType(textPanelBlocks);
			_statusLcd = textPanelBlocks.Find(p => p.DisplayNameText.ToUpperInvariant().Contains(StatusLCD));
			_ejector = GridTerminalSystem.GetBlockGroupWithName(EjectorName) as IMyShipConnector;

			bool success = true;
			if (_dhd == null) {
				_logger.Log("Could not find DHD.", append: false);
				success = false;
			}
			if (_statusLcd == null) {
				_logger.Log("Could not find status LCD.");
				success = false;
			}
			if (_ejector == null) {
				_logger.Log("Could not find ejector.");
				success = false;
			}
			if (success) {
				Runtime.UpdateFrequency = UpdateFrequency.Update100;
				_status = Status.Filling;
				_ejector.CollectAll = true;
			} else {
				_logger.Log("Recompile this block to re-scan.");
			}
		}

		private enum Status {
			Filling,
			Connecting,
			Draining
		}

		private const string _dialAction = "Phoenix.Stargate.QuickDial";
		private const string _irisOpenAction = "Phoenix.Stargate.Iris_Off";
		private const string _irisCloseAction = "Phoenix.Stargate.Iris_On";

		public void Main(string argument, UpdateType updateSource) {
			switch (_status) {
				case Status.Filling:
					_status = Filling();
					return;
				case Status.Connecting:
					_status = Connecting();
					return;
				case Status.Draining:
					_status = Draining();
					return;
			}
		}

		private Status Draining() {
			var stats = GetFillStats();

			MyFixedPoint.MultiplySafe(stats.Max, 0.01f);
			if (stats.Current <= stats.Max) {
				if (CloseIrisAfterDraining) {
					_dhd.ApplyAction(_irisCloseAction);
				}
				_ejector.ThrowOut = false;
				return Status.Filling;
			} else {
				return Status.Draining;
			}
		}

		private Status Connecting() {
			switch (_statusLcd.CurrentlyShownImage) {
				case "Stargate SG1 Idle":
				case "Stargate SG1 Idle Iris":
					_dhd.ApplyAction(_dialAction);
					return Status.Connecting;
				case "Stargate SG1 Incoming Iris":
				case "Stargate SG1 Incoming":
				case "Stargate SG1 Dialing":
					// nothing to do but wait
					return Status.Connecting;
				case "Stargate SG1 Outgoing Iris":
					_dhd.ApplyAction(_irisOpenAction);
					return Status.Connecting;
				case "Stargate SG1 Outgoing":
					_ejector.ThrowOut = true;
					return Status.Draining;
			}
			var message = $"Unexpected status: \"{_statusLcd.CurrentlyShownImage}\".";
			_logger.Log(message);
			throw new System.Exception(message);
		}

		private Status Filling() {
			var stats = GetFillStats();

			MyFixedPoint.MultiplySafe(stats.Max, MaxFillPercentage);
			return stats.Current >= stats.Max ? Status.Connecting : Status.Filling;
		}

		private struct FillStats {
			public MyFixedPoint Max;
			public MyFixedPoint Current;
		}

		private readonly List<IMyCargoContainer> containers;
		private FillStats GetFillStats() {
			GridTerminalSystem.GetBlocksOfType(containers, c => c.CubeGrid.EntityId == Me.CubeGrid.EntityId);
			var max = MyFixedPoint.Zero;
			var current = MyFixedPoint.Zero;

			foreach (var container in containers) {
				for (int i = 0; i < container.InventoryCount; ++i) {
					var inventory = container.GetInventory(i);
					MyFixedPoint.AddSafe(max, inventory.MaxVolume);
					MyFixedPoint.AddSafe(current, inventory.CurrentVolume);
				}
			}

			return new FillStats {
				Max = max,
				Current = current,
			};
		}
	}
}
