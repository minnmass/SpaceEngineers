using Sandbox.ModAPI.Ingame;
using SpaceEngineers.Game.ModAPI.Ingame;
using System.Collections.Generic;

namespace IngameScript {
	partial class Program : MyGridProgram {
		private const string GunsOn = "on";
		private const string GunsOff = "off";
		private const bool ReconfigureWhenTurnedOn = true;
		private const bool ReconfigureWhenTurnedOff = false;

		private readonly List<IMySmallGatlingGun> _smallGatlingGuns = new List<IMySmallGatlingGun>();
		private readonly List<IMySmallMissileLauncher> _smallMissileLaunchers = new List<IMySmallMissileLauncher>();
		private readonly List<IMyLargeConveyorTurretBase> _largeConveyoredGuns = new List<IMyLargeConveyorTurretBase>();
		private readonly List<IMyLargeTurretBase> _largeNonConveyoredGuns = new List<IMyLargeTurretBase>();

		private IEnumerator<bool> _stateMachine;

		public Program() {
			Runtime.UpdateFrequency = UpdateFrequency.None;
		}

		public void Main(string argument, UpdateType updateSource) {
			if ((updateSource & UpdateType.Once) == UpdateType.Once) {
				RunStateMachine();
				return;
			}
			switch (argument) {
				case "":
				case null:
					break;
				case GunsOn:
					_stateMachine = SetAllGunsTo(true, ReconfigureWhenTurnedOn).GetEnumerator();
					break;
				case GunsOff:
					_stateMachine = SetAllGunsTo(false, ReconfigureWhenTurnedOff).GetEnumerator();
					break;
				default:
					Echo("Invalid command.");
					break;
			}
			RunStateMachine();
		}

		public void RunStateMachine() {
			if (_stateMachine != null) {
				if (_stateMachine.MoveNext() && _stateMachine.Current) {
					Runtime.UpdateFrequency |= UpdateFrequency.Once;
				} else {
					_stateMachine.Dispose();
					_stateMachine = null;
				}
			}
		}

		private IEnumerable<bool> SetAllGunsTo(bool targetState, bool configure) {
			GridTerminalSystem.GetBlocksOfType(_largeConveyoredGuns);
			if (configure) {
				ConfigureLargeConveyoredGuns();
			}
			foreach (var gun in _largeConveyoredGuns) {
				gun.Enabled = true;
			}

			yield return true;

			GridTerminalSystem.GetBlocksOfType(_smallGatlingGuns);
			foreach (var gun in _smallGatlingGuns) {
				gun.Enabled = true;
			}

			yield return true;

			GridTerminalSystem.GetBlocksOfType(_smallMissileLaunchers);
			foreach (var gun in _smallMissileLaunchers) {
				gun.Enabled = true;
			}

			yield return true;

			GridTerminalSystem.GetBlocksOfType(_largeNonConveyoredGuns);
			_largeConveyoredGuns.RemoveAll(g => g is IMyLargeConveyorTurretBase);
			if (configure) {
				ConfigureLargeUnconveyoredGuns();
			}
			foreach (var gun in _largeNonConveyoredGuns) {
				gun.Enabled = targetState;
			}

			yield return false;
		}

		private void ConfigureLargeConveyoredGuns() {
			foreach (var gun in _largeConveyoredGuns) {
				gun.TargetCharacters = false;
				gun.TargetLargeGrids = true;
				gun.TargetMeteors = true;
				gun.TargetMissiles = false;
				gun.TargetNeutrals = false;
				gun.TargetSmallGrids = true;
				gun.TargetStations = true;
			}
		}

		private void ConfigureLargeUnconveyoredGuns() {
			foreach (var gun in _largeNonConveyoredGuns) {
				gun.TargetCharacters = false;
				gun.TargetLargeGrids = false;
				gun.TargetMeteors = false;
				gun.TargetMissiles = true;
				gun.TargetNeutrals = false;
				gun.TargetSmallGrids = false;
				gun.TargetStations = false;
			}
		}
	}
}
