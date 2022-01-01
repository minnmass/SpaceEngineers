using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Utilities;

namespace IngameScript {
	partial class Program : MyGridProgram {
		#region mdk preserve
		private const string GroupToToggle = "Toggle On Dock";
		private const string GroupToDisable = "Disable On Dock";
		private const string GroupToEnable = "Enable On Undock";
		private const string DockingConnectorKey = "[AutoToggle]";
		private const string ReinitializeArgument = "initialize";
		private const bool AutoScan = true;
		#endregion

		private readonly List<IMyFunctionalBlock> blocksToEnable = new List<IMyFunctionalBlock>();
		private readonly List<IMyGasTank> tanksToStockpile = new List<IMyGasTank>();
		private readonly List<IMyBatteryBlock> batteriesToRecharge = new List<IMyBatteryBlock>();

		private readonly List<IMyFunctionalBlock> blocksToDisable = new List<IMyFunctionalBlock>();
		private readonly List<IMyGasTank> tanksToUnstockpile = new List<IMyGasTank>();
		private readonly List<IMyBatteryBlock> batteriesToAuto = new List<IMyBatteryBlock>();

		private IMyShipConnector connector;
		private bool wasDockedOnLastRun = false;

		private readonly Logger _logger;

		public Program() {
			_logger = new Logger(Me);
			Initialize();
		}

		public void Main(string argument, UpdateType updateSource) {
			if (
				connector == null ||
				(AutoScan && ((updateSource & UpdateType.Update100) != 0)) || // re-scan every 2-ish minutes if desired
				string.Equals(argument, ReinitializeArgument, StringComparison.OrdinalIgnoreCase) // explicit request
			) {
				_logger.Log("Initializing.");
				Initialize();
			}
			if (connector == null) {
				_logger.Log("Could not find exactly one connector to monitor.");
				return;
			}

			bool docked = connector.Status == MyShipConnectorStatus.Connected;
			if (docked == wasDockedOnLastRun) {
				return;
			}
			wasDockedOnLastRun = docked;

			if (docked) {
				_logger.Log("Docking");
				Dock();
			} else {
				_logger.Log("Undocking");
				Undock();
			}
		}

		private void Dock() {
			foreach (var battery in batteriesToRecharge) {
				battery.ChargeMode = ChargeMode.Recharge;
			}
			foreach (var tank in tanksToStockpile) {
				tank.Stockpile = true;
			}
			foreach (var block in blocksToDisable) {
				block.Enabled = false;
			}
		}

		private void Undock() {
			foreach (var battery in batteriesToAuto) {
				battery.ChargeMode = ChargeMode.Auto;
			}
			foreach (var tank in tanksToUnstockpile) {
				tank.Stockpile = false;
			}
			foreach (var block in blocksToEnable) {
				block.Enabled = true;
			}
		}

		private void Initialize() {
			blocksToEnable.Clear();
			tanksToStockpile.Clear();
			batteriesToRecharge.Clear();
			blocksToDisable.Clear();
			tanksToUnstockpile.Clear();
			batteriesToAuto.Clear();

			GridTerminalSystem.SearchBlocksOfName(DockingConnectorKey, terminalScratch);
			if (terminalScratch.Count == 1 && terminalScratch[0] is IMyShipConnector) {
				connector = terminalScratch[0] as IMyShipConnector;
				wasDockedOnLastRun = connector.Status == MyShipConnectorStatus.Connected;
			} else {
				Echo("Could not find exactly one block to act as monitored connector.");
				_logger.Log("Could not find exactly one block to act as monitored connector.");
				return;
			}

			var toggleGroup = GridTerminalSystem.GetBlockGroupWithName(GroupToToggle);
			var onGroup = GridTerminalSystem.GetBlockGroupWithName(GroupToEnable);
			var offGroup = GridTerminalSystem.GetBlockGroupWithName(GroupToDisable);

			if (toggleGroup != null) {
				toggleGroup.GetBlocksOfType(scratch);
				_logger.Log($"Found {scratch.Count} blocks to toggle.");
				DistributeBlocks(scratch, batteriesToRecharge, tanksToStockpile, blocksToDisable);
				DistributeBlocks(scratch, batteriesToAuto, tanksToUnstockpile, blocksToEnable);
			}
			if (onGroup != null) {
				onGroup.GetBlocksOfType(scratch);
				_logger.Log($"Found {scratch.Count} blocks to turn on.");
				DistributeBlocks(scratch, batteriesToAuto, tanksToUnstockpile, blocksToEnable);
			}
			if (offGroup != null) {
				offGroup.GetBlocksOfType(scratch);
				_logger.Log($"Found {scratch.Count} blocks to turn off.");
				DistributeBlocks(scratch, batteriesToRecharge, tanksToStockpile, blocksToDisable);
			}

			if (
				((Runtime.UpdateFrequency & UpdateFrequency.Update10) != 0) &&
				(
					batteriesToAuto.Count > 0 ||
					batteriesToRecharge.Count > 0 ||
					tanksToStockpile.Count > 0 ||
					tanksToUnstockpile.Count > 0 ||
					blocksToEnable.Count > 0 ||
					blocksToDisable.Count > 0
				)
			) {
				_logger.Log("Setting update frequency to 10.");
				Runtime.UpdateFrequency |= UpdateFrequency.Update10;
			} else {
				_logger.Log("Didn't find any blocks to modify; setting update frequency to 100.");
				Runtime.UpdateFrequency = UpdateFrequency.Update100;
			}

		}

		private static void DistributeBlocks(List<IMyFunctionalBlock> source, List<IMyBatteryBlock> batteries, List<IMyGasTank> tanks, List<IMyFunctionalBlock> remainder) {
			foreach (var block in source) {
				if (block is IMyBatteryBlock) {
					batteries.Add(block as IMyBatteryBlock);
				} else if (block is IMyGasTank) {
					tanks.Add(block as IMyGasTank);
				} else {
					remainder.Add(block);
				}
			}
		}

		// scratch lists for performance
		private readonly List<IMyTerminalBlock> terminalScratch = new List<IMyTerminalBlock>();
		private readonly List<IMyFunctionalBlock> scratch = new List<IMyFunctionalBlock>();
	}
}
