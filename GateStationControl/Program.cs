using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using Utilities;
using VRage;

namespace IngameScript {
	partial class Program : MyGridProgram {
		#region mdk preserve
		private const string DHDName = "GateStation Output DHD";
		private const string EjectorName = "GateStation Ejector";
		private const string StatusLCD = "Stargate";
		private const string StargateName = "Stargate";
		private const float MaxFillPercentage = 0.9f;
		private const bool CloseIrisAfterDraining = true;
		#endregion

		private readonly IMyTerminalBlock _dhd;
		private readonly IMyTextPanel _statusLcd;
		private readonly IMyShipConnector _ejector;
		private readonly IMyTerminalBlock _gate;
		private readonly Logger _logger;

		private Status _status;

		public Program() {
			_logger = new Logger(Me);
			_logger.Log("Starting...", append: false);

			_dhd = GridTerminalSystem.GetBlockWithName(DHDName);
			var textPanelBlocks = new List<IMyTextPanel>();
			GridTerminalSystem.GetBlocksOfType(textPanelBlocks);
			_statusLcd = textPanelBlocks.Find(p => p.DisplayNameText.Contains(StatusLCD));
			_ejector = GridTerminalSystem.GetBlockWithName(EjectorName) as IMyShipConnector;

			var allBlocks = new List<IMyTerminalBlock>();
			GridTerminalSystem.GetBlocksOfType(allBlocks);
			_gate = allBlocks.FirstOrDefault(b => b.BlockDefinition.SubtypeId.StartsWith("Stargate "));

			bool success = true;
			if (_dhd == null) {
				_logger.Log("Could not find DHD.");
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
			if (_gate == null) {
				_logger.Log("Could not find gate.");
				success = false;
			}
			if (success) {
				Runtime.UpdateFrequency = UpdateFrequency.Update100;
				_status = Status.Filling;
				_ejector.CollectAll = true;
				_logger.Log("... ready.");
			} else {
				_logger.Log("Recompile this block to re-scan.");
			}
		}

		private enum Status {
			Filling,
			Connecting,
			Draining,
			WaitingToCloseGate
		}

		private const string _dialAction = "Phoenix.Stargate.QuickDial";
		private const string _irisOpenAction = "Phoenix.Stargate.Iris_Off";
		private const string _irisCloseAction = "Phoenix.Stargate.Iris_On";

		public void Main(string argument, UpdateType updateSource) {
			_logger.Log(_status.ToString(), append: false);
			_logger.Log(DateTime.Now.ToString());
			switch (_status) {
				case Status.Filling:
					_logger.Log("Filling...");
					_status = Filling();
					break;
				case Status.Connecting:
					_logger.Log("Connecting...");
					_status = Connecting();
					break;
				case Status.Draining:
					_logger.Log("Draining...");
					_status = Draining();
					break;
				case Status.WaitingToCloseGate:
					_logger.Log("Waiting to close gate...");
					_status = WaitToCloseGate();
					break;
			}
			_logger.Log(_status.ToString());
		}

		private Status Draining() {
			var stats = GetFillStats();

			var closeEnough = MyFixedPoint.MultiplySafe(stats.Max, 0.01f);
			if (stats.Current <= closeEnough) {
				_ejector.ThrowOut = false;
				return Status.WaitingToCloseGate;
			} else {
				return Status.Draining;
			}
		}

		private static DateTime? CloseGateAt = null;
		private Status WaitToCloseGate() {
			if (CloseGateAt == null) {
				CloseGateAt = DateTime.UtcNow.AddSeconds(30);
				return Status.WaitingToCloseGate;
			}
			if (DateTime.UtcNow <= CloseGateAt) {
				return Status.WaitingToCloseGate;
			}
			_dhd.ApplyAction(_dialAction);
			if (CloseIrisAfterDraining) {
				_gate.ApplyAction(_irisCloseAction);
			}
			return Status.Filling;
		}

		private Status Connecting() {
			switch (_statusLcd.CurrentlyShownImage) {
				case "Stargate SG1 Idle":
				case "Stargate SG1 Idle Iris":
					_logger.Log("Dialing.");
					_dhd.ApplyAction(_dialAction);
					return Status.Connecting;
				case "Stargate SG1 Incoming Iris":
				case "Stargate SG1 Incoming":
				case "Stargate SG1 Dialing":
					_logger.Log("Waiting.");
					// nothing to do but wait
					return Status.Connecting;
				case "Stargate SG1 Outgoing Iris":
					_logger.Log("Iris.");
					_gate.ApplyAction(_irisOpenAction);
					return Status.Connecting;
				case "Stargate SG1 Outgoing":
					_logger.Log("Ejecting.");
					_ejector.ThrowOut = true;
					return Status.Draining;
			}
			var message = $"Unexpected status: \"{_statusLcd.CurrentlyShownImage}\".";
			_logger.Log(message);
			throw new System.Exception(message);
		}

		private Status Filling() {
			_logger.Log("... in Filling() ...");
			var stats = GetFillStats();
			_logger.Log("Got some stats.");

			var maxFill = MyFixedPoint.MultiplySafe(stats.Max, MaxFillPercentage);
			return stats.Current >= maxFill ? Status.Connecting : Status.Filling;
		}

		private struct FillStats {
			public MyFixedPoint Max;
			public MyFixedPoint Current;
		}

		private readonly List<IMyCargoContainer> containers = new List<IMyCargoContainer>();
		private FillStats GetFillStats() {
			_logger.Log("GetFillStats()");
			containers.Clear();
			GridTerminalSystem.GetBlocksOfType(containers, c => c.CubeGrid.EntityId == Me.CubeGrid.EntityId);
			_logger.Log($"Found {containers.Count} containers.");
			var max = new MyFixedPoint();
			var current = new MyFixedPoint();

			foreach (var container in containers) {
				for (int i = 0; i < container.InventoryCount; ++i) {
					_logger.Log("Adding an inventory.");
					var inventory = container.GetInventory(i);
					max = MyFixedPoint.AddSafe(max, inventory.MaxVolume);
					current = MyFixedPoint.AddSafe(current, inventory.CurrentVolume);
				}
			}

			_logger.Log($"Max: {max}");
			_logger.Log($"Current: {current}");

			return new FillStats {
				Max = max,
				Current = current,
			};
		}
	}
}
