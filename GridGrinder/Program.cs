using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRage;
using VRage.Game.GUI.TextPanel;

// state machine inspired by https://github.com/malware-dev/MDK-SE/wiki/Coroutines---Run-operations-over-multiple-ticks

namespace IngameScript {
	partial class Program : MyGridProgram {
		private const string PistonGroupX = "[X]";
		private const string PistonGroupY = "[Y]";
		private const string PistonGroupZ = "[Z]";
		private const float fillMax = 0.9F;

		private const string SensorDetectedCommand = "true";
		private const string SensorStoppedDetectingCommand = "false";
		private const string CalculateGridCargoCommand = "calculate";

		private const float blockWidth = 2.5F;
		private const float pistonVelocity = 0.5F;

		private readonly List<IMyPistonBase> X = new List<IMyPistonBase>();
		private readonly List<IMyPistonBase> Y = new List<IMyPistonBase>();
		private readonly List<IMyPistonBase> Z = new List<IMyPistonBase>();
		private readonly List<IMyCargoContainer> cargoContainers = new List<IMyCargoContainer>();
		private IMyShipGrinder grinder;
		private IMySensorBlock sensor;
		private MyFixedPoint gridCargoCapacity;

		private IEnumerator<bool> _stateMachine;

		private bool stoppedForCargo = false;
		private bool unstoppedFromCargoGrinderStatus = false;

		public Program() {
			Log("Starting", firstLog: true);
			_stateMachine = InitialSetup().Concat(ExtendYToGrind()).GetEnumerator();
			Runtime.UpdateFrequency = UpdateFrequency.Once;
		}

		public void Main(string argument, UpdateType updateSource) {
			if ((updateSource & UpdateType.Terminal) == UpdateType.Terminal) {
				switch (argument) {
					case CalculateGridCargoCommand:
						if (!CalculateGridCargo()) {
							Log("Could not calculate cargo.");
						}
						break;
					default:
						Log("Invalid command.");
						break;
				}
				return;
			}

			if ((updateSource & UpdateType.Once) != UpdateType.Once) {
				var currentCargoUse = MyFixedPoint.Zero;
				foreach (var container in cargoContainers) {
					currentCargoUse = MyFixedPoint.AddSafe(currentCargoUse, container.GetInventory().CurrentVolume);
				}
				if (stoppedForCargo) {
					if (currentCargoUse < gridCargoCapacity) {
						stoppedForCargo = false;
						grinder.Enabled = unstoppedFromCargoGrinderStatus;
						foreach (var piston in Y) {
							piston.Velocity = pistonVelocity;
						}
					}
				} else {
					if (currentCargoUse > gridCargoCapacity) {
						stoppedForCargo = true;
						unstoppedFromCargoGrinderStatus = grinder.Enabled;
						grinder.Enabled = false;
						foreach (var piston in Y) {
							piston.Velocity = 0;
						}
					}
				}
			}

			if ((updateSource & UpdateType.Trigger) == UpdateType.Trigger) {
				bool enablePistons;
				switch (argument) {
					case SensorDetectedCommand:
						enablePistons = false;
						grinder.Enabled = true;
						break;
					case SensorStoppedDetectingCommand:
						enablePistons = true;
						grinder.Enabled = false;
						break;
					default:
						Log("Invalid argument.");
						return;
				}
				foreach (var piston in X.Concat(Y).Concat(Z)) {
					piston.Enabled = enablePistons;
				}
				return;
			}
			if (grinder != null && grinder.Enabled) {
				return;
			}

			if (_stateMachine != null) {
				RunStateMachine();
				return;
			}

			// handle what happens when a direction is fully extended/retracted
			if (Y.All(p => Math.Abs(p.CurrentPosition - p.HighestPosition) < 0.001)) {
				var zToDecrease = Z.FirstOrDefault(p => p.CurrentPosition != p.LowestPosition);
				if (zToDecrease != null) {
					_stateMachine = MaximallyRetractAndPrepareToExtend(Y)
						.Concat(DecreasePiston(zToDecrease, blockWidth))
						.Concat(ExtendYToGrind())
						.GetEnumerator();
				} else {
					var xToIncrease = X.FirstOrDefault(p => p.CurrentPosition != p.HighestPosition);
					if (xToIncrease != null) {
						_stateMachine = MaximallyExtendAndPrepareToRetract(Z)
							.Concat(MaximallyRetractAndPrepareToExtend(Y))
							.Concat(IncreasePiston(xToIncrease, blockWidth))
							.Concat(ExtendYToGrind())
							.GetEnumerator();
					} else {
						_stateMachine = MaximallyRetractAndPrepareToExtend(Y)
							.Concat(Finished())
							.GetEnumerator();
					}
				}
			}

			RunStateMachine();
		}

		public void RunStateMachine() {
			if (_stateMachine != null) {
				if (_stateMachine.MoveNext()) {
					Runtime.UpdateFrequency |= UpdateFrequency.Once;
				} else {
					_stateMachine.Dispose();
					_stateMachine = null;
				}
			}
		}


		public IEnumerable<bool> InitialSetup() {
			GridTerminalSystem.GetBlockGroupWithName(PistonGroupX).GetBlocksOfType(X);
			if (X.Count == 0) {
				Log("Could not find pistons in \"X\" group.");
				yield break;
			}
			GridTerminalSystem.GetBlockGroupWithName(PistonGroupY).GetBlocksOfType(Y);
			if (Y.Count == 0) {
				Log("Could not find pistons in \"Y\" group.");
				yield break;
			}
			GridTerminalSystem.GetBlockGroupWithName(PistonGroupZ).GetBlocksOfType(Z);
			if (Z.Count == 0) {
				Log("Could not find pistons in \"Z\" group.");
				yield break;
			}
			yield return true;

			if (!CalculateGridCargo()) {
				yield break;
			}

			yield return true;

			var grinders = new List<IMyShipGrinder>();
			GridTerminalSystem.GetBlocksOfType(grinders);
			if (grinders.Count != 1) {
				Log("Could not find exactly 1 grinder on the grid.");
				yield break;
			}
			grinder = grinders[0];
			grinder.Enabled = false;

			var sensors = new List<IMySensorBlock>();
			GridTerminalSystem.GetBlocksOfType(sensors);
			if (sensors.Count != 1) {
				Log("Could not find exactly 1 sensor on the grid.");
				yield break;
			}
			sensor = sensors[0];

			Log("No obvious problems. Continuing.");

			yield return true;

			// extend slightly outside the block to minimize clang
			sensor.FrontExtend = 0;
			sensor.LeftExtend = 0.8F;
			sensor.RightExtend = 0.8F;
			sensor.TopExtend = 0.8F;
			sensor.BottomExtend = 0.8F;
			sensor.BackExtend = 5.1F;

			sensor.DetectAsteroids = false;
			sensor.DetectEnemy = true;
			sensor.DetectFloatingObjects = true;
			sensor.DetectFriendly = true;
			sensor.DetectLargeShips = true;
			sensor.DetectNeutral = true;
			sensor.DetectOwner = true;
			sensor.DetectPlayers = false;
			sensor.DetectSmallShips = true;
			sensor.DetectStations = true;
			sensor.DetectSubgrids = false;

			yield return true;

			Log("Retracting Y");
			foreach (var i in MaximallyRetractAndPrepareToExtend(Y)) {
				yield return i;
			}

			Log("Extending Z");
			foreach (var i in MaximallyExtendAndPrepareToRetract(Z)) {
				yield return i;
			}

			Log("Retracting X");
			foreach (var i in MaximallyRetractAndPrepareToExtend(X)) {
				yield return i;
			}

			Runtime.UpdateFrequency |= UpdateFrequency.Update100;
			Log("Initialized.");

			yield return true;
		}

		private IEnumerable<bool> ExtendYToGrind() {
			foreach (var piston in Y) {
				piston.MaxLimit = piston.HighestPosition;
				piston.Velocity = pistonVelocity;
			}
			yield return true;
		}

		private IEnumerable<bool> Finished() {
			Log("Done grinding.");
			grinder.Enabled = false;
			sensor.Enabled = false;
			yield break;
		}

		private bool CalculateGridCargo() {
			GridTerminalSystem.GetBlocksOfType(cargoContainers);
			if (cargoContainers.Count == 0) {
				Log("Could not find cargo containers.");
				return false;
			}
			gridCargoCapacity = MyFixedPoint.Zero;
			foreach (var container in cargoContainers) {
				gridCargoCapacity = MyFixedPoint.AddSafe(gridCargoCapacity, container.GetInventory().MaxVolume);
			}
			gridCargoCapacity = MyFixedPoint.MultiplySafe(gridCargoCapacity, fillMax);
			return true;
		}

		private IEnumerable<bool> MaximallyExtendAndPrepareToRetract(List<IMyPistonBase> pistons) {
			foreach (var piston in pistons) {
				piston.Velocity = pistonVelocity;
				piston.MaxLimit = piston.HighestPosition;
				piston.MinLimit = piston.HighestPosition;
			}
			yield return true;

			while (pistons.Any(p => p.CurrentPosition != p.HighestPosition)) {
				yield return true;
			}

			foreach (var piston in pistons) {
				piston.Velocity = -pistonVelocity;
			}
			yield return true;
		}

		private IEnumerable<bool> MaximallyRetractAndPrepareToExtend(List<IMyPistonBase> pistons) {
			foreach (var piston in pistons) {
				piston.Velocity = -pistonVelocity;
				piston.MaxLimit = piston.LowestPosition;
				piston.MinLimit = piston.LowestPosition;
			}
			yield return true;

			while (pistons.Any(p => p.CurrentPosition != p.LowestPosition)) {
				yield return true;
			}

			foreach (var piston in pistons) {
				piston.Velocity = pistonVelocity;
			}
			yield return true;
		}

		private IEnumerable<bool> IncreasePiston(IMyPistonBase piston, float distance) {
			var newMaxLimit = Math.Min(piston.HighestPosition, piston.MaxLimit + distance);
			piston.MaxLimit = newMaxLimit;

			foreach (var i in WaitForPositionChange(piston, newMaxLimit)) {
				yield return i;
			}
		}

		private IEnumerable<bool> DecreasePiston(IMyPistonBase piston, float distance) {
			var newMinLimit = Math.Max(piston.LowestPosition, piston.MinLimit - distance);
			piston.MinLimit = newMinLimit;

			foreach (var i in WaitForPositionChange(piston, newMinLimit)) {
				yield return i;
			}
		}

		private IEnumerable<bool> WaitForPositionChange(IMyPistonBase piston, float target) {
			while (Math.Abs(piston.CurrentPosition - target) > 0.0001) {
				yield return true;
			}
			yield return true;
		}

		private void Log(string text, bool firstLog = false) {
			var surface = Me.GetSurface(0);
			surface.ContentType = ContentType.TEXT_AND_IMAGE;
			surface.WriteText(text, append: !firstLog);
			surface.WriteText(Environment.NewLine, append: true);
		}
	}
}
